@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Build-And-Install.ps1"
if errorlevel 1 pause
