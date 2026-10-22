@echo off
set "LOG=%LOCALAPPDATA%\SpacePilotBridge\setup.log"
if exist "%LOG%" (
  notepad "%LOG%"
) else (
  echo No setup log exists yet: %LOG%
  pause
)
