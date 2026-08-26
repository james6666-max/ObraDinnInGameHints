param(
    [Parameter(Mandatory = $true)][string]$SourceDirectory,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$facesPath = Join-Path $SourceDirectory 'faces_data.json'
$mappingPath = Join-Path $SourceDirectory 'correct_name_list.json'
if (-not (Test-Path -LiteralPath $facesPath) -or -not (Test-Path -LiteralPath $mappingPath)) {
    throw 'Required private hint data files are missing.'
}

$faces = Get-Content -LiteralPath $facesPath -Raw -Encoding UTF8 | ConvertFrom-Json
$mapping = Get-Content -LiteralPath $mappingPath -Raw -Encoding UTF8 | ConvertFrom-Json
$entries = @($faces.PSObject.Properties | Sort-Object Name)
if ($entries.Count -eq 0) {
    throw 'The private hint data contains no records.'
}

$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$file = [IO.File]::Open($OutputPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
$deflate = New-Object IO.Compression.DeflateStream($file, [IO.Compression.CompressionMode]::Compress)
$writer = New-Object IO.BinaryWriter($deflate, [Text.Encoding]::UTF8)
try {
    $writer.Write('ODIH2')
    $writer.Write([int]$entries.Count)
    $seenFaces = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $seenCrew = New-Object 'Collections.Generic.HashSet[int]'

    foreach ($entry in $entries) {
        $faceId = [IO.Path]::GetFileNameWithoutExtension([string]$entry.Name)
        $crewProperty = $mapping.PSObject.Properties[$entry.Name]
        if ($null -eq $crewProperty) {
            throw 'A hint record has no game mapping.'
        }
        $crewNumber = 0
        $rawCrewNumber = [string]$crewProperty.Value
        if (-not [int]::TryParse($rawCrewNumber, [Globalization.NumberStyles]::Integer,
                [Globalization.CultureInfo]::InvariantCulture, [ref]$crewNumber) -or $crewNumber -le 0) {
            throw 'A game mapping is not a valid one-based crew number.'
        }
        # The source tool uses crew IDs 1..N, while Manifest.GetCrewId uses a zero-based List index.
        $crewIndex = $crewNumber - 1
        if (-not $seenFaces.Add($faceId) -or -not $seenCrew.Add($crewIndex)) {
            throw 'The hint data contains a duplicate mapping.'
        }

        $identity = @($entry.Value.identity_hints)
        if ($identity.Count -eq 0 -and $null -ne $entry.Value.identity) {
            $identity = @([string]$entry.Value.identity)
        }
        $fate = @($entry.Value.fate_hints)
        if ($fate.Count -eq 0 -and $null -ne $entry.Value.fate) {
            $fate = @([string]$entry.Value.fate)
        }
        if ($identity.Count -eq 0 -and $fate.Count -eq 0) {
            throw 'The hint data contains an empty record.'
        }

        $writer.Write($faceId)
        $writer.Write([int]$crewIndex)
        $writer.Write([int]$identity.Count)
        foreach ($hint in $identity) { $writer.Write([string]$hint) }
        $writer.Write([int]$fate.Count)
        foreach ($hint in $fate) { $writer.Write([string]$hint) }
    }
}
finally {
    $writer.Dispose()
    $deflate.Dispose()
    $file.Dispose()
}

Write-Output "Compiled hint records: $($entries.Count)"
