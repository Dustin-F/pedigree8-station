# Removes the Pedigree8 Station program, shortcuts and auto-start.
# Your data and backups in %LOCALAPPDATA%\Pedigree8 are KEPT.
Get-Process Pedigree8Station -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() }
Remove-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "Pedigree8Station" -ErrorAction SilentlyContinue
foreach ($folder in @([Environment]::GetFolderPath("Programs"), [Environment]::GetFolderPath("Desktop"))) {
    Remove-Item (Join-Path $folder "Pedigree8 Station.lnk") -ErrorAction SilentlyContinue
}
Remove-Item (Join-Path $env:LOCALAPPDATA "Programs\Pedigree8Station") -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Pedigree8 Station removed. Your data is still in $(Join-Path $env:LOCALAPPDATA 'Pedigree8')."
