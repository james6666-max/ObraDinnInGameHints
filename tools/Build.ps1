param(
    [string]$GamePath = 'D:\steam\steamapps\common\ObraDinn',
    [Parameter(Mandatory = $true)][string]$PrivateDataDirectory,
    [Parameter(Mandatory = $true)][string]$DuckIconSource
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $projectRoot 'src\ObraDinnInGameHints'
$objectRoot = Join-Path $projectRoot 'obj'
$distRoot = Join-Path $projectRoot 'dist\ObraDinnInGameHints'
$managed = Join-Path $GamePath 'ObraDinn_Data\Managed'
$bepinex = Join-Path $GamePath 'BepInEx\core'
$compiler = 'C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 3.5 compiler is unavailable.' }

New-Item -ItemType Directory -Force -Path $objectRoot,$distRoot | Out-Null
$hintData = Join-Path $objectRoot 'hints.dat'
$duckIcon = Join-Path $objectRoot 'duck.png'
& (Join-Path $PSScriptRoot 'Compile-HintData.ps1') -SourceDirectory $PrivateDataDirectory -OutputPath $hintData
& (Join-Path $PSScriptRoot 'Prepare-DuckIcon.ps1') -SourcePath $DuckIconSource -OutputPath $duckIcon

$output = Join-Path $distRoot 'ObraDinnInGameHints.dll'
$references = @(
    (Join-Path $bepinex 'BepInEx.dll'),
    (Join-Path $bepinex '0Harmony.dll'),
    (Join-Path $managed 'Assembly-CSharp.dll'),
    (Join-Path $managed 'UnityEngine.dll'),
    (Join-Path $managed 'UnityEngine.CoreModule.dll'),
    (Join-Path $managed 'UnityEngine.UIModule.dll'),
    (Join-Path $managed 'UnityEngine.UI.dll'),
    (Join-Path $managed 'UnityEngine.TextRenderingModule.dll'),
    (Join-Path $managed 'UnityEngine.ImageConversionModule.dll'),
    (Join-Path $managed 'UnityEngine.AudioModule.dll')
)
foreach ($reference in $references) {
    if (-not (Test-Path -LiteralPath $reference)) { throw "Missing build reference: $reference" }
}

$arguments = @('/nologo','/target:library','/optimize+','/debug:pdbonly',("/out:$output"))
foreach ($reference in $references) { $arguments += "/reference:$reference" }
$arguments += "/resource:$hintData,ObraDinnInGameHints.Resources.hints.dat"
$arguments += "/resource:$duckIcon,ObraDinnInGameHints.Resources.duck.png"
$arguments += @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' | Sort-Object Name | ForEach-Object FullName)

& $compiler $arguments
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $output)) {
    throw 'Plugin compilation failed.'
}

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $output).Hash
Write-Output "Built: $output"
Write-Output "SHA256: $hash"
