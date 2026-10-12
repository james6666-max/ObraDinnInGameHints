$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('ObraDinnHintDataTests-' + [Guid]::NewGuid().ToString('N'))
$sourceRoot = Join-Path $testRoot 'source'
$outputPath = Join-Path $testRoot 'hints.dat'
$compiler = Join-Path $PSScriptRoot 'Compile-HintData.ps1'

function Set-TestData([string]$Faces, [string]$Mapping = '{"face_01.png":1}') {
    [IO.File]::WriteAllText((Join-Path $sourceRoot 'faces_data.json'), $Faces, [Text.Encoding]::UTF8)
    [IO.File]::WriteAllText((Join-Path $sourceRoot 'correct_name_list.json'), $Mapping, [Text.Encoding]::UTF8)
}

function Read-HintList([IO.BinaryReader]$Reader) {
    $count = $Reader.ReadInt32()
    if ($count -lt 0 -or $count -gt 100) { throw 'Unexpected hint count.' }
    for ($i = 0; $i -lt $count; $i++) { $Reader.ReadString() }
}

function Read-Records {
    $file = [IO.File]::OpenRead($outputPath)
    $deflate = New-Object IO.Compression.DeflateStream($file, [IO.Compression.CompressionMode]::Decompress)
    $reader = New-Object IO.BinaryReader($deflate, [Text.Encoding]::UTF8)
    try {
        if ($reader.ReadString() -ne 'ODIH2') { throw 'Unexpected hint resource header.' }
        $count = $reader.ReadInt32()
        for ($i = 0; $i -lt $count; $i++) {
            [pscustomobject]@{
                Face = $reader.ReadString()
                CrewIndex = $reader.ReadInt32()
                Identity = @(Read-HintList $reader)
                Fate = @(Read-HintList $reader)
            }
        }
        if ($deflate.ReadByte() -ne -1) { throw 'Unexpected trailing resource data.' }
    }
    finally {
        $reader.Dispose()
        $deflate.Dispose()
        $file.Dispose()
    }
}

function Assert-Rejected([string]$Name, [string]$Faces, [string]$Mapping = '{"face_01.png":1}') {
    Set-TestData $Faces $Mapping
    $before = [Convert]::ToBase64String([IO.File]::ReadAllBytes($outputPath))
    $rejected = $false
    try { & $compiler -SourceDirectory $sourceRoot -OutputPath $outputPath | Out-Null }
    catch { $rejected = $true }
    if (-not $rejected) { throw "Invalid input was accepted: $Name" }
    if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($outputPath)) -cne $before) {
        throw "Existing output was damaged: $Name"
    }
    Write-Output "PASS rejected $Name and preserved existing output"
}

try {
    [void][IO.Directory]::CreateDirectory($sourceRoot)
    Set-TestData '{"face_01.png":{"identity_hints":["first","second"]},"face_02.png":{"fate_hints":["test"]}}' '{"face_01.png":1,"face_02.png":2}'
    & $compiler -SourceDirectory $sourceRoot -OutputPath $outputPath | Out-Null
    $records = @(Read-Records)
    if ($records.Count -ne 2 -or $records[0].Face -cne 'face_01' -or $records[1].Face -cne 'face_02' -or
        $records[0].CrewIndex -ne 0 -or $records[1].CrewIndex -ne 1 -or
        $records[0].Identity.Count -ne 2 -or $records[0].Identity[0] -cne 'first' -or
        $records[0].Identity[1] -cne 'second' -or $records[0].Fate.Count -ne 0 -or
        $records[1].Identity.Count -ne 0 -or $records[1].Fate.Count -ne 1 -or $records[1].Fate[0] -cne 'test') {
        throw 'Crew index conversion, tier order, or missing hint lists were serialized incorrectly.'
    }
    Write-Output 'PASS one-based crew mapping, tier order, and absent lists'

    # Replacing an existing resource must also support a bare relative output filename.
    Set-TestData '{"face_01.png":{"identity":"legacy identity","fate_hints":null,"fate":"legacy fate"},"face_02.png":{"identity_hints":[],"identity":"empty list fallback","fate_hints":["preferred"],"fate":"ignored"}}' '{"face_01.png":1,"face_02.png":2}'
    Push-Location $testRoot
    try { & $compiler -SourceDirectory $sourceRoot -OutputPath 'hints.dat' | Out-Null }
    finally { Pop-Location }
    $records = @(Read-Records)
    if ($records.Count -ne 2 -or $records[0].Identity.Count -ne 1 -or $records[0].Fate.Count -ne 1 -or
        $records[0].Identity[0] -cne 'legacy identity' -or $records[0].Fate[0] -cne 'legacy fate' -or
        $records[1].Identity.Count -ne 1 -or $records[1].Identity[0] -cne 'empty list fallback' -or
        $records[1].Fate.Count -ne 1 -or $records[1].Fate[0] -cne 'preferred') {
        throw 'Legacy fallback or explicit tier precedence failed.'
    }
    Write-Output 'PASS absent/null/empty list fallback, explicit precedence, and relative output replacement'

    Assert-Rejected 'empty record' '{"face_01.png":{}}'
    Assert-Rejected 'null lists' '{"face_01.png":{"identity_hints":null,"fate_hints":null}}'
    Assert-Rejected 'null tier' '{"face_01.png":{"identity_hints":[null]}}'
    Assert-Rejected 'blank tier' '{"face_01.png":{"identity_hints":[" "]}}'
    Assert-Rejected 'numeric tier' '{"face_01.png":{"identity_hints":[123]}}'
    Assert-Rejected 'object tier' '{"face_01.png":{"identity_hints":[{}]}}'
    Assert-Rejected 'scalar tier list' '{"face_01.png":{"identity_hints":"test"}}'
    Assert-Rejected 'invalid fallback' '{"face_01.png":{"identity":["test"]}}'
    Assert-Rejected 'null record' '{"face_01.png":null}'
    Assert-Rejected 'array root' '[{"face_01.png":{"identity":"test"}}]'
    Assert-Rejected 'empty root' '{}'
    Assert-Rejected 'malformed JSON' '{'
    Assert-Rejected 'missing mapping' '{"face_01.png":{"identity":"test"}}' '{}'
    Assert-Rejected 'invalid mapping root' '{"face_01.png":{"identity":"test"}}' '[]'
    Assert-Rejected 'zero crew number' '{"face_01.png":{"identity":"test"}}' '{"face_01.png":0}'
    Assert-Rejected 'fractional crew number' '{"face_01.png":{"identity":"test"}}' '{"face_01.png":1.5}'
    Assert-Rejected 'duplicate crew mapping' '{"face_01.png":{"identity":"test"},"face_02.png":{"identity":"test"}}' '{"face_01.png":1,"face_02.png":1}'
    Assert-Rejected 'duplicate face mapping' '{"face_01.png":{"identity":"test"},"face_01.jpg":{"identity":"test"}}' '{"face_01.png":1,"face_01.jpg":2}'
    $manyTiers = @{ 'face_01.png' = @{ identity_hints = @('test') * 101 } } | ConvertTo-Json -Depth 4 -Compress
    Assert-Rejected 'excessive tier count' $manyTiers
    $manyRecords = @{}
    for ($i = 0; $i -lt 1001; $i++) { $manyRecords["face_$i.png"] = @{ identity = 'test' } }
    Assert-Rejected 'excessive record count' ($manyRecords | ConvertTo-Json -Depth 4 -Compress)
}
finally {
    # Delete only the exact temporary directory created by this test run.
    if (Test-Path -LiteralPath $testRoot) { [IO.Directory]::Delete($testRoot, $true) }
}
