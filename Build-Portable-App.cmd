@echo off
setlocal
TITLE SpacePilotBridge v0.9 Portable Build
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Build-Portable-App.ps1"
pause
