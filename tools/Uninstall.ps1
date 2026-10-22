#requires -RunAsAdministrator
$ErrorActionPreference = 'Continue'
Get-Process SpacePilotBridge -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process SpacePilotBridge.Status -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
& (Join-Path $PSScriptRoot 'Uninstall-VirtualDriver.ps1')
$startup = [Environment]::GetFolderPath('Startup')
Remove-Item (Join-Path $startup 'SpacePilotBridge.lnk') -Force -ErrorAction SilentlyContinue
Remove-Item "$env:ProgramFiles\SpacePilotBridge" -Recurse -Force -ErrorAction SilentlyContinue
Write-Host 'SpacePilotBridge uninstalled.' -ForegroundColor Green
