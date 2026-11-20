# pa120 — open-source display driver for the Thermalright Peerless Assassin 120 Digital

A ~15 KB, dependency-free replacement for Thermalright's software. It shows CPU temp, CPU usage,
GPU temp and GPU usage on the cooler's segment display, and does nothing else. It uses ~0% CPU.

## Install

Open PowerShell and run:

```powershell
irm https://github.com/dietofworms/pa120-display/releases/latest/download/get.ps1 | iex
```

This downloads the latest release into `%LOCALAPPDATA%\pa120` and runs `install.ps1` from there.
To pass options, use this form instead:

```powershell
iex "& { $(irm https://github.com/dietofworms/pa120-display/releases/latest/download/get.ps1) } -Color rainbow"
```

### Manual install

1. Download the latest release's `pa120.zip` from the
   [Releases page](https://github.com/dietofworms/pa120-display/releases) (or `git clone` this repo)
   and extract it into a permanent folder, e.g. `C:\Users\<you>\Developer\pa120-display`.
   The startup task runs it from there.
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

Re-run `install.ps1` (or the one-line command) with options any time to change settings. Options you
leave out keep their current values:

```powershell
.\install.ps1 -Color ffffff                     # plain white
.\install.ps1 -Color rainbow -Interval 100      # animated rainbow
.\install.ps1 -Color temp                       # default: blue → green → yellow → red by value
.\install.ps1 -Fahrenheit
.\install.ps1 -Celsius                          # back from -Fahrenheit
.\install.ps1 -SkipPawnIO                       # don't offer PawnIO (CPU temp shows ---)
.\install.ps1 -InstallPawnIO                    # install PawnIO without asking
```

`-Color` takes `temp`, `rainbow`, or any hex RGB colour (`ff0000` red, `00ffff` cyan, ...).

### Update

Run the one-line install command again. It downloads the latest release over the old one,
rebuilds, and keeps your settings. From a manual install or git clone, replace the files
(or `git pull`) and re-run `install.ps1`.

Installs from before v1.0.0 can switch to the one-line command: it moves the startup task to
`%LOCALAPPDATA%\pa120` and keeps your settings, and you can delete the old folder afterwards.

`pa120 --version`, or the first line of `pa120.log`, shows the installed version.

### Uninstall

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
```

Run it from the install folder (`%LOCALAPPDATA%\pa120` for the one-line install).

Removes the startup task and stops pa120. PawnIO is left installed (other tools such as
LibreHardwareMonitor use it); remove it from **Settings → Apps** if you don't want it.

## How it works

| Stat      | Source                                                                  | Needs                          |
|-----------|-------------------------------------------------------------------------|--------------------------------|
| CPU usage | `GetSystemTimes` (Windows API)                                          | nothing                        |
| GPU temp / usage | `nvml.dll` (ships with the NVIDIA driver)                        | NVIDIA GPU                     |
| GPU temp / usage | `atiadlxx.dll` (AMD Display Library, ships with the AMD driver), same calls as LibreHardwareMonitor | AMD Radeon GPU |
| CPU temp  | AMD Zen `Tctl` register (SMN `0x59800`) via the PawnIO driver, same as LibreHardwareMonitor and Linux `k10temp` | PawnIO + admin (the startup task has both) |

The cooler blanks its display a few seconds after it stops receiving frames, so pa120 has to run
continuously. Only one copy can drive the display at a time; a second one exits immediately.
The background build writes status to `pa120.log` next to the exe.

NVIDIA is tried first, then AMD. With several AMD GPUs (e.g. a Radeon card plus Ryzen integrated
graphics), pa120 picks the discrete one. RX 5000 and newer report edge temperature (hotspot if edge
is missing) and graphics activity; older Radeons use the Overdrive N / 6 / 5 equivalents.

### About PawnIO

Windows doesn't let normal programs read CPU temperature, so a kernel driver is required.
[PawnIO](https://github.com/namazso/PawnIO) is open source, signed, and only runs small signed,
sandboxed modules, unlike the older WinRing0 driver that Windows Defender now flags.
`AMDFamily17.bin` here is the official signed module (PawnIO.Modules 0.2.11, as bundled by
LibreHardwareMonitor; LGPL-2.1, see `AMDFamily17.COPYING.txt`).

## Manual use

```
pa120 [--color temp|rainbow|RRGGBB] [--interval ms] [--fahrenheit] [--once] [--test] [--version]
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
