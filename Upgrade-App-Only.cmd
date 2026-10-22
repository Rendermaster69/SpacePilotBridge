@echo off
setlocal
TITLE SpacePilotBridge v0.9.2 App Upgrade
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Upgrade-App-Launcher.ps1"
echo.
echo SpacePilotBridge app upgrade launcher has finished.
pause
