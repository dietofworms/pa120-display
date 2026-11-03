# pa120 — open-source display driver for the Thermalright Peerless Assassin 120 Digital

A ~15 KB, dependency-free replacement for Thermalright's software. It shows CPU temp, CPU usage,
GPU temp and GPU usage on the cooler's segment display, and does nothing else. It uses ~0% CPU.

| Stat      | Source                                                                  | Needs                          |
|-----------|-------------------------------------------------------------------------|--------------------------------|
| CPU usage | `GetSystemTimes` (Windows API)                                          | nothing                        |
| GPU temp / usage | `nvml.dll` (ships with the NVIDIA driver)                        | NVIDIA GPU                     |
| CPU temp  | AMD Zen `Tctl` register (SMN `0x59800`) via the PawnIO driver, same as LibreHardwareMonitor and Linux `k10temp` | PawnIO installed + run as admin |

Without PawnIO the CPU temp shows `---`; everything else still works.

## Build

```powershell
.\build.ps1
```

Uses the C# compiler built into Windows (`csc.exe` from .NET Framework 4). Produces `pa120.exe`
(console) and `pa120w.exe` (no window, for background use).

## CPU temperature: install PawnIO

Windows gives user programs no way to read CPU temperature, so a kernel driver is required.
[PawnIO](https://pawnio.eu) is open source, Microsoft-signed, and only runs signed, sandboxed
modules. `AMDFamily17.bin` here is the signed official module (PawnIO.Modules 0.2.11, as bundled
by LibreHardwareMonitor; LGPL-2.1, see `AMDFamily17.COPYING.txt`).

Download the installer from [pawnio.eu](https://pawnio.eu) or run `winget install namazso.PawnIO`
(winget isn't available on Windows LTSC editions).

## Keep it running

The cooler blanks its display a few seconds after it stops receiving frames, so pa120 has to run
continuously (see *Run at logon*). Only one instance can drive the display at a time; a second
one exits immediately. The windowless build writes status to `pa120.log` next to the exe.

## Use

```
pa120 [--color temp|rainbow|RRGGBB] [--interval ms] [--fahrenheit] [--once] [--test]
```

- `--color temp` (default): each reading is colored blue → green → yellow → red by its value
- `--color rainbow`: animated rainbow (use `--interval 100` for a smooth effect)
- `--color 00ffff`: one fixed color
- `--test`: lights all segments, then each of the 84 segments one by one, then a test pattern
  (`CPU 123° 45%`, `GPU 67° 100%`) — use this to check the layout

## Run at logon

Move this folder somewhere permanent, then in an **admin** PowerShell:

```powershell
.\install-task.ps1                       # or: .\install-task.ps1 -Arguments '--color rainbow --interval 100'
```

Remove with `Unregister-ScheduledTask -TaskName 'PA120 Display' -Confirm:$false`.

## Protocol

USB HID `0416:8001`. One frame = 20-byte header `DA DB DC DD 00×12 FC 00 00 FF` followed by
84 × RGB (one triplet per segment, `000000` = off), split into 64-byte output reports
(on Windows each prefixed with report ID `00`). Segment map is in `Layout` in `pa120.cs`.

Protocol credit: [raffa0001/Peerless_assassin_and_CLI_UI](https://github.com/raffa0001/Peerless_assassin_and_CLI_UI) (MIT).

## License

MIT
