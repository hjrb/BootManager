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
7. If the application is not running with Administrator/sudo (root) privileges, tell the user and
   offer to restart the application elevated (Administrator on Windows, sudo/root on Linux/macOS).
8. Allow the user to set the **default** boot item (in addition to setting the next boot item).
9. Provide an option to show boot related system information - everything a user would want to know or
   would need in order to troubleshoot a boot problem. At minimum:
   - Firmware: vendor, version, release date, UEFI vs. legacy BIOS mode, UEFI specification version.
   - Security: Secure Boot state.
   - Hardware: manufacturer, model, mainboard.
   - Timing: last boot time, current uptime, and the duration of the last boot (broken down into
     firmware/loader/kernel/userspace where the platform provides it).
   - Platform quirks relevant to dual boot troubleshooting, e.g. Windows "Fast Startup" (hiberboot),
     which leaves the disk in a hibernated state and is a frequent cause of dual-boot problems.
   - The information must be refreshable and copyable, so it can be pasted into a bug report.
10. Expose a command line interface with the following commands, each printing its result to the console:
    | Command | Purpose |
    | --- | --- |
    | `list` | List the available boot entries. |
    | `setnext <id>` | Set the entry used for the next boot only. |
    | `setdef <id>` | Set the persistent default boot entry. |
    | `bootUEFI` | Configure the system to open UEFI setup at the next boot. |
    | `info` | Print the boot related system information. |

    When a CLI command is invoked without Administrator/root privileges the application must simply
    fail with an error message and a non-zero exit code - it must **not** try to re-launch itself
    elevated, since that would detach from the console and lose the output.

### Default boot item vs. next boot item
These are two distinct UEFI concepts and the UI must keep them clearly separated:

| | Default boot item | Next boot item |
| --- | --- | --- |
| Scope | **Persistent** - applies to every boot | **One-time** - applies to the next boot only |
| Lifetime | Stays in effect until changed again | Consumed by the firmware at the next boot, then the default applies again |
| UEFI variable | `BootOrder` (its first entry) | `BootNext` |
| Windows (`bcdedit`) | `{fwbootmgr}` `displayorder` (first entry) | `{fwbootmgr}` `bootsequence` |
| Linux (`efibootmgr`) | `efibootmgr -o <ids>` | `efibootmgr -n <id>` |

In short: setting the *next boot item* is a one-shot override that reverts automatically, while setting
the *default boot item* changes what the machine boots from every time. Setting a next boot item does
not change the default, and the UI must therefore be able to show both markers on the list
(an entry can be the default, the next boot, both, or neither).

macOS has no equivalent of a one-time boot override, so there setting the boot item is always a change
of the default (persistent startup disk).

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
- Error message text must be selectable and copyable to the clipboard.
- Code must be documented so that a developer who is not a C# expert can understand it:
  - Proper XML documentation comments on all public methods and types.
  - Document purpose, intention, and the background reasoning/assumptions behind the implementation.
  - Explain command line options of every external process that is invoked, and the meaning of the
    native APIs, flags and exit codes involved.
  - This standard is enforced for AI-assisted edits via
    `.github/instructions/csharp-comments.instructions.md` (applies to `**/*.cs`).

## Delivery
- A PowerShell script (`publish.ps1`) must produce release builds, each into its own subfolder:
  - Self-contained single-file builds (no .NET installation required) for Windows x64, Linux x64
    and macOS ARM.
  - Framework-dependent single-file builds (smaller, require the .NET runtime) for the same three
    platforms.
  - A portable framework-dependent build without a runtime identifier that runs on all supported
    operating systems.
- A brief `README.md` must explain to end users how to use the tool.

## Implementation Notes
- Avalonia MVVM app (`BootManager.csproj`), NuGet packages: `Microsoft.Extensions.Configuration*`,
  `Serilog` + `Serilog.Sinks.File` + `Serilog.Settings.Configuration`.
- `Services/IBootManagerService` abstracts enumeration, setting the next boot entry, setting the default
  boot entry, and requesting firmware setup, with platform implementations selected by
  `BootManagerServiceFactory`:
  - Windows: `bcdedit.exe /enum firmware` (enumerate/parse), `bcdedit.exe /set {fwbootmgr} bootsequence`
    (one-time next boot), `bcdedit.exe /set {fwbootmgr} displayorder <id> /addfirst` (default boot),
    and the UEFI `OsIndications` firmware variable set via
    `SetFirmwareEnvironmentVariableW` (request firmware setup on next boot). Requires admin rights.
    `shutdown.exe /r /fw` is intentionally avoided - it fails with ERROR_ENVVAR_NOT_FOUND (203) and hides
    the real cause; setting `OsIndications` directly also avoids forcing an immediate reboot and lets the
    app check `OsIndicationsSupported` first to report unsupported firmware clearly.
  - Linux: `efibootmgr -v` (enumerate/parse), `efibootmgr -n <id>` (one-time next boot via BootNext),
    `efibootmgr -o <ids>` (default boot, by moving the entry to the front of BootOrder),
    `systemctl reboot --firmware-setup` (reboot into firmware setup). Requires root.
  - macOS: `diskutil list` / `bless --getBoot` (enumerate), `bless --device /dev/<id> --setBoot`
    (persistent startup disk selection - no true one-time boot on macOS, so "next boot" and "default"
    map to the same operation). Requesting firmware setup is
    **not supported** on macOS (no scriptable equivalent); the app surfaces this as an error message.
- Logging configured via Serilog reading from `appsettings.json` (`Serilog` section), rolling daily,
  size-limited, retaining 14 files.
- `Services/ElevationService` detects elevation (`WindowsPrincipal.IsInRole(Administrator)` on Windows,
  `geteuid() == 0` via libc on Linux/macOS) and relaunches elevated on request:
  Windows uses `ProcessStartInfo(Verb = "runas")` (UAC prompt), Linux uses `pkexec` (polkit prompt,
  no TTY needed), macOS uses `osascript ... with administrator privileges` (native password prompt).
  The main window shows a persistent banner with a restart button when not elevated.
