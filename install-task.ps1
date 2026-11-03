# Registers pa120w.exe to start hidden at logon with admin rights (needed for PawnIO CPU temp).
# Run from an elevated PowerShell after moving this folder somewhere permanent.
# Remove with:  Unregister-ScheduledTask -TaskName 'PA120 Display' -Confirm:$false
param([string]$Arguments = '--color temp')
$exe = Join-Path $PSScriptRoot 'pa120w.exe'
$action   = New-ScheduledTaskAction -Execute $exe -Argument $Arguments -WorkingDirectory $PSScriptRoot
$trigger  = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -RunLevel Highest -LogonType Interactive
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit 0 -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -AllowStartIfOnBatteries
Register-ScheduledTask -TaskName 'PA120 Display' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName 'PA120 Display'
"Installed and started: $exe $Arguments"
