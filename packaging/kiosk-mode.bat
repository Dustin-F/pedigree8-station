@echo off
rem Double-click after install.bat: the tablet then opens Pedigree8 Station by itself when switched on.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0kiosk-mode.ps1"
