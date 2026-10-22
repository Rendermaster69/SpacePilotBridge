@echo off
setlocal
TITLE SpacePilotBridge v0.9 Resume Setup
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Setup-Launcher.ps1"
echo.
echo Resume setup has finished.
pause
