@echo off
setlocal
TITLE SpacePilotBridge Rollback Virtual Driver
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File ""%~dp0tools\Rollback-VirtualDriver-SpaceMouseOnly.ps1""'"
echo.
echo Rollback launcher finished.
pause
