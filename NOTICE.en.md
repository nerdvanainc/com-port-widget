# COM Port Widget Notice

한국어: [NOTICE.md](NOTICE.md) · English (this page)

Made and maintained by: © 2026 Nerdvana Inc., https://www.nerdvana.co.kr

This page collects notices about scope of use, responsibility and trademarks. The license itself is [LICENSE](LICENSE) (MIT). "COM Port Widget" is a temporary product name and may change.

## No warranty

The software is provided as is, without warranty of any kind, including merchantability or fitness for a particular purpose. The copyright holder is not liable for damage arising from its use. The exact wording is in [LICENSE](LICENSE).

## What the cleanup does, and who is responsible

The normal list view only reads device information. Only "Clean up COM ports" changes the system.

- For each selected item it removes the device record with `pnputil /remove-device` and releases that COM number in the ComDB reservation list. For a reserved number that has no device record, it releases the reservation only.
- A removed device record cannot be restored. When you plug the device in again, Windows creates a new record and assigns the lowest free number at that time. It may not be the number the device had before.
- Driver files are not removed.
- Before running, the original ComDB and the details of the records to be removed are written to `%APPDATA%\ComPortWidget\backup-<time>.json`. The file is for investigation; the app has no undo. It contains device instance IDs, including USB serial numbers, so be careful when sending it to someone.
- You choose the items in the list yourself. Whether a program or setting that refers to a released number is affected (a terminal program's port setting, a COM number written into a script) is your call. You are responsible for the result.

### Safeguards and remaining limits

The cleanup is built so that it does not touch connected devices. Just before running, each item's presence is checked again. The elevated side then re-validates that each target is a disconnected record in the Ports class and that a reserved-only number is not used by any device. The following limits remain.

1. If the same device is plugged in between the last presence check and the `pnputil` call (not measured; a short time, roughly a second or less), its record can be removed. Do not plug or unplug devices while the cleanup runs.
2. The plan file (`reset-plan.json`) and the result file (`reset-result.json`) sit in a folder the user can write to (`%APPDATA%\ComPortWidget`). The elevated side checks the file names, that both are in the same folder, and that no path component is a link (reparse point). It cannot prevent a file or the folder itself being swapped after those checks (for example, the folder replaced by a link); that is part of the remaining race. A process that is already running as your user could replace a file when you approve the UAC prompt. The elevated side validates the plan contents again, so it cannot remove a connected device or a record outside the Ports class, but it could be made to remove a disconnected record you did not select, or to release reserved numbers. The result and backup files are written to the same folder, so they can be swapped or pre-created in the same way (false content in the result file, or interference with the backup write).
3. Elevation goes through Windows PowerShell (`powershell.exe`, 5.1). That PowerShell honours user-level .NET profiler settings in environment variables (`COR_PROFILER` and similar). A malicious process already running as your user could plant such a setting and get code running inside the elevated PowerShell. The app can only clean the environment of the child process that PowerShell starts.
4. If the power goes out before the cleanup finishes, or the folder cannot be deleted, a folder `C:\Program Files\ComPortWidget-Cleanup-<GUID>` can be left behind. The app does not report this. You can delete it by hand.

If the PC is already infected, the limits above are a reason not to use this feature.

## Network and data

- The program does not use the network. There is no telemetry, update check or error reporting, and the app's own code makes no network calls. (The .NET runtime bundled in the exe includes libraries such as `System.Net`, which the app does not use.)
- Notes, observed connect and disconnect times, and settings are stored only in `%APPDATA%\ComPortWidget\data.json`. Nothing is synchronised to another PC. On an unhandled error the exception text is appended to `crash.log` in the same folder; it is never sent anywhere.
- If you turn on starting with Windows, one value is written under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- The program never opens a COM port. Device information is read from the registry and the device management APIs.

## Unsigned executable

The executable is not code-signed. SmartScreen may warn on the first run, and the UAC prompt of the cleanup shows "Windows PowerShell" instead of the app name. To check that your download is intact, compare it with `SHA256SUMS.txt` from the release.

## Names and trademarks

This software is not affiliated with, sponsored by or endorsed by the companies or products below. The names appear only to describe devices. The vendor name in the list is the chip maker that corresponds to the VID (vendor number) the USB device reports. It can differ from the company that built the device.

FTDI is a trademark of Future Technology Devices International Ltd. Silicon Labs is a trademark of Silicon Laboratories Inc. Espressif is a trademark of Espressif Systems. WCH is a trademark of Nanjing Qinheng Microelectronics Co., Ltd. Adafruit is a trademark of Adafruit Industries. STMicroelectronics is a trademark of STMicroelectronics. Prolific is a trademark of Prolific Technology Inc. Arduino is a trademark of Arduino S.r.l. SEGGER is a trademark of SEGGER Microcontroller GmbH. Arm is a trademark of Arm Limited. Microsoft, Windows and PowerShell are trademarks of Microsoft Corporation. Other names are trademarks of their owners.

## Third-party components

The components included in the executable are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
