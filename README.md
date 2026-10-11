# BootManager

[![CI](https://github.com/hjrb/BootManager/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/hjrb/BootManager/actions/workflows/ci.yml)
[![CodeQL](https://github.com/hjrb/BootManager/actions/workflows/codeql.yml/badge.svg?branch=main)](https://github.com/hjrb/BootManager/actions/workflows/codeql.yml)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)

A small cross-platform tool to inspect and change your computer's UEFI boot options.

Use it to pick which operating system starts next, change the one that starts by default, jump straight
into the UEFI setup screen on the next restart, and collect the information you need when a boot goes
wrong.

Runs on **Windows**, **Linux** and **macOS**, with both a graphical window and a command line.

---

## Requirements

- A machine that boots via **UEFI**. On a computer running in legacy BIOS/CSM mode there are no UEFI
  boot entries to manage; the *System Information* tab tells you which mode you are in.
- **Administrator** (Windows) or **root/sudo** (Linux, macOS) rights. Boot settings live in protected
  firmware memory, so nothing can be read or changed without them.
- On Linux, the `efibootmgr` package must be installed.

On Linux, **Restart with sudo** uses `pkexec` and a graphical polkit password prompt. It requires
`pkexec` and a running polkit authentication agent, and retains the desktop display connection when
launching the elevated window.

---

## Wake Devices

The **Wake Devices** tab follows **Boot Options**. On Windows, it lists wake-programmable devices
under **Available Devices** and currently enabled devices under **Armed Devices**. Select a device
and use the right arrow to arm it, or the left arrow to disarm it. Refresh re-reads both lists.
Changing permissions requires Administrator rights; querying the lists does not automatically elevate.

Both lists are grouped by device type: **Mice**, **Keyboards**, **Network**, **USB / HID**, and **Other**
for unclassified devices. Double-click an available device to arm it, or an armed device to disarm it.
Group headings only expand or collapse; double-click changes still require elevated rights.
Windows grouping uses device classes, not translated names, and preserves the exact device names.
Devices listed by Windows in `powercfg /devicequery wake_from_any` use light-green text in both lists,
not bold text. Their tooltips explain the capability; actual waking still depends on firmware,
device power and driver settings.
Linux currently identifies network and USB devices from sysfs paths; remaining devices appear in Other.

Keyboard, mouse, network and USB/HID devices appear when their drivers support configurable waking.
Wake behavior also depends on firmware, driver settings and the sleep or hibernation state. Disarming
a keyboard or mouse may mean you need the power button to wake the computer.
Expand **UEFI Wake Support** beneath the device lists for a firmware checklist: USB wake, network/PCIe
wake, and ErP/Deep Sleep standby-power restrictions. Setting names vary by motherboard; consult its
manual and test Sleep and Hibernate separately.

On Linux, the same lists expose physical devices with readable `power/wakeup` attributes under
`/sys/devices`. Labels include their physical paths, and USB product names when available. Arming
and disarming require root and write `enabled` or `disabled` to the selected device's existing
attribute. These are live settings: reconnecting, rebooting, or suspend/resume may reset them.
The app does not install persistent udev rules or systemd services, toggle ACPI wake sources, or
configure Wake-on-LAN through ethtool. USB endpoints and controllers may both need enabling.
See [ArchWiki wakeup triggers](https://wiki.archlinux.org/title/Power_management/Wakeup_triggers).

On macOS, the tab opens **System Settings**. Set **Wake for network access** under **Battery > Options**
(laptops) or **Energy** (desktops), where available. macOS does not provide these per-device arm/disarm
lists. See [Apple's sleep and wake settings](https://support.apple.com/guide/mac-help/set-sleep-and-wake-settings-mchle41a6ccd/mac).

## Installing

Download the build for your system from the
[latest release](https://github.com/hjrb/BootManager/releases/latest) and unpack it anywhere. There is
no installer. Windows builds are `.zip`, Linux and macOS builds are `.tar.gz`.

Every build contains a `README.html` next to the program — this document, ready to open in any browser
without an internet connection.

Only the newest release is kept, so that page always shows the current version. Each release also
carries a `SHA256SUMS.txt` — see [SECURITY.md](SECURITY.md) for how to check a download before running
it with administrator rights.

**Nothing to install** — these include everything they need (~48 MB):

| Folder | For |
| --- | --- |
| `win-x64` | Windows 64-bit |
| `linux-x64` | Linux 64-bit |
| `osx-arm64` | macOS on Apple Silicon |

**Smaller, but needs the [.NET 10 runtime](https://dotnet.microsoft.com/download) installed** (~25-31 MB):

| Folder | For |
| --- | --- |
| `win-x64-framework` | Windows 64-bit |
| `linux-x64-framework` | Linux 64-bit |
| `osx-arm64-framework` | macOS on Apple Silicon |
| `portable` | Any supported system — one folder that runs everywhere, but the largest download |

If you are unsure, take the first table: those need no .NET installation.

The released `portable` build is produced on Linux, so its launcher is a Linux executable. On Windows
and macOS, start it with `dotnet BootManager.dll` instead — or take one of the platform builds above.

On Linux and macOS, mark the file as executable once after unpacking:

```bash
chmod +x ./BootManager
```

---

## Using the window

Start the program without any arguments.

If it was not started with the required rights, a yellow banner appears at the top with a button that
restarts it properly — Windows shows the usual UAC prompt, Linux and macOS ask for your password.

### Boot Options tab

The list shows every entry your firmware can boot. Two labels tell you what each one currently does:

- **Current default** — what your computer boots every time.
- **Next boot** — what it will boot the next time you start it.

Select an entry, then choose an action from the toolbar above the list. Its buttons are grouped into
**Reboot**, **Sleep**, **Shutdown** and **UEFI**:

| Button | What it does |
| --- | --- |
| Reboot &rsaquo; **Graceful**, **Forced**, **Delayed** | The restart actions your operating system supports. |
| Sleep &rsaquo; **Sleep**, **Hibernate** | Suspend to memory or to disk. Windows keeps unavailable Hibernate clickable with light-gray text. Hibernate is hidden on macOS, which has no separate action for it. |
| Shutdown &rsaquo; **Hybrid**, **Full** | Power off. On Windows a plain shutdown keeps the hybrid boot cache; **Full** skips it for a true cold start and exists on Windows only. |
| UEFI &rsaquo; **Refresh** | Re-reads the current state from the firmware. |
| UEFI &rsaquo; **Next Boot** | Boots the selected entry **once**. Afterwards your computer goes back to the default on its own. Use this to start another operating system a single time. |
| UEFI &rsaquo; **Default** | Makes the selected entry the **permanent** default, used for every boot until you change it again. |
| UEFI &rsaquo; **UEFI Setup** | Your computer opens its UEFI/BIOS setup screen the next time it starts, so you don't have to catch the right key during startup. On Windows this only arms the request and does **not** restart now. On Linux the only supported mechanism restarts the machine, so a countdown appears first and gives you time to save your work. |
| Information &rsaquo; **?**, **About** | Why an entry may be missing from the list, and the version, license and third-party notices. |
| Logoff &rsaquo; **Logoff** | Signs out the current desktop session without rebooting. Save your work first. Linux terminates session applications without prompting; the section is hidden if the session ID is unavailable. |

Every button's tooltip shows the exact command or API call it runs, so you can check or reproduce it
yourself.

On Windows, **Sleep** calls the native suspend API directly with hibernation disabled and wake events
enabled. It does not launch a terminal or run a reboot command.

Windows hibernation availability is checked without administrator rights. If unavailable, clicking
**Hibernate** offers administrators a confirmation to enable it; other users receive an error.
Enabling does not immediately hibernate. The chevron beside **Hibernate** offers **Disable Hibernate**,
which also requires administrator rights and confirmation. Disabling removes the hibernation file
and disables Fast Startup and hybrid sleep. Neither operation automatically requests elevation.

The toolbar distinguishes two ways to restart:

- **Now (graceful)** goes through the mechanism that lets applications ask you about unsaved
  work — which means an application can also refuse, and the machine stays up.
- **Now (forced)** restarts straight away and terminates applications without warning.
- **Delayed** shows a countdown (20 seconds by default), so you can cancel or reboot immediately.

The countdown window also has a **Close apps** button. It asks every one of your running applications
to close itself — the same thing that happens when you click a window's close button — so each one can
prompt you about unsaved work. Nothing is ever killed: an application may refuse, and it is then listed
as still open. Pressing the button stops the countdown, because those prompts are waiting for you; you
then decide when to continue with **Now**, or drop the whole thing with **Cancel**.

### Graceful vs. forced reboot — and why it matters

This sounds like a technicality, but it decides whether you lose work.

**A graceful reboot asks first.** The operating system tells every running program "I am about to shut
down". Each program can then save what it is doing, and it can also say *no* — which is why a graceful
reboot may end with your machine still running. That is not a failure, it is the point. You will
typically see it when a document has unsaved changes, when a download or a backup is in progress, or
when a package manager is halfway through installing updates.

**A forced (or "hard") reboot does not ask.** Programs are killed where they stand. Anything not yet
written to disk is gone: unsaved documents, database transactions in flight, a half-finished update.
It is faster and it always works — including when a program is hung and would otherwise block the
graceful path forever.

| | Graceful | Forced / hard |
| --- | --- | --- |
| Programs are asked first | Yes | No |
| A program can cancel it | Yes | No |
| Unsaved work | Programs get a chance to save it | Lost |
| Always restarts | No | Yes |
| Good for | Everyday use | A hung system, or when you know everything is saved |

**Rule of thumb:** use graceful. Reach for forced only when graceful does not get you there, and only
after you have saved your work.

One warning about the other platforms: on Linux and macOS the ordinary restart commands do **not**
prompt you the way Windows does. `systemctl reboot` sends programs a termination signal and kills
anything that has not exited; `shutdown -r now` on macOS is just as abrupt. This is exactly why the
delayed reboot with its countdown exists — on those systems it is the only warning you get.

For the curious: hover any button to see the exact command it runs. Graceful and forced map to
different commands on each operating system:

| | Windows | Linux | macOS |
| --- | --- | --- | --- |
| Graceful | `shutdown.exe /r /t 0` | `systemctl reboot --check-inhibitors=yes` | `osascript -e 'tell application "System Events" to restart'` |
| Forced | `shutdown.exe /r /t 0 /f` | `systemctl reboot --force --no-wall` | `shutdown -r now` |

On Windows the only difference is the `/f` flag. Careful: `/f` is also implied automatically whenever
the timeout is greater than zero, so `shutdown /r /t 30` is *not* the gentle option many people assume
it is. [`Commands.md`](Commands.md) explains what each platform does under the hood.

### Why don't I see my USB stick or DVD?

Sometimes a USB stick or an optical drive shows up in the list, sometimes it doesn't — and that is
normal. Entries for removable media are usually **not permanent**. Your firmware creates them while the
computer powers on and throws them away again afterwards, so what you see here is a snapshot of what was
attached the last time the machine started.

The usual reasons an entry is missing:

- **The medium was not attached at power-on.** This is by far the most common one. Plug a stick in after
  your system has already booted and no entry exists — the list still reflects the last startup. Insert
  the medium and restart, then look again.
- **The medium is not bootable.** It needs a bootloader your firmware can find, normally
  `\EFI\BOOT\BOOTX64.EFI` on a FAT32 partition. A stick with only data on it, or an ISO copied as a
  plain file, will never appear.
- **Fast Boot is enabled in your firmware setup.** To save a second or two during startup, many
  firmwares skip initialising USB controllers and optical drives entirely — so those devices are never
  looked at and never get an entry. Turning Fast Boot off in the firmware setup usually brings them
  back. Note this is your *firmware's* Fast Boot, not Windows' *Fast Startup* on the System Information
  tab — different settings with confusingly similar names.
- **Secure Boot rejected it.** A medium whose bootloader is not signed with a key your firmware trusts
  may be dropped silently.
- **Your machine is in legacy/CSM mode.** Legacy boot targets are not UEFI boot entries at all. They
  exist only in your firmware's own boot menu, so no tool that reads UEFI variables can show them.
- **Your firmware simply never stores them.** Some vendors build the list fresh each time you press the
  boot-menu key and never write it down. Their one-time boot menu will always show more devices than
  this list, and there is nothing to be done about that.

So: if you want to boot from a stick or a disc, insert it **and then restart**. If it still doesn't
appear, turn off Fast Boot in your firmware setup. And if you only need it once, your firmware's own
boot menu — usually F12, F8 or Esc during startup — will find devices that were never written to NVRAM.

### System Information tab

Everything worth knowing when a boot misbehaves: firmware vendor and version, whether you are in UEFI
or legacy BIOS mode, Secure Boot state, your hardware model, when the system last started, how long it
has been running, and how long the last boot took.

**Copy All** puts the whole list on the clipboard as text, ready to paste into a bug report or a forum
post.

> **Dual-booting Windows and Linux?** Check *Fast Startup* on this tab. When it is enabled, shutting
> Windows down does not really shut it down — it hibernates instead, which leaves your disks in a state
> Linux must not write to. It is a very common cause of dual-boot trouble.

---

## Using the command line

Pass a command as the first argument and the program prints its result to the console instead of
opening a window. Useful in scripts.

```
BootManager list             List the available boot entries with their ids
BootManager setnext <id>     Boot that entry on the next start only
BootManager setdef <id>      Make that entry the permanent default
BootManager bootUEFI         Open UEFI setup at the next boot
BootManager info             Print the system information
BootManager disableFastStartup
                             Windows only: turn Fast Startup off
BootManager reboot           Reboot, letting applications close first (they may cancel it)
BootManager reboot-graceful  Alias for reboot
BootManager hardreboot       Reboot immediately, terminating applications without warning
BootManager shutdown         Shut down the machine immediately
BootManager help             Show the available commands
```

`disableFastStartup` clears the `HiberbootEnabled` power setting, so shutting Windows down really
powers the machine off instead of hibernating the kernel. Hibernation itself stays available. The
change applies from the next shutdown on.

`reboot` is the graceful one: it may finish with your machine still running, because a program was
allowed to cancel it. Scripts that must restart no matter what should use `hardreboot` — but be aware
that it kills everything without asking, so anything unsaved is lost. See
[Graceful vs. forced reboot](#graceful-vs-forced-reboot--and-why-it-matters) for the details.
`BootManager help` prints the exact command each of these runs on the current operating system.

Every command except `help` needs elevated rights. Unlike the window, the command line **does not**
offer to restart itself — it prints an error and exits with a non-zero code, so scripts can react to it.
Start your console as Administrator, or use `sudo`, before running these.

Starting the program without a command opens the window, and no console window ever appears with it —
not from Explorer, a shortcut or the task scheduler.

The flip side on Windows: because the executable is a windowed program, your shell does not wait for
it, so an interactive prompt comes back before the output does. The output itself is written into
your console, and redirecting it into a file or a pipe works as usual:

```powershell
BootManager list | Out-Host          # PowerShell: waits and prints in order
cmd /c "BootManager list > out.txt"  # redirection into a file
```

### Example

```
> BootManager list
ID                                      FLAGS  DESCRIPTION
{7e6b4144-baa1-11ef-bebb-806e6f6e6963}  *>     Ubuntu
{bootmgr}                                      Windows Boot Manager

* = default (every boot)   > = next boot (one time)

> BootManager setnext {bootmgr}
Next boot set to 'Windows Boot Manager' ({bootmgr}). This applies once; the default is unchanged.
```

---

## Default boot vs. next boot

These two are easy to mix up, and choosing the wrong one is the most common mistake:

| | Default boot | Next boot |
| --- | --- | --- |
| Applies to | Every boot | The next boot only |
| Lasts | Until you change it again | Is used up once, then reverts by itself |
| Use it when | You want to switch which system you normally use | You want to start another system just this once |

On **macOS** only the persistent choice exists: Apple's firmware has no one-time override, so both
actions do the same thing there.

---

## Logs

Every action is written to a log file in the `logs` folder next to the program, with one file per day
and the last 14 days kept. If something fails, the log contains the exact command that was run and the
complete response from the system — attach it when reporting a problem.

## Settings

`appsettings.json` next to the program controls logging and a few behaviours of the window:

| Setting | Meaning |
| --- | --- |
| `BootManager:PowerCountdownSeconds` | How long the countdown window waits before it restarts. |
| `BootManager:CloseApplicationsGracePeriodSeconds` | How long **Close apps** waits for an application to disappear before listing it as still open. |
| `BootManager:ProtectedProcessNamesWindows` | Windows processes **Close apps** must never touch, because they are the shell rather than an application. |
| `BootManager:ProtectedProcessNamesUnix` | The same for Linux and macOS: the session's init system, display server, desktop shell, sound and bus daemons. |

The two process lists cannot be complete — every desktop environment names its parts differently — so
add any name you find missing. Use the name as the system reports it, without a path or `.exe`; on
Linux only the first 15 characters are compared, which is all the kernel keeps. If a list is empty or
missing, **Close apps** refuses to run rather than risk ending your session.

You can override any setting without editing the file, either with an environment variable prefixed
`BOOTMANAGER_` or on the command line:

```
BootManager --Serilog:MinimumLevel=Debug
```

---

## Known limitations

- **macOS**: cannot open the firmware setup screen — Macs have none. Hold **Option** (Intel) or the
  **power button** (Apple Silicon) during startup to reach the startup picker instead. A one-time boot
  override is also not possible.
- **Linux**: the boot duration breakdown requires `systemd`, and the Secure Boot state requires
  `mokutil`. Both are reported as unavailable if the tool is missing.
- **Legacy BIOS**: machines not booted through UEFI have no boot entries to manage.

## Building from source

Requires the .NET 10 SDK.

```powershell
dotnet build                 # build
dotnet run                   # run the window
.\publish.ps1                # produce release builds for all platforms in .\publish
```

`publish.ps1` can also build a single target, for example
`.\publish.ps1 -Targets linux-x64 -Clean`.

---

## Contributing

Bug reports, ideas, and pull requests are welcome. `main` is protected: every change goes through a
pull request that has to build cleanly and pass static analysis.

Start with [CONTRIBUTING.md](CONTRIBUTING.md). Everyone taking part is expected to follow the
[Code of Conduct](CODE_OF_CONDUCT.md). Security weaknesses go to [SECURITY.md](SECURITY.md), not to
the issue tracker.

---

## License

Licensed under the [Apache License, Version 2.0](LICENSE).

BootManager changes firmware settings and is provided without warranty of any kind. See sections 7 and
8 of the license.
