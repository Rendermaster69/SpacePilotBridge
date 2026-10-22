@echo off
setlocal
TITLE SpacePilotBridge v0.9 Setup
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Setup-Launcher.ps1"
echo.
echo SpacePilotBridge setup launcher has finished.
pause
