<#
.SYNOPSIS
    Publishes release builds of BootManager for every supported operating system.

.DESCRIPTION
    Produces one subfolder per target under the output directory.

    Self-contained builds bundle the .NET runtime, so the user has to install nothing.
    Each is a single executable of roughly 50 MB.

        win-x64                Windows 64-bit
        linux-x64              Linux 64-bit
        osx-arm64              macOS on Apple Silicon

    Framework-dependent builds require a matching .NET runtime to be installed, but are
    only a few megabytes. They are single executables as well.

        win-x64-framework      Windows 64-bit
        linux-x64-framework    Linux 64-bit
        osx-arm64-framework    macOS on Apple Silicon

    The portable build is framework-dependent and carries no runtime identifier, so the
    same folder runs on any supported operating system. It cannot be packed into a single
    file and ships the native libraries for every platform, making it the largest output.

        portable               Any supported operating system

    Publishing for macOS and Linux works from Windows: the compiler only needs the
    target's runtime package, which NuGet downloads automatically. No Mac or Linux
    machine is required to produce the binaries, though they can only be tested there.

.PARAMETER OutputPath
    Directory that receives the per-target subfolders. Defaults to '.\publish'.

.PARAMETER Targets
    Which targets to build. Defaults to all of them.

.PARAMETER Clean
    Delete the output directory before publishing, so no files from an earlier run remain.

.EXAMPLE
    .\publish.ps1
    Publishes every target into .\publish.

.EXAMPLE
    .\publish.ps1 -Targets win-x64, win-x64-framework -OutputPath C:\builds -Clean
    Publishes both Windows variants into C:\builds, after emptying that folder.
#>
[CmdletBinding()]
param(
    [string] $OutputPath = (Join-Path $PSScriptRoot 'publish'),

    [ValidateSet(
        'win-x64', 'linux-x64', 'osx-arm64',
        'win-x64-framework', 'linux-x64-framework', 'osx-arm64-framework',
        'portable')]
    [string[]] $Targets = @(
        'win-x64', 'linux-x64', 'osx-arm64',
        'win-x64-framework', 'linux-x64-framework', 'osx-arm64-framework',
        'portable'),

    [switch] $Clean
)

# Stop at the first error instead of continuing with a half-finished build.
$ErrorActionPreference = 'Stop'

# What each target folder name actually builds.
#   Rid            runtime identifier passed to '-r', or $null for a platform-neutral build.
#   SelfContained  whether the .NET runtime is bundled into the output.
# Ordered so the summary table lists the targets in a predictable sequence.
$targetDefinitions = [ordered]@{
    'win-x64'             = @{ Rid = 'win-x64';   SelfContained = $true }
    'linux-x64'           = @{ Rid = 'linux-x64'; SelfContained = $true }
    'osx-arm64'           = @{ Rid = 'osx-arm64'; SelfContained = $true }
    'win-x64-framework'   = @{ Rid = 'win-x64';   SelfContained = $false }
    'linux-x64-framework' = @{ Rid = 'linux-x64'; SelfContained = $false }
    'osx-arm64-framework' = @{ Rid = 'osx-arm64'; SelfContained = $false }
    'portable'            = @{ Rid = $null;       SelfContained = $false }
}

$projectPath = Join-Path $PSScriptRoot 'BootManager.csproj'
if (-not (Test-Path $projectPath)) {
    throw "Project file not found at '$projectPath'. Run this script from the repository root."
}

if ($Clean -and (Test-Path $OutputPath)) {
    Write-Host "Cleaning $OutputPath" -ForegroundColor Yellow
    Remove-Item -Path $OutputPath -Recurse -Force
}

$results = [System.Collections.Generic.List[object]]::new()

foreach ($target in $Targets) {
    $definition = $targetDefinitions[$target]
    $targetOutput = Join-Path $OutputPath $target

    # Arguments common to every target.
    #   -c Release                     optimised build without debug checks.
    #   -o <dir>                       where the finished files are written.
    #   /p:DebugType=None              omits the .pdb debug symbols from the release output.
    $arguments = @(
        'publish', $projectPath,
        '-c', 'Release',
        '-o', $targetOutput,
        '/p:DebugType=None'
    )

    if ($null -eq $definition.Rid) {
        # No runtime identifier: one set of files that runs on any OS. Packing into a single file is
        # not possible here, because producing an executable requires knowing the target platform.
        $kind = 'portable, runtime required'
    }
    else {
        # -r <rid>                        the runtime identifier of the target platform.
        # /p:PublishSingleFile=true       packs the output into one executable.
        # /p:IncludeNativeLibrariesForSelfExtract=true
        #                                 also embeds the native libraries Avalonia needs, so the
        #                                 single file really is the only file required.
        $arguments += @(
            '-r', $definition.Rid,
            '/p:PublishSingleFile=true',
            '/p:IncludeNativeLibrariesForSelfExtract=true'
        )

        if ($definition.SelfContained) {
            # --self-contained true             bundles the .NET runtime so nothing must be installed.
            # /p:EnableCompressionInSingleFile=true
            #                                   compresses the bundle, roughly halving its size at the
            #                                   cost of a slightly slower first start. Only worthwhile
            #                                   for self-contained builds, where the embedded runtime
            #                                   accounts for nearly all of the size.
            $arguments += @(
                '--self-contained', 'true',
                '/p:EnableCompressionInSingleFile=true'
            )
            $kind = 'self-contained'
        }
        else {
            # The application is compiled against the shared runtime, which must already be present
            # on the user's machine. The output keeps the native libraries for this platform only.
            $arguments += @('--self-contained', 'false')
            $kind = 'runtime required'
        }
    }

    Write-Host "Publishing $target ($kind)..." -ForegroundColor Cyan
    & dotnet @arguments

    if ($LASTEXITCODE -ne 0) {
        throw "Publishing target '$target' failed with exit code $LASTEXITCODE."
    }

    # DebugType=None only suppresses this project's symbols. The native dependencies of the UI
    # framework (Skia, HarfBuzz) ship their own .pdb files, which are copied in regardless and would
    # otherwise account for most of the output size.
    Get-ChildItem -Path $targetOutput -Recurse -Filter '*.pdb' | Remove-Item -Force

    # Report the size of the produced folder, which is the number users care about most.
    $sizeMb = [math]::Round(
        ((Get-ChildItem -Path $targetOutput -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB), 1)

    $results.Add([pscustomobject]@{
        Target = $target
        Kind   = $kind
        Output = $targetOutput
        SizeMB = $sizeMb
    })
}

Write-Host ''
Write-Host 'Publish completed.' -ForegroundColor Green
$results | Format-Table -AutoSize
