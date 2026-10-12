[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Import-Module Microsoft.PowerShell.Utility
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('obra-install-' + [Guid]::NewGuid().ToString('N'))
$fixtureState = @{ Running = $false; FailInstalledHash = $false }
$checks = 0
function Assert-Test([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
    Write-Output "PASS: $Message"
}
# Real file transactions run only in a disposable fixture. Mock system prerequisites
# and process state so no installed game, compiler, or downloaded data is needed.
function Test-Path {
    param([string]$LiteralPath, [string]$PathType)
    if ($LiteralPath -eq 'C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe') { return $true }
    if ($PathType) { Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath -PathType $PathType }
    else { Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath }
}
function Get-Process {
    [CmdletBinding()] param([string]$Name)
    if ($fixtureState.Running) { [pscustomobject]@{ ProcessName = 'ObraDinn' } }
}
function Get-FileHash {
    param([string]$LiteralPath, [string]$Algorithm)
    if ($fixtureState.FailInstalledHash -and $LiteralPath -eq $fixtureState.Target) { return [pscustomobject]@{ Hash = 'incorrect' } }
    Microsoft.PowerShell.Utility\Get-FileHash -LiteralPath $LiteralPath -Algorithm $Algorithm
}
function Invoke-Fixture([bool]$ShouldPass, [hashtable]$ExtraArguments = @{}) {
    $passed = $true
    try { & $installer -GamePath $game -PrivateDataDirectory $data @ExtraArguments | Out-Null }
    catch { $passed = $false }
    Assert-Test ($passed -eq $ShouldPass) "Installer expected success=$ShouldPass ($($ExtraArguments.Keys -join ', '))"
}
try {
    [void][IO.Directory]::CreateDirectory((Join-Path $testRoot 'tools'))
    $installer = Join-Path $testRoot 'tools\Install.ps1'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install.ps1') -Destination $installer
    $game = Join-Path $testRoot 'game'
    $data = Join-Path $testRoot 'data'
    $plugin = Join-Path $game 'BepInEx\plugins\ObraDinnInGameHints'
    $destination = Join-Path $plugin 'ObraDinnInGameHints.dll'
    $fixtureState.Target = $destination
    $fixtureFiles = @('ObraDinn.exe', 'BepInEx\core\BepInEx.dll', 'BepInEx\core\0Harmony.dll')
    foreach ($name in @('Assembly-CSharp.dll', 'UnityEngine.dll', 'UnityEngine.CoreModule.dll', 'UnityEngine.UIModule.dll',
        'UnityEngine.UI.dll', 'UnityEngine.TextRenderingModule.dll', 'UnityEngine.ImageConversionModule.dll', 'UnityEngine.AudioModule.dll')) {
        $fixtureFiles += 'ObraDinn_Data\Managed\' + $name
    }
    foreach ($name in $fixtureFiles) {
        $path = Join-Path $game $name
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
        [IO.File]::WriteAllText($path, 'fixture')
    }
    [void][IO.Directory]::CreateDirectory($data)
    foreach ($name in @('faces_data.json', 'correct_name_list.json')) { [IO.File]::WriteAllText((Join-Path $data $name), '{}') }
    $build = Join-Path $testRoot 'tools\Build.ps1'
    $buildContent = @'
param($GamePath, $PrivateDataDirectory, $DuckIconSource)
$root = Split-Path -Parent $PSScriptRoot
[IO.File]::WriteAllText((Join-Path $root 'build-called'), 'yes')
$dist = Join-Path $root 'dist\ObraDinnInGameHints'
[void][IO.Directory]::CreateDirectory($dist)
[IO.File]::WriteAllText((Join-Path $dist 'ObraDinnInGameHints.dll'), 'new-plugin')
'@
    [IO.File]::WriteAllText($build, $buildContent)
    [IO.File]::WriteAllText((Join-Path $testRoot 'tools\Get-HintData.ps1'), @'
param($Destination)
[IO.File]::WriteAllText((Join-Path (Split-Path -Parent $PSScriptRoot) 'download-called'), 'yes')
'@)
    Invoke-Fixture $true @{ CheckOnly = $true; DownloadHintData = $true }
    Assert-Test (-not (Test-Path -LiteralPath (Join-Path $testRoot 'build-called'))) 'CheckOnly skips build'
    Assert-Test (-not (Test-Path -LiteralPath (Join-Path $testRoot 'download-called'))) 'CheckOnly skips download even when requested'
    Assert-Test (-not (Test-Path -LiteralPath $plugin)) 'CheckOnly creates no plugin directory'
    Remove-Item -LiteralPath (Join-Path $data 'faces_data.json')
    Invoke-Fixture $false @{ CheckOnly = $true }
    Invoke-Fixture $false
    Assert-Test (-not (Test-Path -LiteralPath (Join-Path $testRoot 'download-called'))) 'Missing data never downloads implicitly'
    [IO.File]::WriteAllText((Join-Path $data 'faces_data.json'), '{}')
    $fixtureState.Running = $true
    Invoke-Fixture $false
    Assert-Test (-not (Test-Path -LiteralPath (Join-Path $testRoot 'build-called'))) 'Running game blocks build and installation'
    $fixtureState.Running = $false
    Invoke-Fixture $true @{ DownloadHintData = $true }
    Assert-Test (Test-Path -LiteralPath (Join-Path $testRoot 'download-called')) 'Explicit download reaches downloader'
    Assert-Test ([IO.File]::ReadAllText($destination) -eq 'new-plugin') 'Fresh install copies plugin'
    Assert-Test (@(Get-ChildItem -LiteralPath $plugin -Filter '*.bak').Count -eq 0) 'Fresh install has no unnecessary backup'
    [IO.File]::WriteAllText($destination, 'old-plugin')
    $otherPlugin = Join-Path $game 'BepInEx\plugins\other.dll'
    [IO.File]::WriteAllText($otherPlugin, 'other-plugin')
    Invoke-Fixture $true
    $backups = @(Get-ChildItem -LiteralPath $plugin -Filter '*.bak')
    Assert-Test ($backups.Count -eq 1 -and [IO.File]::ReadAllText($backups[0].FullName) -eq 'old-plugin') 'Upgrade preserves previous DLL in unique backup'
    Assert-Test ([IO.File]::ReadAllText($otherPlugin) -eq 'other-plugin') 'Other plugins are preserved'
    [IO.File]::WriteAllText($destination, 'rollback-plugin')
    $fixtureState.FailInstalledHash = $true
    Invoke-Fixture $false
    Assert-Test ([IO.File]::ReadAllText($destination) -eq 'rollback-plugin') 'Checksum failure restores previous plugin'
    Assert-Test (@(Get-ChildItem -LiteralPath $plugin -Filter '*.tmp').Count -eq 0) 'Transaction cleans temporary files'
    $fixtureState.FailInstalledHash = $false
    [IO.File]::WriteAllText($build, "throw 'Synthetic build failure'")
    Invoke-Fixture $false
    Assert-Test ([IO.File]::ReadAllText($destination) -eq 'rollback-plugin') 'Build failure leaves installed plugin intact'
    [IO.File]::WriteAllText($build, 'exit 1')
    Invoke-Fixture $false
    Assert-Test ([IO.File]::ReadAllText($destination) -eq 'rollback-plugin') 'Nonzero build exit never installs a stale DLL'
    Remove-Item -LiteralPath (Join-Path $game 'ObraDinn.exe')
    Invoke-Fixture $false @{ CheckOnly = $true }
    Write-Output "Passed $checks installer checks. No real game directory was used."
}
finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolved.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notlike 'obra-install-*') { throw 'Unsafe test cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
