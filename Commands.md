# Overview of commands to use to manually configure UEFI boot
| Function                            | Windows 11                                              | Linux (UEFI)                        | macOS Intel                            | macOS Apple Silicon                                |
| ----------------------------------- | ------------------------------------------------------- | ----------------------------------- | -------------------------------------- | -------------------------------------------------- |
| **List boot entries**               | `bcdedit /enum firmware`                                | `efibootmgr -v`                     | `bless --info` / `nvram -p`            | **not through UEFI NVRAM like a PC**               |
| **Current boot order**              | `bcdedit /enum firmware` → `{fwbootmgr}` `displayorder` | `efibootmgr` → `BootOrder`          | `bless --info` / `nvram`               | Apple Startup Security / Boot Picker               |
| **Set default boot**                | `bcdedit /set {fwbootmgr} displayorder {ID} /addfirst`  | `efibootmgr -o XXXX,YYYY,...`       | `bless --setBoot ...`                  | Apple-specific                                     |
| **Set next boot only**              | `bcdedit /bootsequence {ID}`                            | `efibootmgr -n XXXX`                | `bless --nextonly ...`                 | Apple-specific                                     |
| **Open UEFI/boot configuration**    | `shutdown /r /fw /t 0`                                  | `systemctl reboot --firmware-setup` | `bless --nextonly --firmware`          | `systemctl` not applicable; Apple Startup Options  |
| **Delete an entry**                 | `bcdedit /delete {ID}`*                                 | `efibootmgr -b XXXX -B`             | no equivalent UEFI NVRAM concept       | —                                                  |
| **Reboot, graceful**                | `shutdown /r /t 0` (no `/f`)                            | `systemctl reboot --check-inhibitors=yes` | `osascript -e 'tell application "System Events" to restart'` | same as macOS Intel                   |
| **Reboot, forced / hard**           | `shutdown /r /t 0 /f`                                   | `systemctl reboot --force --no-wall` | `shutdown -r now`                     | same as macOS Intel                                |
| **Shut down**                       | `shutdown /s /t 0 /f`                                   | `systemctl poweroff`                | `shutdown -h now`                      | same as macOS Intel                                |

# Graceful vs. forced reboot
The two reboot rows above are not just different spellings of the same thing, and the difference is
where unsaved work is won or lost.

**Graceful** means the operating system asks every running program to stop first. A program may take
the opportunity to save, and it may also refuse - so a graceful reboot can legitimately end with the
machine still running. Each platform exposes this through a completely different channel:

- **Windows** sends `WM_QUERYENDSESSION` to every window. A program can block the shutdown and explain
  why (that is the "This app is preventing shutdown" screen). The trap: `/f` suppresses all of it, and
  `/f` is *implied automatically* whenever `/t` is greater than zero. `shutdown /r /t 30` is therefore
  a forced reboot, not a polite one - only `/t 0` without `/f` is graceful.
- **Linux** has no per-application prompt at all. `systemctl reboot` sends `SIGTERM`, waits
  `DefaultTimeoutStopSec` (90 s by default) and then sends `SIGKILL`. The closest thing to a veto is an
  *inhibitor lock* (`systemd-inhibit`), which a package manager or backup tool can hold - but root
  bypasses `block` inhibitors unless you pass `--check-inhibitors=yes`. Desktop environments do show a
  save-your-work prompt, but only when the reboot goes through GNOME's or KDE's session manager rather
  than through `systemctl`.
- **macOS** distinguishes the two sharply. The AppleEvent to `loginwindow`
  (`osascript -e 'tell application "System Events" to restart'`) asks each application to quit, so it
  can show its "unsaved changes" sheet and cancel the restart. `shutdown -r now` does none of that.

**Forced / hard** means programs are killed where they stand and anything not yet written to disk is
lost. On Linux, note that `--force` once still unmounts file systems; specifying it twice (`-ff`) skips
even that and risks filesystem corruption, which is why it is best avoided.

Use graceful by default. Use forced when the system is hung, or when you know everything is saved.

# Notes
Sure you can learn all that. Or just use the tool :-)