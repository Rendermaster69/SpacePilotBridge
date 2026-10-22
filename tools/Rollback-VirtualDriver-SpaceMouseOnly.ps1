#requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$currentCommon = Join-Path $root 'driver\patches\common.h'
$currentPrepare = Join-Path $PSScriptRoot 'Prepare-VirtualDriver.ps1'
$rollbackDir = Join-Path $root 'driver\rollback-v06'
$tmpCommon = $currentCommon + '.v07tmp'
$tmpPrepare = $currentPrepare + '.v07tmp'
$installedExe = "$env:ProgramFiles\SpacePilotBridge\SpacePilotBridge.exe"

Get-Process -Name SpacePilotBridge -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Copy-Item $currentCommon $tmpCommon -Force
Copy-Item $currentPrepare $tmpPrepare -Force
try {
    Copy-Item (Join-Path $rollbackDir 'common.h') $currentCommon -Force
    Copy-Item (Join-Path $rollbackDir 'Prepare-VirtualDriver.ps1') $currentPrepare -Force
    & (Join-Path $PSScriptRoot 'Build-VirtualDriver.ps1')
    & (Join-Path $PSScriptRoot 'Install-VirtualDriver.ps1') -DriverPath (Join-Path $root 'dist\driver')
}
finally {
    Copy-Item $tmpCommon $currentCommon -Force
    Copy-Item $tmpPrepare $currentPrepare -Force
    Remove-Item $tmpCommon,$tmpPrepare -Force -ErrorAction SilentlyContinue
}
if (Test-Path $installedExe) { Start-Process $installedExe }
Write-Host 'Rolled back to SpaceMouse-only virtual HID driver.' -ForegroundColor Green
Read-Host 'Press Enter to close'
