#requires -RunAsAdministrator
$ErrorActionPreference = 'Continue'
$devcon = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10" -Filter devcon.exe -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '\\x64\\devcon\.exe$' } | Sort-Object FullName -Descending | Select-Object -First 1
if ($devcon) { & $devcon.FullName remove 'root\SpacePilotBridgeVhid' | Out-Host }
Get-ChildItem Cert:\LocalMachine\My | Where-Object Subject -eq 'CN=SpacePilotBridge Local Driver' | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem Cert:\LocalMachine\Root | Where-Object Subject -eq 'CN=SpacePilotBridge Local Driver' | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem Cert:\LocalMachine\TrustedPublisher | Where-Object Subject -eq 'CN=SpacePilotBridge Local Driver' | Remove-Item -Force -ErrorAction SilentlyContinue
Write-Host 'Virtual HID removed.' -ForegroundColor Green
