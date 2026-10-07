![image](https://github.com/user-attachments/assets/fe2e27e5-dfc8-45aa-a181-ee9647208002)

# Seven Updater

Builds an up-to-date Windows 7 installation ISO that boots on modern (UEFI) hardware:
the Windows 7 `install.wim` is updated, optionally patched and loaded with drivers, and then
placed into the Windows 10 installer, whose setup and boot files work on modern UEFI systems.

## What a build does

1. Optionally downloads the latest **UpdatePack7R2** (`updates\UpdatePack7R2+.exe`).
2. Extracts the Windows 7 ISO and lets you pick the edition (Ultimate, Professional, ...).
3. Integrates the newest `updates\UpdatePack7R2-*.exe` (picked by version number).
4. Mounts the image, optionally replaces `acpi.sys` with the modded one (fixes the 0xA5 boot error)
   and injects the selected driver pack, then saves and unmounts it.
5. Extracts the Windows 10 ISO, swaps in the Windows 7 `install.wim` and creates `<ISO label>.iso`
   (BIOS + UEFI bootable) in the output directory.
6. Removes the temporary files.

Progress is shown below the log and in the taskbar button. **Cancel** stops the running tool and
unmounts the image without saving. Everything is logged to `output.log` next to the program.

## Folder layout next to `SevenUpdater.exe`

```
bin\7za.exe                    7-Zip command line (driver / acpi archives)
bin\oscdimg.exe                ISO creation (Windows ADK)
updates\UpdatePack7R2+.exe     UpdatePack downloader (optional)
updates\UpdatePack7R2-*.exe    Update pack(s); the newest version is used
drivers\*.7z|*.zip|*.rar       Driver packs shown in the "Drivers" list
acpi\WIN7_A5_FIX_ACPI.7z       Modded acpi.sys (optional)
```

## Notes

- Runs as administrator (DISM needs it). Plan for ~30 GB free space in the working directory.
- **Clean** only deletes the app's own temporary folders (`win7`, `win10`, `offline`, `drivers`,
  `acpi`, `temp`) inside the working directory; other files there, such as created ISOs, are kept.
- The ISO label is also the file name. Letters, digits, `_` and `-` only, up to 32 characters.
