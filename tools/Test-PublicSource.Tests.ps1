[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$guard = Join-Path $PSScriptRoot 'Test-PublicSource.ps1'
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('obra-public-guard-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $testRoot)
$checks = 0

function Invoke-TestGit {
    param([string[]]$GitArgs)
    & git -C $testRoot @GitArgs 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Synthetic repository setup failed.' }
}
function Assert-Guard {
    param([string]$Name, [bool]$ShouldPass, [string]$ExpectedPath = '')
    $before = (& git -C $testRoot status --porcelain=v1 --untracked-files=all) -join "`n"
    $output = New-Object 'System.Collections.Generic.List[string]'
    $passed = $true
    try { & $guard -RepositoryPath $testRoot | ForEach-Object { $output.Add([string]$_) } }
    catch { $passed = $false }
    $after = (& git -C $testRoot status --porcelain=v1 --untracked-files=all) -join "`n"
    if ($before -cne $after) { throw "Guard altered working tree or index: $Name" }
    if ($passed -ne $ShouldPass) { throw "Unexpected guard result: $Name" }
    $text = $output -join "`n"
    if ($ExpectedPath -and -not $text.Contains($ExpectedPath)) { throw "Missing path diagnostic: $Name" }
    if ($text.Contains('SYNTHETIC_PRIVATE_SENTINEL')) { throw 'Guard disclosed file content.' }
    $script:checks++
    Write-Output "PASS: $Name"
}

try {
    Invoke-TestGit @('init', '--quiet')
    Invoke-TestGit @('config', 'user.name', 'Public guard test')
    Invoke-TestGit @('config', 'user.email', 'test@example.invalid')
    [System.IO.File]::WriteAllText((Join-Path $testRoot 'README.md'), 'Synthetic source')
    Assert-Guard 'Clean unborn repository and new source' $true
    Invoke-TestGit @('add', 'README.md')
    Invoke-TestGit @('-c', 'commit.gpgsign=false', 'commit', '--quiet', '-m', 'Synthetic source')
    [System.IO.File]::WriteAllText((Join-Path $testRoot '.gitignore'), "private_hint_source/`n*.dll`n")
    [void](New-Item -ItemType Directory -Path (Join-Path $testRoot 'private_hint_source'))
    [System.IO.File]::WriteAllText((Join-Path $testRoot 'private_hint_source/faces_data.json'), 'SYNTHETIC_PRIVATE_SENTINEL')
    Assert-Guard 'Ignored local data is allowed' $true
    Invoke-TestGit @('add', '-f', 'private_hint_source/faces_data.json')
    Assert-Guard 'Forced tracked private data is rejected' $false 'private_hint_source/faces_data.json'
    Invoke-TestGit @('-c', 'commit.gpgsign=false', 'commit', '--quiet', '-m', 'Synthetic prohibited data')
    Invoke-TestGit @('rm', '--quiet', 'private_hint_source/faces_data.json')
    Invoke-TestGit @('-c', 'commit.gpgsign=false', 'commit', '--quiet', '-m', 'Synthetic removal')
    Assert-Guard 'Deleted historical private data is rejected' $false 'History:'
    # Use a fresh orphan branch then delete the old ref so remaining cases have clean history.
    Invoke-TestGit @('checkout', '--quiet', '--orphan', 'clean-tests')
    Invoke-TestGit @('-c', 'commit.gpgsign=false', 'commit', '--quiet', '-m', 'Synthetic clean root')
    $branches = @(& git -C $testRoot for-each-ref '--format=%(refname:short)' refs/heads)
    foreach ($branch in $branches) {
        if ($branch -ne 'clean-tests') { Invoke-TestGit @('branch', '-D', $branch) }
    }
    Assert-Guard 'Unreachable history is outside published refs' $true
    foreach ($name in @('.env', '.env.production', 'id_ed25519', 'server.pem', 'state.json', 'state.json.corrupt-20261012.json', 'source-provenance.json', 'game.log', 'bundle.zip', 'Assembly-CSharp.dll')) {
        [System.IO.File]::WriteAllText((Join-Path $testRoot $name), 'SYNTHETIC_PRIVATE_SENTINEL')
        if ($name.EndsWith('.dll')) { Invoke-TestGit @('add', '-f', $name) }
        Assert-Guard "Prohibited path $name" $false $name
        if ($name.EndsWith('.dll')) { Invoke-TestGit @('reset', '--quiet', 'HEAD', '--', $name) }
        Remove-Item -LiteralPath (Join-Path $testRoot $name)
    }
    Assert-Guard 'Read-only guard leaves clean source accepted' $true
    Write-Output "Public-source guard tests passed: $checks checks."
}
finally {
    # Only remove this run's uniquely named, verified temporary workspace.
    $resolved = [System.IO.Path]::GetFullPath($testRoot)
    $temp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($temp, [System.StringComparison]::OrdinalIgnoreCase) -or
        [System.IO.Path]::GetFileName($resolved) -notmatch '^obra-public-guard-[0-9a-f]{32}$') {
        throw 'Refusing cleanup outside the synthetic test workspace.'
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
