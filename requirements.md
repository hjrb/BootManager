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
   - The information must be able to refresh and copyable, so it can be pasted into a bug report.
10. Expose a command line interface with the following commands, each printing its result to the console:
    | Command | Purpose |
    | --- | --- |
    | `list` | List the available boot entries. |
    | `setnext <id>` | Set the entry used for the next boot only. |
    | `setdef <id>` | Set the persistent default boot entry. |
    | `bootUEFI` | Configure the system to open UEFI setup at the next boot. |
    | `info` | Print the boot related system information. |
    | `disableFastStartup` | Windows only: turn Fast Startup off. |
    | `reboot` / `reboot-graceful` | Reboot gracefully; applications may prompt or cancel it. |
    | `hardreboot` | Reboot immediately, terminating applications without warning. |
    | `shutdown` | Shut the machine down immediately. |

    When a CLI command is invoked without Administrator/root privileges the application must simply
    fail with an error message and a non-zero exit code - it must **not** try to re-launch itself
    elevated, since that would detach from the console and lose the output.

    The `help` output must support showing the concrete command each power action runs on the current
    operating system, so the text stays accurate per platform instead of describing only one of them.
11. Add a power control in the main window that opens a popup menu with the supported shutdown/reboot actions for the current OS:
    - Reboot now, **graceful**.
    - Reboot now, **forced**.
    - Delayed reboot (20 seconds, with an on-screen countdown and a cancel button).
    - Shutdown.
    - Full shutdown on Windows only; hidden on Linux and macOS where the OS does not expose a separate action.
    - Unsupported actions for a given OS must be hidden instead of shown as disabled items.
    - On Linux and macOS, the implementation should still expose the closest equivalent operations that are actually supported by the platform, such as `systemctl reboot`/`poweroff` and `shutdown -r now`/`shutdown -h now`.
12. The application must support two distinct kinds of restart, because the difference decides whether
    unsaved work survives:
    - **Graceful** must route through the operating system mechanism that lets applications ask the
      user about unsaved work. It must therefore be allowed to *not* reboot when an application
      refuses, and that outcome must not be reported as a failure.
    - **Forced/hard** must reboot straight away and terminate applications without warning.
    - The two must be separate menu entries and separate CLI commands; a single "reboot" action that
      silently picks one is not sufficient.
    - The graceful path must use `shutdown.exe /r /t 0` **without** `/f` on Windows (a timeout greater
      than zero would imply `/f`), `systemctl reboot --check-inhibitors=yes` on Linux (so inhibitor
      locks are honoured instead of being bypassed by root), and the AppleEvent
      `osascript -e 'tell application "System Events" to restart'` on macOS.
13. Because neither `systemctl reboot` on Linux nor `shutdown -r now` on macOS gives applications any
    chance to prompt, every action that restarts the machine without the user explicitly asking for an
    immediate restart must be put behind the countdown window:
    - The countdown window must support being reused for any deferred action, not only the plain reboot.
    - It must offer **Cancel**, an immediate variant, and - where the action has a graceful counterpart -
      a button that lets running applications close first and then reboots gracefully.
    - It must display the command that will run when the countdown ends.
14. Requesting the firmware setup screen must support platforms where the request itself reboots:
    - The service layer must expose whether the request restarts the machine immediately.
    - Where it does (Linux, `systemctl reboot --firmware-setup`), the UI must show the countdown first
      so the user can save work; where it does not (Windows), it must arm the request without restarting.
15. Every interactive element must have a tooltip. For anything that runs an external tool or a native
    API, the tooltip must support showing the exact command or API call, resolved for the current
    operating system, and it must be derived from the same source the execution path uses so that the
    two cannot drift apart.
16. The System Information panels must support using the full width of their container.
17. The boot entry list must support explaining its own gaps. Entries for removable media (USB sticks,
    optical drives) are created by the firmware during power-on and discarded again, so the list
    legitimately differs between starts and cannot show a medium that was attached afterwards. A help
    button next to the list must open the user documentation section that explains this, and the user
    documentation must cover the common causes: medium absent at power-on, medium not bootable,
    firmware Fast Boot skipping device enumeration, Secure Boot rejection, legacy/CSM mode, and
    firmwares that never persist such entries at all.
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
    systemd offers no supported way to set the firmware setup flag without restarting, which is why
    that path goes through the countdown window.
  - macOS: `diskutil list` / `bless --getBoot` (enumerate), `bless --device /dev/<id> --setBoot`
    (persistent startup disk selection - no true one-time boot on macOS, so "next boot" and "default"
    map to the same operation). Requesting firmware setup is
    **not supported** on macOS (no script supported equivalent); the app surfaces this as an error message.
- Logging configured via Serilog reading from `appsettings.json` (`Serilog` section), rolling daily,
  size-limited, retaining 14 files.
- `Services/ElevationService` detects elevation (`WindowsPrincipal.IsInRole(Administrator)` on Windows,
  `geteuid() == 0` via libc on Linux/macOS) and relaunches elevated on request:
  Windows uses `ProcessStartInfo(Verb = "runas")` (UAC prompt), Linux uses `pkexec` (polkit prompt,
  no TTY needed), macOS uses `osascript ... with administrator privileges` (native password prompt).
  The main window shows a persistent banner with a restart button when not elevated.
