<#
.SYNOPSIS
    Builds Debug and Release and creates Linux double-click launchers.

.DESCRIPTION
    Creates launch.sh, launch-elevated.sh, BootManager.desktop and
    BootManager-elevated.desktop in each build output directory.
    Launchers use the .NET installation that built the application.
    Elevated launches use pkexec and retain the desktop display connection.
    Run this script as your normal user, not as root.

.EXAMPLE
    pwsh -NoProfile -File ./build.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

if (-not $IsLinux) {
    throw 'This script creates Linux desktop launchers and must run on Linux.'
}

$dotnetPath = (Get-Command dotnet -CommandType Application -TotalCount 1 -ErrorAction Stop).Source
$dotnetPath = (Get-Item -LiteralPath $dotnetPath).ResolveLinkTarget($true)?.FullName ?? $dotnetPath
$runtimeRoot = Split-Path -Parent $dotnetPath
$pkexecPath = (Get-Command pkexec -CommandType Application -TotalCount 1 -ErrorAction Stop).Source
$envPath = '/usr/bin/env'
$chmodPath = (Get-Command chmod -CommandType Application -TotalCount 1 -ErrorAction Stop).Source
$gio = Get-Command gio -CommandType Application -TotalCount 1 -ErrorAction SilentlyContinue
$projectPath = Join-Path $PSScriptRoot 'BootManager.csproj'
$iconPath = Join-Path $PSScriptRoot 'Assets/bootmanager-256.png'

if ((& id -u) -eq '0') {
    throw 'Run build.ps1 as your normal user, not as root.'
}

& /usr/bin/test -x $envPath
if ($LASTEXITCODE -ne 0) {
    throw "Required system utility '$envPath' is missing or not executable."
}

function ConvertTo-ShellArgument {
    param([string] $Value)
    return "'" + $Value.Replace("'", "'\''") + "'"
}

function ConvertTo-DesktopValue {
    param([string] $Value)
    return $Value.Replace('\', '\\').Replace("`n", '\n').Replace("`r", '\r').Replace("`t", '\t')
}

function ConvertTo-DesktopArgument {
    param([string] $Value)
    $quoted = '"' + $Value.Replace('\', '\\').Replace('"', '\"').Replace('`', '\`').Replace('$', '\$').Replace('%', '%%') + '"'
    return ConvertTo-DesktopValue $quoted
}

foreach ($configuration in @('Debug', 'Release')) {
    Write-Host "Building $configuration..." -ForegroundColor Cyan
    & $dotnetPath build $projectPath -c $configuration -warnaserror
    if ($LASTEXITCODE -ne 0) {
        throw "Building '$configuration' failed with exit code $LASTEXITCODE."
    }

    $propertiesJson = & $dotnetPath msbuild $projectPath -nologo `
        "-property:Configuration=$configuration" -getProperty:TargetDir,TargetName
    if ($LASTEXITCODE -ne 0) {
        throw "Reading '$configuration' output paths failed with exit code $LASTEXITCODE."
    }
    $properties = ($propertiesJson -join "`n" | ConvertFrom-Json).Properties
    $outputPath = $properties.TargetDir
    $executablePath = Join-Path $outputPath $properties.TargetName
    if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
        throw "Built executable not found at '$executablePath'."
    }

    # Separate elevated logs prevent root-owned files from blocking normal launches.
    New-Item -ItemType Directory -Path (Join-Path $outputPath 'logs') -Force | Out-Null
    $normalPath = Join-Path $outputPath 'launch.sh'
    $elevatedPath = Join-Path $outputPath 'launch-elevated.sh'
    $shellOutput = ConvertTo-ShellArgument $outputPath
    $shellExecutable = ConvertTo-ShellArgument $executablePath
    $shellRuntime = ConvertTo-ShellArgument "DOTNET_ROOT=$runtimeRoot"
    $shellEnv = ConvertTo-ShellArgument $envPath
    $shellPkexec = ConvertTo-ShellArgument $pkexecPath

    $normalScript = @'
#!/bin/sh
set -eu
cd {0}
unset DOTNET_ROOT_X64 DOTNET_ROOT_ARM64 DOTNET_ROOT_X86 DOTNET_ROOT_ARM
exec {1} {2} {3} "$@"
'@ -f $shellOutput, $shellEnv, $shellRuntime, $shellExecutable

    $elevatedScript = @'
#!/bin/sh
set -eu
cd {0}
if [ -z "${{XAUTHORITY:-}}" ] && [ -f "$HOME/.Xauthority" ]; then
    XAUTHORITY="$HOME/.Xauthority"
fi
exec {1} {2} {3} \
    "DISPLAY=${{DISPLAY:-}}" \
    "XAUTHORITY=${{XAUTHORITY:-}}" \
    "WAYLAND_DISPLAY=${{WAYLAND_DISPLAY:-}}" \
    "XDG_RUNTIME_DIR=${{XDG_RUNTIME_DIR:-}}" \
    "XDG_SESSION_ID=${{XDG_SESSION_ID:-}}" \
    'BOOTMANAGER_Serilog__WriteTo__0__Args__path=logs/bootmanager-elevated-.log' \
    {4} "$@"
'@ -f $shellOutput, $shellPkexec, $shellEnv, $shellRuntime, $shellExecutable

    Set-Content -LiteralPath $normalPath -Value $normalScript -Encoding utf8NoBOM
    Set-Content -LiteralPath $elevatedPath -Value $elevatedScript -Encoding utf8NoBOM

    $launchers = @(
        @{ Name = "BootManager ($configuration)"; Script = $normalPath; File = 'BootManager.desktop' }
        @{ Name = "BootManager ($configuration, elevated)"; Script = $elevatedPath; File = 'BootManager-elevated.desktop' }
    )
    foreach ($launcher in $launchers) {
        $desktopPath = Join-Path $outputPath $launcher.File
        $desktop = @"
[Desktop Entry]
Type=Application
Name=$($launcher.Name)
Comment=Manage UEFI boot options
Exec=$(ConvertTo-DesktopArgument $launcher.Script)
Path=$(ConvertTo-DesktopValue $outputPath)
Icon=$(ConvertTo-DesktopValue $iconPath)
Terminal=false
Categories=System;
"@
        Set-Content -LiteralPath $desktopPath -Value $desktop -Encoding utf8NoBOM
        & $chmodPath +x -- $launcher.Script $desktopPath $executablePath
        if ($LASTEXITCODE -ne 0) {
            throw "Making '$desktopPath' executable failed with exit code $LASTEXITCODE."
        }

        if ($gio) {
            & $gio.Source set $desktopPath metadata::trusted true
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "Could not mark '$desktopPath' trusted. Use your file manager's Allow Launching action."
            }
        }
        else {
            Write-Warning "gio is unavailable. Use your file manager's Allow Launching action for '$desktopPath'."
        }
        Write-Host "Created $desktopPath" -ForegroundColor Green
    }
}
