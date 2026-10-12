[CmdletBinding()]
param([string]$Destination)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $Destination) { $Destination = Join-Path $projectRoot 'work\private_hint_source' }
$destinationPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination)
$repository = 'https://github.com/Yide-Zhang/ObraDinn-HintsAndCheck'
$revision = 'dcfa6c557e01078e449ae753967fc752844367f3'
$files = @(
    @{ Name = 'faces_data.json'; Hash = 'AF767AFFBD0765EE237C98552D7E39C34389758C422443479D0527CE6DA689FF' },
    @{ Name = 'correct_name_list.json'; Hash = '2A5A64092388545F2D0F7B0866E4E35F53C4C04EF1BC746F95BF0A7D9159AC5D' }
)

Write-Output "Hint data author: @Yide-Zhang ($repository)"
Write-Output 'Downloads third-party data for local use only. No redistribution permission is implied.'
foreach ($file in $files) {
    $path = Join-Path $destinationPath $file.Name
    if (Test-Path -LiteralPath $path) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.Hash) {
            throw 'Existing hint data differs from the pinned source. Preserve it and choose another -Destination; nothing was overwritten.'
        }
    }
}

[void][IO.Directory]::CreateDirectory($destinationPath)
$pending = @()
$originalProtocol = [Net.ServicePointManager]::SecurityProtocol
try {
    [Net.ServicePointManager]::SecurityProtocol = $originalProtocol -bor [Net.SecurityProtocolType]::Tls12
    foreach ($file in $files) {
        $path = Join-Path $destinationPath $file.Name
        if (Test-Path -LiteralPath $path -PathType Leaf) { continue }
        $temporary = Join-Path $destinationPath ([Guid]::NewGuid().ToString('N') + '.tmp')
        $pending += @{ Temporary = $temporary; Target = $path }
        $uri = "https://raw.githubusercontent.com/Yide-Zhang/ObraDinn-HintsAndCheck/$revision/$($file.Name)"
        Invoke-WebRequest -UseBasicParsing -Uri $uri -OutFile $temporary -TimeoutSec 60
        if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $file.Hash) {
            throw 'Downloaded hint data failed SHA256 verification. Existing files were preserved.'
        }
    }
    # Publish only after every download passed; File.Move refuses to overwrite existing data.
    foreach ($item in $pending) { [IO.File]::Move($item.Temporary, $item.Target) }
    $provenance = Join-Path $destinationPath 'source-provenance.json'
    if (-not (Test-Path -LiteralPath $provenance)) {
        @{ repository = $repository; commit = $revision; downloadedAt = [DateTimeOffset]::Now.ToString('o');
            files = @($files | ForEach-Object { @{ name = $_.Name; sha256 = $_.Hash } })
        } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $provenance -Encoding UTF8
    }
}
finally {
    [Net.ServicePointManager]::SecurityProtocol = $originalProtocol
    foreach ($item in $pending) {
        if (Test-Path -LiteralPath $item.Temporary -PathType Leaf) { Remove-Item -LiteralPath $item.Temporary }
    }
}
Write-Output "Verified both hint data files at revision $revision."
Write-Output "Local data: $destinationPath"
