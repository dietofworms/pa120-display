# One-step setup: builds pa120, optionally installs PawnIO (for CPU temperature),
# and registers it to start hidden at every logon with admin rights.
#
#   powershell -ExecutionPolicy Bypass -File .\install.ps1
#   powershell -ExecutionPolicy Bypass -File .\install.ps1 -Color ffffff
#   powershell -ExecutionPolicy Bypass -File .\install.ps1 -Color rainbow -Interval 100
#
# Re-run it any time to change settings. Undo with uninstall.ps1.
param(
    [string]$Color = 'temp',          # temp | rainbow | RRGGBB
    [int]$Interval = 1000,            # ms between updates
    [switch]$Fahrenheit,
    [switch]$InstallPawnIO,           # install PawnIO without asking
    [switch]$SkipPawnIO,              # never install PawnIO (CPU temp shows ---)
    [switch]$Relaunched               # internal: set when the script re-ran itself elevated
)
$ErrorActionPreference = 'Stop'
$TaskName = 'PA120 Display'

function Test-Admin {
    ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Registering an elevated logon task needs admin, so re-run elevated (one UAC prompt).
if (-not (Test-Admin)) {
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"",
                 '-Color', $Color, '-Interval', $Interval, '-Relaunched')
    if ($Fahrenheit)    { $argList += '-Fahrenheit' }
    if ($InstallPawnIO) { $argList += '-InstallPawnIO' }
    if ($SkipPawnIO)    { $argList += '-SkipPawnIO' }
    Start-Process powershell.exe -ArgumentList $argList -Verb RunAs
    return
}

try {
    Set-Location $PSScriptRoot

    # a running copy locks the exe and would block the rebuild
    Get-Process pa120w, pa120 -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500

    Write-Host '== Building pa120'
    & "$PSScriptRoot\build.ps1"
    if ($LASTEXITCODE) { throw "build failed ($LASTEXITCODE)" }

    $pawnInstalled = (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO') -or
                     (Test-Path 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO')
    if ($pawnInstalled) {
        Write-Host '== PawnIO already installed (CPU temperature enabled)'
    } elseif (-not $SkipPawnIO) {
        $doInstall = $InstallPawnIO
        if (-not $doInstall) {
            Write-Host ''
            Write-Host 'CPU temperature needs PawnIO, an open-source, signed driver (https://pawnio.eu).'
            Write-Host 'Without it everything else works and CPU temp shows ---.'
            $doInstall = (Read-Host 'Install PawnIO now? [Y/n]') -notmatch '^[nN]'
        }
        if ($doInstall) {
            Write-Host '== Downloading PawnIO from its official GitHub release'
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            $rel = Invoke-RestMethod 'https://api.github.com/repos/namazso/PawnIO.Setup/releases/latest'
            $asset = $rel.assets | Where-Object name -eq 'PawnIO_setup.exe' | Select-Object -First 1
            if (-not $asset) { throw 'PawnIO_setup.exe not found in the latest release' }
            $setup = Join-Path $env:TEMP 'PawnIO_setup.exe'
            Invoke-WebRequest -UseBasicParsing $asset.browser_download_url -OutFile $setup
            $sig = Get-AuthenticodeSignature $setup
            if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'namazso') {
                Remove-Item $setup
                throw "PawnIO installer signature check failed ($($sig.Status), $($sig.SignerCertificate.Subject))"
            }
            Write-Host "   signature OK: $($sig.SignerCertificate.Subject)"
            Write-Host "== Installing PawnIO $($rel.tag_name) (follow the setup window)"
            Start-Process $setup -Wait
            Remove-Item $setup -ErrorAction SilentlyContinue
        }
    }

    $runArgs = "--color $Color --interval $Interval"
    if ($Fahrenheit) { $runArgs += ' --fahrenheit' }

    Write-Host '== Registering logon task'
    $exe = Join-Path $PSScriptRoot 'pa120w.exe'
    $action    = New-ScheduledTaskAction -Execute $exe -Argument $runArgs -WorkingDirectory $PSScriptRoot
    $trigger   = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
    $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -RunLevel Highest -LogonType Interactive
    $settings  = New-ScheduledTaskSettingsSet -ExecutionTimeLimit 0 -RestartCount 3 `
                     -RestartInterval (New-TimeSpan -Minutes 1) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal `
        -Settings $settings -Force | Out-Null
    Start-ScheduledTask -TaskName $TaskName

    Write-Host ''
    Write-Host "Done. pa120 is running and will start at every logon: pa120w.exe $runArgs"
    Write-Host "Status log: $PSScriptRoot\pa120.log"
}
catch {
    Write-Host "Install failed: $_" -ForegroundColor Red
    if ($Relaunched) { Read-Host 'Press Enter to close' }
    exit 1
}
if ($Relaunched) { Read-Host 'Press Enter to close' }
