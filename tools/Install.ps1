#requires -RunAsAdministrator
param([string]$Source = (Join-Path $PSScriptRoot '..\dist'))
$ErrorActionPreference = 'Stop'
$Source = (Resolve-Path $Source).Path
$target = "$env:ProgramFiles\SpacePilotBridge"

Get-Process SpacePilotBridge -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process SpacePilotBridge.Status -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

& (Join-Path $PSScriptRoot 'Install-VirtualDriver.ps1') -DriverPath (Join-Path $Source 'driver')

New-Item -ItemType Directory -Force $target | Out-Null
Remove-Item (Join-Path $target '*') -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $Source 'app\*') $target -Recurse -Force
New-Item -ItemType Directory -Force (Join-Path $target 'Diagnostics') | Out-Null
Copy-Item (Join-Path $Source 'status\*') (Join-Path $target 'Diagnostics') -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'Uninstall.ps1') (Join-Path $target 'Uninstall.ps1') -Force
Copy-Item (Join-Path $PSScriptRoot 'Uninstall-VirtualDriver.ps1') (Join-Path $target 'Uninstall-VirtualDriver.ps1') -Force

# Start with the current user at logon.
$startup = [Environment]::GetFolderPath('Startup')
$shortcutPath = Join-Path $startup 'SpacePilotBridge.lnk'
$ws = New-Object -ComObject WScript.Shell
$shortcut = $ws.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $target 'SpacePilotBridge.exe'
$shortcut.WorkingDirectory = $target
$shortcut.Description = 'SpacePilot to virtual SpaceMouse bridge'
$shortcut.Save()

# Current official 3DxWare is intentionally not redistributed by this project.
$threeDx = @(
    "$env:ProgramFiles\3Dconnexion\3DxWare",
    "${env:ProgramFiles(x86)}\3Dconnexion\3DxWare"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (!$threeDx) {
    Write-Host ''
    Write-Host 'IMPORTANT: Current 3Dconnexion 3DxWare is not detected.' -ForegroundColor Yellow
    Write-Host 'Install the current 3DxWare driver so Fusion/Maya/ZBrush can consume the virtual SpaceMouse.' -ForegroundColor Yellow
}

Write-Host "Installed to $target" -ForegroundColor Green
Start-Process (Join-Path $target 'SpacePilotBridge.exe')
