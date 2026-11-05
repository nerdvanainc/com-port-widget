# Changelog

한국어: [CHANGELOG.md](CHANGELOG.md) · English (this page)

Changes in each COM Port Widget release.

## 0.1.0 (2026-11-04)

First public version.

- A widget that lists connected COM ports in order of recognition time. The most recently recognised port is highlighted (5 minutes by default); an unplugged device moves to a collapsed "Not connected" group.
- Plug and unplug are detected from Windows device change notifications, and the whole list is re-read every 30 seconds. It never opens a port.
- Per-device notes. A device with a serial number keeps its note on another USB port, and the ROM and application modes of an ESP32-family board share one note.
- Vendor names from a USB VID table. A VID not in the table is shown as `VID 1234`.
- Devices recognised right after boot or resume are not highlighted and are labelled "at boot".
- Cleanup of COM ports: removes the records and COM number reservations of disconnected devices, for the items you select. Connected devices are excluded and checked again just before running. Needs Windows 10 2004 or later.
- Cleanup needs administrator rights, so the same exe is elevated through UAC and run in a cleanup-only mode. The exe is unsigned, so the UAC prompt shows "Unknown publisher".
- Tray icon, global hotkey (default `Ctrl+Alt+Shift+C`), always on top, start with Windows, Korean and English UI.
- If the data file is damaged, a `.corrupt` copy is kept and the widget header says so. Unhandled errors are written to `crash.log`.
- `--demo` mode: shows the screen with made-up devices.
- Single-exe distribution built on .NET Framework 4.8, which is part of Windows 10 1903 and later, so nothing needs installing. The exe is about 210 KiB and the zip about 98 KiB. Not code-signed.
- Known limits: a device plugged in during cleanup can have its record removed, and the plan file sits in a user-writable folder. A `.exe.config` file next to the exe or an environment variable can run code inside the program, so keep the exe in a folder of its own. See [NOTICE.en.md](NOTICE.en.md).
