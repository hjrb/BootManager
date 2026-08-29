# BootManager

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

---

## Installing

Download the build for your system and unpack it anywhere. There is no installer.

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

Select an entry, then choose an action:

| Button | What it does |
| --- | --- |
| **Set as Next Boot** | Boots the selected entry **once**. Afterwards your computer goes back to the default on its own. Use this to start another operating system a single time. |
| **Set as Default** | Makes the selected entry the **permanent** default, used for every boot until you change it again. |
| **Open UEFI Setup at Next Boot** | Your computer opens its UEFI/BIOS setup screen the next time it starts, so you don't have to catch the right key during startup. It does **not** restart now — reboot when you are ready. |
| **Refresh** | Re-reads the current state from the firmware. |

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
BootManager help             Show the available commands
```

Every command except `help` needs elevated rights. Unlike the window, the command line **does not**
offer to restart itself — it prints an error and exits with a non-zero code, so scripts can react to it.
Start your console as Administrator, or use `sudo`, before running these.

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

`appsettings.json` next to the program controls logging. You can override any setting without editing
the file, either with an environment variable prefixed `BOOTMANAGER_` or on the command line:

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
