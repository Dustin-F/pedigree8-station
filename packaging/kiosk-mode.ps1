# Turns the tablet into a station: switch it on and Pedigree8 Station opens by itself, full screen.
# Also keeps Windows out of the way: no lock screen after the screen sleeps, no edge-swipe menus,
# no update restarts during the day. Run kiosk-mode.bat after install.bat. Undo with kiosk-off.bat.
param([switch]$Elevated)
$ErrorActionPreference = "Stop"
$exe = Join-Path $env:LOCALAPPDATA "Programs\Pedigree8Station\Pedigree8Station.exe"

if (-not $Elevated) {
    if (-not (Test-Path $exe)) {
        Write-Host "Install Pedigree8 Station first (double-click install.bat)." -ForegroundColor Red
        Read-Host "Press Enter to close"; exit 1
    }
    Write-Host ""
    Write-Host "Setting up kiosk mode..." -ForegroundColor Cyan

    # Start with Windows, full screen, without the usual startup-app delay.
    Set-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "Pedigree8Station" -Value "`"$exe`" --kiosk"
    $serialize = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize"
    if (-not (Test-Path $serialize)) { New-Item $serialize | Out-Null }
    Set-ItemProperty $serialize -Name "StartupDelayInMSec" -Value 0 -Type DWord

    Write-Host "Windows will now ask for permission to change tablet settings - tap Yes."
    try {
        Start-Process powershell -Verb RunAs -Wait -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Elevated"
    } catch {
        Write-Host "Permission was not given, so only the auto-start part is done. Run kiosk-mode.bat again to finish." -ForegroundColor Yellow
        Read-Host "Press Enter to close"; exit 1
    }
    Write-Host ""
    Write-Host "Kiosk mode is on. Restart the tablet to try it." -ForegroundColor Green
    Read-Host "Press Enter to close"
    exit 0
}

# --- Tablet-wide settings (runs with permission) ---

# No edge-swipe menus (Action Center, Task View) over the app.
$edge = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\EdgeUI"
if (-not (Test-Path $edge)) { New-Item $edge -Force | Out-Null }
Set-ItemProperty $edge -Name "AllowEdgeSwipe" -Value 0 -Type DWord

# While plugged in: screen off after 15 minutes, never sleep, no sign-in when it wakes up.
powercfg /change monitor-timeout-ac 15
powercfg /change standby-timeout-ac 0
powercfg /setacvalueindex SCHEME_CURRENT SUB_NONE CONSOLELOCK 0
powercfg /setdcvalueindex SCHEME_CURRENT SUB_NONE CONSOLELOCK 0
powercfg /setactive SCHEME_CURRENT

# Windows Update may only restart between 23:00 and 06:00.
$ux = "HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings"
if (-not (Test-Path $ux)) { New-Item $ux -Force | Out-Null }
Set-ItemProperty $ux -Name "ActiveHoursStart" -Value 6 -Type DWord
Set-ItemProperty $ux -Name "ActiveHoursEnd" -Value 23 -Type DWord
Set-ItemProperty $ux -Name "SmartActiveHoursState" -Value 0 -Type DWord

# Sign in by itself at power-on. Windows only allows this through its own screen (netplwiz);
# this makes its checkbox visible on PCs that use a PIN / Windows Hello.
$winlogon = Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
if ($winlogon.AutoAdminLogon -ne "1") {
    $pwl = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\PasswordLess\Device"
    if (-not (Test-Path $pwl)) { New-Item $pwl -Force | Out-Null }
    Set-ItemProperty $pwl -Name "DevicePasswordLessBuildVersion" -Value 0 -Type DWord

    Write-Host ""
    Write-Host "LAST STEP - automatic sign-in" -ForegroundColor Cyan
    Write-Host "A 'User Accounts' window opens. Untick 'Users must enter a user name and password",
               "to use this computer', tap OK, and type your Windows password twice."
    Write-Host "(If there is no tick box, the tablet already signs in by itself - just tap OK.)"
    Start-Process netplwiz -Wait
}
