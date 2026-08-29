<#
.SYNOPSIS
    Publishes release builds of BootManager for every supported operating system.

.DESCRIPTION
    Produces one subfolder per target under the output directory:

        win-x64      Windows 64-bit, self-contained single file
        linux-x64    Linux 64-bit, self-contained single file
        osx-arm64    macOS on Apple Silicon, self-contained single file
        portable     Framework-dependent build that runs on any supported OS,
                     but requires the matching .NET runtime to be installed

    The three platform builds are self-contained, so the .NET runtime is bundled and
    users do not have to install anything. That makes them considerably larger, which is
    why the small portable build is produced as well.

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
    .\publish.ps1 -Targets win-x64 -OutputPath C:\builds -Clean
    Publishes only the Windows build into C:\builds, after emptying that folder.
#>
[CmdletBinding()]
param(
    [string] $OutputPath = (Join-Path $PSScriptRoot 'publish'),

    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64', 'portable')]
    [string[]] $Targets = @('win-x64', 'linux-x64', 'osx-arm64', 'portable'),

    [switch] $Clean
)

# Stop at the first error instead of continuing with a half-finished build.
$ErrorActionPreference = 'Stop'

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

    if ($target -eq 'portable') {
        # No runtime identifier: one set of files that runs on any OS, but the user must have
        # the .NET runtime installed.
        Write-Host "Publishing portable (framework-dependent) build..." -ForegroundColor Cyan
    }
    else {
        # -r <rid>                        the runtime identifier of the target platform.
        # --self-contained true           bundles the .NET runtime so nothing must be installed.
        # /p:PublishSingleFile=true       packs the output into one executable.
        # /p:IncludeNativeLibrariesForSelfExtract=true
        #                                 also embeds the native libraries Avalonia needs, so the
        #                                 single file really is the only file required.
        # /p:EnableCompressionInSingleFile=true
        #                                 compresses the bundle, which roughly halves its size at
        #                                 the cost of a slightly slower first start.
        Write-Host "Publishing $target (self-contained single file)..." -ForegroundColor Cyan
        $arguments += @(
            '-r', $target,
            '--self-contained', 'true',
            '/p:PublishSingleFile=true',
            '/p:IncludeNativeLibrariesForSelfExtract=true',
            '/p:EnableCompressionInSingleFile=true'
        )
    }

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
        Output = $targetOutput
        SizeMB = $sizeMb
    })
}

Write-Host ''
Write-Host 'Publish completed.' -ForegroundColor Green
$results | Format-Table -AutoSize
