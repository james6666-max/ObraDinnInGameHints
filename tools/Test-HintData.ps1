$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('ObraDinnHintDataTests-' + [Guid]::NewGuid().ToString('N'))
$sourceRoot = Join-Path $testRoot 'source'
$outputPath = Join-Path $testRoot 'hints.dat'

function Read-HintList([IO.BinaryReader]$reader) {
    $count = $reader.ReadInt32()
    for ($i = 0; $i -lt $count; $i++) { [void]$reader.ReadString() }
}

try {
    [void][IO.Directory]::CreateDirectory($sourceRoot)
    [IO.File]::WriteAllText(
        (Join-Path $sourceRoot 'faces_data.json'),
        '{"face_01.png":{"identity_hints":["test"]},"face_02.png":{"fate_hints":["test"]}}',
        [Text.Encoding]::UTF8)
    [IO.File]::WriteAllText(
        (Join-Path $sourceRoot 'correct_name_list.json'),
        '{"face_01.png":1,"face_02.png":2}',
        [Text.Encoding]::UTF8)

    & (Join-Path $PSScriptRoot 'Compile-HintData.ps1') -SourceDirectory $sourceRoot -OutputPath $outputPath | Out-Null

    $file = [IO.File]::OpenRead($outputPath)
    $deflate = New-Object IO.Compression.DeflateStream($file, [IO.Compression.CompressionMode]::Decompress)
    $reader = New-Object IO.BinaryReader($deflate, [Text.Encoding]::UTF8)
    try {
        if ($reader.ReadString() -ne 'ODIH2' -or $reader.ReadInt32() -ne 2) {
            throw 'Unexpected hint resource header.'
        }

        [void]$reader.ReadString()
        $firstIndex = $reader.ReadInt32()
        Read-HintList $reader
        Read-HintList $reader
        [void]$reader.ReadString()
        $secondIndex = $reader.ReadInt32()

        if ($firstIndex -ne 0 -or $secondIndex -ne 1) {
            throw 'One-based crew numbers were not converted to zero-based manifest indices.'
        }
    }
    finally {
        $reader.Dispose()
        $deflate.Dispose()
        $file.Dispose()
    }

    Write-Output 'PASS one-based crew mapping conversion'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        [IO.Directory]::Delete($testRoot, $true)
    }
}
