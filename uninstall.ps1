# Removes the logon task and stops pa120. PawnIO is left installed (it may be used by other
# tools such as LibreHardwareMonitor); remove it from Settings > Apps if you no longer want it.
#
#   powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
$TaskName = 'PA120 Display'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Start-Process powershell.exe -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"" -Verb RunAs
    return
}

Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
Get-Process pa120w, pa120 -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Host 'pa120 stopped and removed from logon. The cooler display will go dark shortly.'
Read-Host 'Press Enter to close'
