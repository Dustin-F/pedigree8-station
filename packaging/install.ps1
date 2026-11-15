# Pedigree8 Station installer — runs on the tablet, no administrator rights needed.
# Installs the app, adds Start menu + desktop shortcuts and starts it with Windows.
# Existing station data (%LOCALAPPDATA%\Pedigree8) is never touched.
$ErrorActionPreference = "Stop"
$source = Join-Path $PSScriptRoot "app"
$target = Join-Path $env:LOCALAPPDATA "Programs\Pedigree8Station"
$exe = Join-Path $target "Pedigree8Station.exe"
$dataDb = Join-Path $env:LOCALAPPDATA "Pedigree8\station.db"

Write-Host ""
Write-Host "Installing Pedigree8 Station..." -ForegroundColor Cyan

$release = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" -ErrorAction SilentlyContinue).Release
if (-not $release -or $release -lt 528040) {
    Write-Host "This tablet needs .NET Framework 4.8 (normally part of Windows 10). Run Windows Update, then try again." -ForegroundColor Red
    Read-Host "Press Enter to close"; exit 1
}

Get-Process Pedigree8Station -ErrorAction SilentlyContinue | ForEach-Object { $_.CloseMainWindow() | Out-Null; Start-Sleep 2; if (-not $_.HasExited) { $_.Kill() } }

New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $source "*") $target -Recurse -Force

$shell = New-Object -ComObject WScript.Shell
foreach ($folder in @([Environment]::GetFolderPath("Programs"), [Environment]::GetFolderPath("Desktop"))) {
    $link = $shell.CreateShortcut((Join-Path $folder "Pedigree8 Station.lnk"))
    $link.TargetPath = $exe
    $link.Arguments = "--kiosk"
    $link.WorkingDirectory = $target
    $link.Save()
}

Set-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "Pedigree8Station" -Value "`"$exe`" --kiosk"

if (Test-Path $dataDb) {
    Write-Host "Existing station data found and kept: $dataDb" -ForegroundColor Yellow
} else {
    Write-Host "Your collection will be loaded the first time the app opens."
}

Write-Host "Done. Pedigree8 Station will now open, and will start by itself whenever Windows starts." -ForegroundColor Green
Start-Process $exe "--kiosk"
Start-Sleep 3
