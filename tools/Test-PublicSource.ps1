# Path-only release guard. Does not inspect file contents, ignored files, or Git object bodies.
# Checks the index, non-ignored untracked paths, and every commit reachable from local refs.
# This is not a content/secret scanner; renamed private data still requires human review.
[CmdletBinding()]
param(
    [string]$RepositoryPath
)

$ErrorActionPreference = 'Stop'
if (-not $RepositoryPath) { $RepositoryPath = Split-Path -Parent $PSScriptRoot }
$root = (Resolve-Path -LiteralPath $RepositoryPath).Path

function Invoke-PathGuardGit {
    param([string]$Arguments)
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = 'git'
    $start.Arguments = $Arguments
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $start
    try {
        [void]$process.Start()
        $errorRead = $process.StandardError.ReadToEndAsync()
        $result = $process.StandardOutput.ReadToEnd()
        $process.WaitForExit()
        [void]$errorRead.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            # Git diagnostics may include sensitive local paths; do not echo them.
            throw 'Public-source inspection failed while reading Git metadata.'
        }
        return $result
    }
    finally { $process.Dispose() }
}

function Test-ForbiddenPublicPath {
    param([string]$Path)
    return (
        $Path -match '(?i)(^|/)(work|outputs|ANALYSIS|private_hint_source|obj|bin|dist|Debug|Release|BepInEx|Managed|saves?|backups?)(/|$)' -or
        $Path -match '(?i)(^|/)(faces_data\.json|correct_name_list\.json|source-provenance\.json|hints\.dat|state\.json|state\.json\.corrupt-[^/]*\.json|steam_appid\.txt|credentials\.json|secrets\.json|id_rsa|id_dsa|id_ecdsa|id_ed25519)(/|$)' -or
        $Path -match '(?i)(^|/)\.env($|\.)' -or
        $Path -match '(?i)\.(dll|pdb|mdb|exe|zip|7z|rar|nupkg|tar|gz|log|bak|pending|state|save|sav|pem|key|p12|pfx)$'
    )
}

$inside = Invoke-PathGuardGit 'rev-parse --is-inside-work-tree'
if ($inside.Trim() -ne 'true') { throw 'RepositoryPath must refer to a Git working tree.' }
$gitRoot = (Invoke-PathGuardGit 'rev-parse --show-toplevel').TrimEnd("`r", "`n")
$root = (Resolve-Path -LiteralPath $gitRoot).Path
$findings = New-Object 'System.Collections.Generic.HashSet[string]'
function Add-PathFindings {
    param([string]$Paths, [string]$Scope)
    foreach ($path in $Paths.Split([char]0)) {
        if ($path.Length -gt 0 -and (Test-ForbiddenPublicPath $path)) {
            # JSON escaping keeps unusual file names from injecting terminal lines.
            $safePath = ConvertTo-Json -InputObject $path -Compress
            [void]$findings.Add("${Scope}: $safePath")
        }
    }
}

Add-PathFindings (Invoke-PathGuardGit 'ls-files -z --cached --others --exclude-standard --full-name') 'Pending/index'
$commits = Invoke-PathGuardGit 'rev-list --all'
foreach ($commit in ($commits -split '\s+')) {
    if ($commit.Length -eq 0) { continue }
    if ($commit -notmatch '^[0-9a-f]{40,64}$') { throw 'Unexpected commit identifier.' }
    Add-PathFindings (Invoke-PathGuardGit "ls-tree -r -z --name-only $commit") 'History'
}

if ($findings.Count -gt 0) {
    Write-Output 'Public-source check FAILED. Prohibited paths (contents were not read):'
    $findings | Sort-Object | Write-Output
    throw 'Remove prohibited pending paths; historical findings require resolving the affected history before publication.'
}
Write-Output 'Public-source check passed: no prohibited paths in the index, non-ignored new files, or reachable local history. Content and upstream permission review are still required.'
