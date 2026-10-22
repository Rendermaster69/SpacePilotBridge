#requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$installedExe = "$env:ProgramFiles\SpacePilotBridge\SpacePilotBridge.exe"

Write-Host 'SpacePilotBridge v0.7 virtual game-controller driver upgrade' -ForegroundColor Cyan
Write-Host 'This keeps the SpaceMouse collection and adds an optional generic 6-axis gamepad collection.' -ForegroundColor DarkGray

Get-Process -Name SpacePilotBridge -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

& (Join-Path $PSScriptRoot 'Build-VirtualDriver.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Driver build failed.' }
& (Join-Path $PSScriptRoot 'Install-VirtualDriver.ps1') -DriverPath (Join-Path $root 'dist\driver')
if ($LASTEXITCODE -ne 0) { throw 'Driver install failed.' }

if (Test-Path $installedExe) { Start-Process $installedExe }
Write-Host ''
Write-Host 'Driver upgraded. Reboot only if Windows asks for it.' -ForegroundColor Green
Read-Host 'Press Enter to close'
