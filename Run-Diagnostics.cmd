@echo off
setlocal
set "EXE=%ProgramFiles%\SpacePilotBridge\Diagnostics\SpacePilotBridge.Status.exe"
if not exist "%EXE%" (
  echo SpacePilotBridge is not installed yet. Run Setup.cmd first.
  pause
  exit /b 1
)
"%EXE%"
