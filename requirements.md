# Requirements

This file tracks the requirements provided for the BootManager project.

## Overview
A simple UI application (Avalonia UI, cross-platform: Windows, macOS, Linux) to manage UEFI boot options.

## Functional Requirements
1. Enumerate UEFI boot options.
2. Select which boot option should be used for the next boot.
3. Configure the system to open the UEFI configuration (firmware setup) screen at the next boot.
4. In the main window, enumerate the list of existing UEFI boot options and highlight the current one
   (the one that will be used for the next boot), and allow the user to change the boot item.
5. Add a button to configure the system to boot into the UEFI configuration.
6. Implement the above for Linux, Windows and macOS.

## Non-Functional / Technical Requirements
- Prefer NuGet packages where available; if no suitable package exists for a platform operation,
  spawn the relevant native process (e.g. `bcdedit`, `efibootmgr`, `bless`) and capture its output.
- When the user makes a boot configuration change, write a log file entry.
- Configuration must use the default `IConfiguration` stack (appsettings.json, environment variables,
  command-line parameters).
- Logging must use a rotating log file. Use NuGet packages for logging if required.
  - Trace-level detail (process start/args/output) logged at Verbose.
  - Successful user-initiated changes logged at Information.
- Any exception must be caught and reported to the user via a non-modal message (not a blocking dialog).

## Implementation Notes
- Avalonia MVVM app (`BootManager.csproj`), NuGet packages: `Microsoft.Extensions.Configuration*`,
  `Serilog` + `Serilog.Sinks.File` + `Serilog.Settings.Configuration`.
- `Services/IBootManagerService` abstracts enumeration, setting next boot, and requesting firmware setup,
  with platform implementations selected by `BootManagerServiceFactory`:
  - Windows: `bcdedit.exe /enum firmware` (enumerate/parse), `bcdedit.exe /set {fwbootmgr} bootsequence`
    (one-time next boot), `shutdown.exe /r /fw /t 0` (reboot into firmware setup). Requires admin rights.
  - Linux: `efibootmgr -v` (enumerate/parse), `efibootmgr -n <id>` (one-time next boot via BootNext),
    `systemctl reboot --firmware-setup` (reboot into firmware setup). Requires root.
  - macOS: `diskutil list` / `bless --getBoot` (enumerate), `bless --device /dev/<id> --setBoot`
    (persistent startup disk selection - no true one-time boot on macOS). Requesting firmware setup is
    **not supported** on macOS (no scriptable equivalent); the app surfaces this as an error message.
- Logging configured via Serilog reading from `appsettings.json` (`Serilog` section), rolling daily,
  size-limited, retaining 14 files.
