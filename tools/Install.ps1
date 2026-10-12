[CmdletBinding()]
param(
    [string]$GamePath = 'D:\steam\steamapps\common\ObraDinn',
    [string]$PrivateDataDirectory,
    [switch]$DownloadHintData,
    [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($PrivateDataDirectory)) {
    $PrivateDataDirectory = Join-Path $projectRoot 'work\private_hint_source'
}
$GamePath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($GamePath)
$PrivateDataDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PrivateDataDirectory)
$missing = New-Object 'Collections.Generic.List[string]'
$requirements = @(
    (Join-Path $GamePath 'ObraDinn.exe'),
    (Join-Path $GamePath 'BepInEx\core\BepInEx.dll'),
    (Join-Path $GamePath 'BepInEx\core\0Harmony.dll'),
    'C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe'
)
foreach ($name in @('Assembly-CSharp.dll', 'UnityEngine.dll', 'UnityEngine.CoreModule.dll',
        'UnityEngine.UIModule.dll', 'UnityEngine.UI.dll', 'UnityEngine.TextRenderingModule.dll',
        'UnityEngine.ImageConversionModule.dll', 'UnityEngine.AudioModule.dll')) {
    $requirements += Join-Path $GamePath ('ObraDinn_Data\Managed\' + $name)
}
foreach ($path in $requirements) {
    if (Test-Path -LiteralPath $path -PathType Leaf) { Write-Output "OK: $path" }
    else { $missing.Add($path); Write-Output "MISSING: $path" }
}
$dataMissing = $false
foreach ($name in @('faces_data.json', 'correct_name_list.json')) {
    $path = Join-Path $PrivateDataDirectory $name
    if (Test-Path -LiteralPath $path -PathType Leaf) { Write-Output "OK: $path" }
    else { $dataMissing = $true; Write-Output "MISSING: $path (use -DownloadHintData to fetch the credited upstream data)" }
}
if ($missing.Count -gt 0) {
    throw 'Prerequisites missing. In Steam, choose Manage > Browse local files and pass that folder with -GamePath. Install BepInEx 5 x86 and enable Windows .NET Framework 3.5 as described in README.md, then retry.'
}
if ($CheckOnly) {
    if ($dataMissing) { throw 'Check incomplete: local hint data is missing. No files were changed.' }
    Write-Output 'Prerequisite check passed. No files were changed; data validity is checked during the build.'
    return
}
function Assert-GameStopped {
    if (@(Get-Process -Name ObraDinn -ErrorAction SilentlyContinue).Count -gt 0) {
        throw 'Close Return of the Obra Dinn before installing, then retry.'
    }
}
Assert-GameStopped
if ($DownloadHintData) {
    & (Join-Path $PSScriptRoot 'Get-HintData.ps1') -Destination $PrivateDataDirectory
}
elseif ($dataMissing) {
    throw 'Local hint data is missing. Retry with -DownloadHintData to download it, or supply -PrivateDataDirectory.'
}
& (Join-Path $PSScriptRoot 'Build.ps1') -GamePath $GamePath -PrivateDataDirectory $PrivateDataDirectory -DuckIconSource (Join-Path $projectRoot 'assets\duck-source-generated.png')
if (-not $?) { throw 'Build failed; nothing was installed.' }
$builtDll = Join-Path $projectRoot 'dist\ObraDinnInGameHints\ObraDinnInGameHints.dll'
if (-not (Test-Path -LiteralPath $builtDll -PathType Leaf)) { throw 'Build did not produce the plugin DLL; nothing was installed.' }
$expectedHash = (Get-FileHash -LiteralPath $builtDll -Algorithm SHA256).Hash
Assert-GameStopped
$pluginDirectory = Join-Path $GamePath 'BepInEx\plugins\ObraDinnInGameHints'
[void][IO.Directory]::CreateDirectory($pluginDirectory)
$destination = Join-Path $pluginDirectory 'ObraDinnInGameHints.dll'
$transactionId = [Guid]::NewGuid().ToString('N')
$staged = Join-Path $pluginDirectory ($transactionId + '.tmp')
$backup = Join-Path $pluginDirectory ('ObraDinnInGameHints.' + $transactionId + '.bak')
$hadPrevious = Test-Path -LiteralPath $destination -PathType Leaf
$installed = $false
try {
    Copy-Item -LiteralPath $builtDll -Destination $staged
    if ((Get-FileHash -LiteralPath $staged -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Staged DLL checksum mismatch.' }
    Assert-GameStopped
    if ($hadPrevious) { [IO.File]::Replace($staged, $destination, $backup) }
    else { [IO.File]::Move($staged, $destination) }
    $installed = $true
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Installed DLL checksum mismatch.' }
}
catch {
    $installError = $_
    if ($installed) {
        try {
            if ($hadPrevious) { [IO.File]::Replace($backup, $destination, [NullString]::Value) }
            else { [IO.File]::Delete($destination) }
        }
        catch { throw "Installation failed and automatic rollback failed. Recover the previous DLL from '$backup'. Error: $($_.Exception.Message)" }
    }
    throw $installError
}
finally {
    if (Test-Path -LiteralPath $staged) { Remove-Item -LiteralPath $staged -Force }
}
Write-Output "Installed: $destination"
Write-Output "SHA256: $expectedHash"
if ($hadPrevious) { Write-Output "Previous DLL backup: $backup" }
Write-Output 'Launch the game, open the journal, then press H or click the duck. Saves and other plugins were not changed.'
