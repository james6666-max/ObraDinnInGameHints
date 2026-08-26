$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = 'C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe'
$outputDirectory = Join-Path $projectRoot 'obj\tests'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$output = Join-Path $outputDirectory 'LogicTests.exe'
$sources = @(
    (Join-Path $projectRoot 'src\ObraDinnInGameHints\Models.cs'),
    (Join-Path $projectRoot 'src\ObraDinnInGameHints\HintEngine.cs'),
    (Join-Path $projectRoot 'src\ObraDinnInGameHints\HintStateStore.cs'),
    (Join-Path $projectRoot 'src\ObraDinnInGameHints\UiPresentation.cs'),
    (Join-Path $projectRoot 'tests\LogicTests.cs')
)
& $compiler /nologo /target:exe /out:$output /reference:System.dll /reference:System.Core.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Logic test compilation failed.' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'Logic tests failed.' }
& (Join-Path $PSScriptRoot 'Test-HintData.ps1')
