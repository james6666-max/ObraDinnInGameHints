param(
    [Parameter(Mandatory = $true)][string]$SourceDirectory,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'

function Read-JsonObject([string]$Path) {
    $json = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    if ([string]::IsNullOrWhiteSpace($json) -or -not $json.TrimStart().StartsWith('{')) {
        throw 'Private hint data must contain JSON objects.'
    }
    $value = ConvertFrom-Json -InputObject $json
    if ($value -isnot [pscustomobject]) { throw 'Private hint data must contain JSON objects.' }
    return $value
}

function Get-Hints($Record, [string]$ListName, [string]$FallbackName) {
    $list = $Record.PSObject.Properties[$ListName]
    $hints = @()
    if ($null -ne $list -and $null -ne $list.Value) {
        if ($list.Value -isnot [array]) { throw 'Hint tiers must be arrays of non-empty strings.' }
        $hints = @($list.Value)
    }
    if ($hints.Count -eq 0) {
        $fallback = $Record.PSObject.Properties[$FallbackName]
        if ($null -ne $fallback -and $null -ne $fallback.Value) {
            if ($fallback.Value -isnot [string]) { throw 'Legacy hints must be non-empty strings.' }
            $hints = @($fallback.Value)
        }
    }
    if ($hints.Count -gt 100) { throw 'A hint record contains too many tiers.' }
    foreach ($hint in $hints) {
        if ($hint -isnot [string] -or [string]::IsNullOrWhiteSpace($hint)) {
            throw 'Hint tiers must be non-empty strings.'
        }
        Write-Output $hint
    }
}

$facesPath = Join-Path $SourceDirectory 'faces_data.json'
$mappingPath = Join-Path $SourceDirectory 'correct_name_list.json'
if (-not (Test-Path -LiteralPath $facesPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $mappingPath -PathType Leaf)) {
    throw 'Required private hint data files are missing.'
}

$faces = Read-JsonObject $facesPath
$mapping = Read-JsonObject $mappingPath
$entries = @($faces.PSObject.Properties | Sort-Object Name)
if ($entries.Count -eq 0 -or $entries.Count -gt 1000) {
    throw 'The private hint data record count must be between 1 and 1000.'
}

$seenFaces = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$seenCrew = New-Object 'Collections.Generic.HashSet[int]'
$records = @(foreach ($entry in $entries) {
    $faceId = [IO.Path]::GetFileNameWithoutExtension([string]$entry.Name)
    if ([string]::IsNullOrWhiteSpace($faceId) -or $entry.Value -isnot [pscustomobject]) {
        throw 'A hint record has an invalid identifier or value.'
    }
    $crewProperty = $mapping.PSObject.Properties[$entry.Name]
    if ($null -eq $crewProperty) { throw 'A hint record has no game mapping.' }
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
    $identity = @(Get-Hints $entry.Value 'identity_hints' 'identity')
    $fate = @(Get-Hints $entry.Value 'fate_hints' 'fate')
    if ($identity.Count -eq 0 -and $fate.Count -eq 0) { throw 'The hint data contains an empty record.' }
    [pscustomobject]@{ FaceId = $faceId; CrewIndex = $crewIndex; Identity = $identity; Fate = $fate }
})

# Validate everything first, then publish a complete resource from the same directory.
$resolvedOutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
$outputDirectory = [IO.Path]::GetDirectoryName($resolvedOutputPath)
[void][IO.Directory]::CreateDirectory($outputDirectory)
$tempPath = Join-Path $outputDirectory ([IO.Path]::GetRandomFileName())
$file = $null
$deflate = $null
$writer = $null
$tempCreated = $false
try {
    $file = [IO.File]::Open($tempPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $tempCreated = $true
    $deflate = New-Object IO.Compression.DeflateStream($file, [IO.Compression.CompressionMode]::Compress)
    $writer = New-Object IO.BinaryWriter($deflate, [Text.Encoding]::UTF8)
    $writer.Write('ODIH2')
    $writer.Write([int]$records.Count)
    foreach ($record in $records) {
        $writer.Write([string]$record.FaceId)
        $writer.Write([int]$record.CrewIndex)
        $writer.Write([int]$record.Identity.Count)
        foreach ($hint in $record.Identity) { $writer.Write([string]$hint) }
        $writer.Write([int]$record.Fate.Count)
        foreach ($hint in $record.Fate) { $writer.Write([string]$hint) }
    }
    $writer.Dispose()
    $deflate.Dispose()
    $file.Dispose()
    if ([IO.File]::Exists($resolvedOutputPath)) {
        [IO.File]::Replace($tempPath, $resolvedOutputPath, [NullString]::Value)
    }
    else { [IO.File]::Move($tempPath, $resolvedOutputPath) }
}
finally {
    if ($null -ne $writer) { $writer.Dispose() }
    if ($null -ne $deflate) { $deflate.Dispose() }
    if ($null -ne $file) { $file.Dispose() }
    if ($tempCreated -and [IO.File]::Exists($tempPath)) { [IO.File]::Delete($tempPath) }
}

Write-Output "Compiled hint records: $($records.Count)"
