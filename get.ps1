# One-line install and update for pa120. Downloads the latest release into
# %LOCALAPPDATA%\pa120 and runs install.ps1 from there.
#
#   irm https://github.com/dietofworms/pa120-display/releases/latest/download/get.ps1 | iex
#
# With options (same as install.ps1):
#
#   iex "& { $(irm https://github.com/dietofworms/pa120-display/releases/latest/download/get.ps1) } -Color rainbow"
#
# Running it again updates to the latest release and keeps your settings.
param(
    [string]$Color,
    [int]$Interval,
    [switch]$Fahrenheit,
    [switch]$Celsius,
    [switch]$InstallPawnIO,
    [switch]$SkipPawnIO,
    [string]$Dir = (Join-Path $env:LOCALAPPDATA 'pa120')
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$zip = Join-Path $env:TEMP 'pa120.zip'
Write-Host '== Downloading the latest pa120 release'
Invoke-WebRequest -UseBasicParsing 'https://github.com/dietofworms/pa120-display/releases/latest/download/pa120.zip' -OutFile $zip
New-Item -ItemType Directory -Force $Dir | Out-Null
# The zip holds no .exe, so a running pa120 doesn't block this; install.ps1 stops it before rebuilding.
Expand-Archive -Force $zip $Dir
Remove-Item $zip
Write-Host "== Extracted to $Dir"

$argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $Dir 'install.ps1'))
if ($Color)         { $argList += '-Color', $Color }
if ($Interval)      { $argList += '-Interval', $Interval }
if ($Fahrenheit)    { $argList += '-Fahrenheit' }
if ($Celsius)       { $argList += '-Celsius' }
if ($InstallPawnIO) { $argList += '-InstallPawnIO' }
if ($SkipPawnIO)    { $argList += '-SkipPawnIO' }
& powershell.exe @argList
