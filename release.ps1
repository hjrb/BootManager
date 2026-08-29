<#
.SYNOPSIS
    Cuts a release by tagging the current commit on main and pushing the tag.

.DESCRIPTION
    Pushing a tag of the form v1.2.3 starts the 'Release' workflow, which builds every target,
    publishes them as a GitHub release, and then deletes the previous release. Because that
    deletion is not reversible, this script refuses to tag anything that is not a clean, fully
    pushed main.

    It only creates the tag. Everything after that happens on GitHub.

.PARAMETER Version
    The version to release, without the leading 'v', for example '1.2.3'.

.PARAMETER SkipChecks
    Tag even if the working tree is dirty, the branch is not main, or CI is not green.
    Only for repairing a botched release.

.PARAMETER WhatIf
    Show what would happen without creating or pushing anything.

.EXAMPLE
    .\release.ps1 1.2.3

.EXAMPLE
    .\release.ps1 1.2.3 -WhatIf
    Runs every check and reports the tag it would push, without touching the repository.
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [switch] $SkipChecks
)

$ErrorActionPreference = 'Stop'

# git signals failure through the exit code, not through the error stream, so every call has to
# be checked explicitly.
function Invoke-Git {
    # Declaring a parameter would make PowerShell bind git's own switches, such as the '-a' of
    # 'git tag -a', to it; $args keeps them as plain arguments.
    $gitArgs = $args

    # git reports progress on stderr even when it succeeds, which the script-wide 'Stop'
    # preference would otherwise turn into a terminating error.
    $ErrorActionPreference = 'Continue'

    $output = & git @gitArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($gitArgs -join ' ') failed:`n$output"
    }
    return ($output | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] })
}

function Assert-Or-Skip {
    param([bool] $Condition, [string] $Message)

    if ($Condition) { return }
    if ($SkipChecks) {
        Write-Warning "$Message (ignored because -SkipChecks was given)"
        return
    }
    throw $Message
}

$tag = "v$Version"

Push-Location $PSScriptRoot
try {
    if (-not (Test-Path (Join-Path $PSScriptRoot 'BootManager.csproj'))) {
        throw "Project file not found. Run this script from the repository root."
    }

    Write-Host "Preparing release $tag" -ForegroundColor Cyan

    $branch = (Invoke-Git rev-parse --abbrev-ref HEAD).Trim()
    Assert-Or-Skip ($branch -eq 'main') "Current branch is '$branch', not 'main'."

    Assert-Or-Skip ([string]::IsNullOrWhiteSpace((Invoke-Git status --porcelain) -join '')) `
        'The working tree has uncommitted changes.'

    # A tag that points at a commit nobody else has is useless: the workflow builds from the
    # remote, and the release notes are assembled from the merged pull requests.
    Invoke-Git fetch origin --tags --prune | Out-Null

    $local = (Invoke-Git rev-parse HEAD).Trim()
    $remote = (Invoke-Git rev-parse 'origin/main').Trim()
    Assert-Or-Skip ($local -eq $remote) `
        "Local main ($($local.Substring(0, 7))) differs from origin/main ($($remote.Substring(0, 7))). Push or pull first."

    if ((Invoke-Git ls-remote --tags origin "refs/tags/$tag") -join '') {
        throw "Tag '$tag' already exists on origin. Pick a higher version."
    }

    & git rev-parse -q --verify "refs/tags/$tag" 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        throw "Tag '$tag' already exists locally. Delete it with 'git tag -d $tag' if it was never pushed."
    }

    # The release workflow deletes the previous release, so a red build would leave the project
    # with no downloadable binaries at all.
    if (Get-Command gh -ErrorAction SilentlyContinue) {
        $conclusion = & {
            # Like git, gh reports failures such as an expired login on stderr.
            $ErrorActionPreference = 'Continue'
            (& gh run list --branch main --workflow ci.yml --limit 1 --json conclusion --jq '.[0].conclusion' 2>$null) -join ''
        }
        if ($LASTEXITCODE -eq 0 -and $conclusion) {
            Assert-Or-Skip ($conclusion -eq 'success') "The last CI run on main is '$conclusion', not 'success'."
        }
        else {
            Write-Warning 'Could not read the CI status from GitHub; check it manually.'
        }
    }
    else {
        Write-Warning 'The GitHub CLI is not installed, so the CI status was not checked.'
    }

    $subject = (Invoke-Git log -1 --pretty=%s).Trim()
    Write-Host "  Commit  $($local.Substring(0, 7))  $subject"
    Write-Host "  Tag     $tag"
    Write-Host ''
    Write-Warning 'Publishing this release deletes the previous one and its tag from GitHub.'

    if ($PSCmdlet.ShouldProcess("origin/$tag", 'Create and push release tag')) {
        Invoke-Git tag -a $tag -m "BootManager $tag" | Out-Null
        try {
            Invoke-Git push origin "refs/tags/$tag" | Out-Null
        }
        catch {
            # Leaving a tag behind that the remote never received would block the next attempt.
            & git tag -d $tag | Out-Null
            throw
        }

        $repository = (Invoke-Git config --get remote.origin.url).Trim() -replace '\.git$', ''
        Write-Host ''
        Write-Host "Pushed $tag. The Release workflow is now building:" -ForegroundColor Green
        Write-Host "  $repository/actions/workflows/release.yml"
    }
}
finally {
    Pop-Location
}
