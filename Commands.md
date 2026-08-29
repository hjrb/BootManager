# Overview of commands to use to manually configure UEFI boot
| Function                            | Windows 11                                              | Linux (UEFI)                        | macOS Intel                            | macOS Apple Silicon                                |
| ----------------------------------- | ------------------------------------------------------- | ----------------------------------- | -------------------------------------- | -------------------------------------------------- |
| **List boot entries**               | `bcdedit /enum firmware`                                | `efibootmgr -v`                     | `bless --info` / `nvram -p`            | **not through UEFI NVRAM like a PC**               |
| **Current boot order**              | `bcdedit /enum firmware` → `{fwbootmgr}` `displayorder` | `efibootmgr` → `BootOrder`          | `bless --info` / `nvram`               | Apple Startup Security / Boot Picker               |
| **Set default boot**                | `bcdedit /set {fwbootmgr} displayorder {ID} /addfirst`  | `efibootmgr -o XXXX,YYYY,...`       | `bless --setBoot ...`                  | Apple-specific                                     |
| **Set next boot only**              | `bcdedit /bootsequence {ID}`                            | `efibootmgr -n XXXX`                | `bless --nextonly ...`                 | Apple-specific                                     |
| **Open UEFI/boot configuration**    | `shutdown /r /fw /t 0`                                  | `systemctl reboot --firmware-setup` | `bless --nextonly --firmware`          | `systemctl` not applicable; Apple Startup Options  |
| **Delete an entry**                 | `bcdedit /delete {ID}`*                                 | `efibootmgr -b XXXX -B`             | no equivalent UEFI NVRAM concept       | —                                                  |

# Notes
Sure you can learn all that. Or just use the tool :-)