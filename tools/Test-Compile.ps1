param(
    [string]$GamePath = 'D:\steam\steamapps\common\ObraDinn'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = 'C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 3.5 compiler is unavailable.' }
$managed = Join-Path $GamePath 'ObraDinn_Data\Managed'
$core = Join-Path $GamePath 'BepInEx\core'
$references = @(
    (Join-Path $core 'BepInEx.dll'),
    (Join-Path $core '0Harmony.dll'),
    (Join-Path $managed 'Assembly-CSharp.dll')
)
foreach ($reference in $references) {
    if (-not (Test-Path -LiteralPath $reference)) { throw "Missing compile reference: $reference" }
}
$references += @(Get-ChildItem -LiteralPath $managed -Filter 'UnityEngine*.dll' | ForEach-Object FullName)
$outputDirectory = Join-Path $projectRoot 'obj\compile-check'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$output = Join-Path $outputDirectory 'ObraDinnInGameHints.compile-check.dll'
# Deliberately omit private resources: this output checks APIs, and is not an installable mod.
$arguments = @('/nologo', '/target:library', "/out:$output")
$arguments += @($references | ForEach-Object { "/reference:$_" })
$arguments += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src\ObraDinnInGameHints') -Filter '*.cs' | Sort-Object Name | ForEach-Object FullName)
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Plugin compile check failed.' }
Write-Output 'PASS plugin compilation (no private resources; do not install this DLL)'
