# pa120 — open-source display driver for the Thermalright Peerless Assassin 120 Digital

A ~15 KB, dependency-free replacement for Thermalright's software. It shows CPU temp, CPU usage,
GPU temp and GPU usage on the cooler's segment display, and does nothing else. It uses ~0% CPU.

## Install

1. Download this repo (**Code → Download ZIP**, or `git clone`) and put the folder somewhere
   permanent, e.g. `C:\Users\<you>\Developer\pa120-display`. The startup task runs it from there.
2. Open PowerShell in that folder and run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install.ps1
   ```

That's it. The installer:

- asks for admin rights once (UAC),
- builds `pa120.exe` / `pa120w.exe` with the C# compiler built into Windows (nothing to download),
- offers to install [PawnIO](https://pawnio.eu) for CPU temperature: it downloads the official
  release from GitHub and checks its digital signature before running it,
- registers a **startup task** so the display runs automatically, hidden, at every logon, and
  starts it right away.

You can uninstall Thermalright's own software; if it keeps running it will fight pa120 for the display.

### Options

Re-run `install.ps1` with options any time to change settings:

```powershell
.\install.ps1 -Color ffffff                     # plain white
.\install.ps1 -Color rainbow -Interval 100      # animated rainbow
.\install.ps1 -Color temp                       # default: blue → green → yellow → red by value
.\install.ps1 -Fahrenheit
.\install.ps1 -SkipPawnIO                       # don't offer PawnIO (CPU temp shows ---)
.\install.ps1 -InstallPawnIO                    # install PawnIO without asking
```

`-Color` takes `temp`, `rainbow`, or any hex RGB colour (`ff0000` red, `00ffff` cyan, ...).

### Uninstall

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
```

Removes the startup task and stops pa120. PawnIO is left installed (other tools such as
LibreHardwareMonitor use it); remove it from **Settings → Apps** if you don't want it.

## How it works

| Stat      | Source                                                                  | Needs                          |
|-----------|-------------------------------------------------------------------------|--------------------------------|
| CPU usage | `GetSystemTimes` (Windows API)                                          | nothing                        |
| GPU temp / usage | `nvml.dll` (ships with the NVIDIA driver)                        | NVIDIA GPU                     |
| CPU temp  | AMD Zen `Tctl` register (SMN `0x59800`) via the PawnIO driver, same as LibreHardwareMonitor and Linux `k10temp` | PawnIO + admin (the startup task has both) |

The cooler blanks its display a few seconds after it stops receiving frames, so pa120 has to run
continuously. Only one copy can drive the display at a time; a second one exits immediately.
The background build writes status to `pa120.log` next to the exe.

### About PawnIO

Windows doesn't let normal programs read CPU temperature, so a kernel driver is required.
[PawnIO](https://github.com/namazso/PawnIO) is open source, signed, and only runs small signed,
sandboxed modules, unlike the older WinRing0 driver that Windows Defender now flags.
`AMDFamily17.bin` here is the official signed module (PawnIO.Modules 0.2.11, as bundled by
LibreHardwareMonitor; LGPL-2.1, see `AMDFamily17.COPYING.txt`).

## Manual use

```
pa120 [--color temp|rainbow|RRGGBB] [--interval ms] [--fahrenheit] [--once] [--test]
```

`pa120.exe` is the console build for trying things out; `pa120w.exe` has no window. Stop the
startup copy first (`uninstall.ps1`, or end `pa120w.exe` in Task Manager). `--test` lights all
segments, then each of the 84 segments in turn, then a test pattern, to check the layout.

To rebuild by hand: `.\build.ps1`.

## Protocol

USB HID `0416:8001`. One frame = 20-byte header `DA DB DC DD 00×12 FC 00 00 FF` followed by
84 × RGB (one triplet per segment, `000000` = off), split into 64-byte output reports
(on Windows each prefixed with report ID `00`). Segment map is in `Layout` in `pa120.cs`.

Protocol credit: [raffa0001/Peerless_assassin_and_CLI_UI](https://github.com/raffa0001/Peerless_assassin_and_CLI_UI) (MIT).

## License

MIT
