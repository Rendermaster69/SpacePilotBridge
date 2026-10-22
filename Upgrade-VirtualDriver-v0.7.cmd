@echo off
setlocal
TITLE SpacePilotBridge v0.7 Virtual Gamepad Driver Upgrade
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File ""%~dp0tools\Upgrade-VirtualDriver.ps1""'"
echo.
echo Driver upgrade launcher finished.
pause
