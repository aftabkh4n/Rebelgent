<#
.SYNOPSIS
    Detects legitimate source or configuration files under src/ and tests/ that
    Git ignores unexpectedly.

.DESCRIPTION
    Some entries in .gitignore (for example the standard [Rr]elease/ build-output
    rule) can accidentally hide real source directories. When that happens the
    working copy of a normal contributor still compiles, but a fresh clone or
    worktree fails because the ignored files never leave the original machine.

    This script walks src/ and tests/ for files whose extension marks them as
    source or build-configuration input (.cs, .csproj, .props, .targets, .json,
    .config, .resx, .xaml). For every such file it consults `git check-ignore`.
    If Git would ignore the file and the file is not on the allow list, the
    script writes an error and exits with a non-zero status.

    The script does not scan bin/, obj/, TestResults/, or other well-known
    generated output. It does not touch directories outside the repository.

.PARAMETER RepositoryRoot
    Optional path to the repository root. Defaults to the parent of the folder
    that contains this script.

.EXAMPLE
    pwsh -File scripts/verify-repository-sources.ps1

.EXAMPLE
    pwsh -File scripts/verify-repository-sources.ps1 -RepositoryRoot C:\src\Rebelgent

.NOTES
    Exit code 0 = all source files are tracked or explicitly allowed.
    Exit code 1 = one or more legitimate source files are ignored.
    Exit code 2 = execution failed (missing git, invalid path, etc.).
#>

[CmdletBinding()]
param(
    [string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'

if (-not $RepositoryRoot) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $RepositoryRoot = Split-Path -Parent $scriptDir
}

if (-not (Test-Path $RepositoryRoot)) {
    Write-Error "Repository root not found: $RepositoryRoot"
    exit 2
}

$gitCmd = Get-Command git -ErrorAction SilentlyContinue
if (-not $gitCmd) {
    Write-Error "git is not on PATH. Install git or add it to PATH before running this script."
    exit 2
}

Push-Location $RepositoryRoot
try {
    # Extensions we treat as legitimate source or build-config input.
    $sourceExtensions = @(
        '.cs', '.csproj', '.props', '.targets',
        '.json', '.config', '.resx', '.xaml',
        '.sln', '.slnx', '.editorconfig', '.ruleset'
    )

    # Explicit allow list. Add entries when an ignored file is intentional.
    # Paths are repository-relative and use forward slashes.
    $allowedIgnored = @(
        '.claude/settings.local.json'
    )

    $scanRoots = @('src', 'tests')

    $candidates = New-Object System.Collections.Generic.List[string]

    foreach ($root in $scanRoots) {
        $rootPath = Join-Path $RepositoryRoot $root
        if (-not (Test-Path $rootPath)) { continue }

        Get-ChildItem -Path $rootPath -Recurse -File -Force |
            Where-Object {
                $relative = $_.FullName.Substring($RepositoryRoot.Length + 1).Replace('\', '/')
                $skip = $relative -match '(^|/)(bin|obj|TestResults|node_modules|\.vs)(/|$)'
                if ($skip) { return $false }
                return ($sourceExtensions -contains $_.Extension.ToLowerInvariant())
            } |
            ForEach-Object {
                $relative = $_.FullName.Substring($RepositoryRoot.Length + 1).Replace('\', '/')
                $candidates.Add($relative) | Out-Null
            }
    }

    if ($candidates.Count -eq 0) {
        Write-Host "No source files found under $($scanRoots -join ', '). Nothing to verify."
        exit 0
    }

    # Feed candidates through `git check-ignore` in one pass.
    $ignored = $candidates | & git check-ignore --stdin 2>$null

    if (-not $ignored) {
        Write-Host "OK. $($candidates.Count) source files scanned. None are ignored by Git."
        exit 0
    }

    $violations = @()
    foreach ($path in ($ignored -split "`r?`n")) {
        if (-not $path) { continue }
        $normalized = $path.Trim().Replace('\', '/')
        if ($allowedIgnored -contains $normalized) { continue }
        $violations += $normalized
    }

    if ($violations.Count -eq 0) {
        Write-Host "OK. $($candidates.Count) source files scanned. All ignored entries are on the allow list."
        exit 0
    }

    Write-Host ""
    Write-Host "Repository integrity check FAILED." -ForegroundColor Red
    Write-Host "The following legitimate source files are ignored by Git:" -ForegroundColor Red
    foreach ($v in $violations) {
        Write-Host "  $v" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "Fix the .gitignore rule (rename the directory, add a negation with !path, or narrow the pattern)."
    Write-Host "If an ignored file is intentional, add its repository-relative path to `$allowedIgnored in scripts/verify-repository-sources.ps1."
    exit 1
}
finally {
    Pop-Location
}
