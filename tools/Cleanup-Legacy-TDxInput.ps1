#requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'

$deviceClsid = '{82C5AB54-C92C-4D52-AAC5-27E25E22604C}'
$key = "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\CLSID\$deviceClsid\InprocServer32"
if (!(Test-Path $key)) { return }

$inproc = (Get-Item $key).GetValue('')
if ([string]::IsNullOrWhiteSpace([string]$inproc)) { return }

$path = [string]$inproc
$isOpenNdof = $path -match '(?i)OpenNDOF' -or ($path -match '(?i)TDxInput\.comhost\.dll' -and $path -notmatch '(?i)3Dconnexion')
if (-not $isOpenNdof) { return }

Write-Host ''
Write-Host 'Found the experimental OpenNDOF TDxInput COM registration from the earlier tests:' -ForegroundColor Yellow
Write-Host "  $path" -ForegroundColor Yellow
Write-Host 'Removing it so it cannot override current 3DxWare/Maya integration.' -ForegroundColor Yellow

$backupDir = Join-Path $env:LOCALAPPDATA 'SpacePilotBridge\registry-backup'
New-Item -ItemType Directory -Force $backupDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
& reg.exe export 'HKLM\SOFTWARE\Classes\TDxInput.Device' (Join-Path $backupDir "TDxInput.Device-$stamp.reg") /y 2>$null | Out-Null

$classes = @(
    @{ Prog='TDxInput.Device';    Clsid='{82C5AB54-C92C-4D52-AAC5-27E25E22604C}' },
    @{ Prog='TDxInput.Sensor';    Clsid='{85004B00-1AA7-4777-B1CE-8427301B942D}' },
    @{ Prog='TDxInput.Keyboard';  Clsid='{25BBE090-583A-4903-A61B-D0EC629AC4EC}' },
    @{ Prog='TDxInput.TDxInfo';   Clsid='{1A960ECE-0E57-4A68-B694-8373114F1FF4}' },
    @{ Prog='TDxInput.AngleAxis'; Clsid='{512A6C3E-3010-401B-8623-E413E2ACC138}' },
    @{ Prog='TDxInput.Vector3D';  Clsid='{740A7479-C7C1-44DA-8A84-B5DE63C78B32}' }
)
foreach ($c in $classes) {
    Remove-Item "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\$($c.Prog)" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\CLSID\$($c.Clsid)" -Recurse -Force -ErrorAction SilentlyContinue
}

New-Item -ItemType File -Force (Join-Path $backupDir '3DxWare-repair-recommended.flag') | Out-Null
Write-Host "Legacy TDxInput registration removed. Backup: $backupDir" -ForegroundColor Green
