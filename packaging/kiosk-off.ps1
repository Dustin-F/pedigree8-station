# Turns kiosk mode off again: edge swipes and the lock screen come back.
# The app still starts with Windows (turn that off in Backup & settings).
param([switch]$Elevated)

if (-not $Elevated) {
    Remove-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize" -Name "StartupDelayInMSec" -ErrorAction SilentlyContinue
    try {
        Start-Process powershell -Verb RunAs -Wait -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Elevated"
    } catch {
        Write-Host "Permission was not given - nothing else was changed." -ForegroundColor Yellow
        Read-Host "Press Enter to close"; exit 1
    }
    Write-Host "Kiosk mode is off. Restart the tablet to finish." -ForegroundColor Green
    Read-Host "Press Enter to close"
    exit 0
}

Remove-ItemProperty "HKLM:\SOFTWARE\Policies\Microsoft\Windows\EdgeUI" -Name "AllowEdgeSwipe" -ErrorAction SilentlyContinue
powercfg /setacvalueindex SCHEME_CURRENT SUB_NONE CONSOLELOCK 1
powercfg /setdcvalueindex SCHEME_CURRENT SUB_NONE CONSOLELOCK 1
powercfg /setactive SCHEME_CURRENT

Write-Host ""
Write-Host "To ask for a password at power-on again: a 'User Accounts' window opens -"
Write-Host "tick 'Users must enter a user name and password', tap OK. Or just close it."
Start-Process netplwiz -Wait
