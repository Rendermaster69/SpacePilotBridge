@echo off
setlocal
TITLE SpacePilotBridge v0.9 Build App
set OUT=%~dp0dist\app-only
if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%"
dotnet publish "%~dp0src\SpacePilotBridge.App\SpacePilotBridge.App.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:PublishTrimmed=false -o "%OUT%"
if errorlevel 1 (
  echo.
  echo BUILD FAILED
  pause
  exit /b 1
)
echo.
echo Built: %OUT%\SpacePilotBridge.exe
pause
