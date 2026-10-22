$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script = Join-Path $PSScriptRoot 'Build-And-Install.ps1'
$logDir = Join-Path $env:LOCALAPPDATA 'SpacePilotBridge'
New-Item -ItemType Directory -Force $logDir | Out-Null
$log = Join-Path $logDir 'setup.log'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if ($isAdmin) {
    & $script -SetupLog $log
    exit $LASTEXITCODE
}

Write-Host 'Requesting administrator permission...' -ForegroundColor Cyan
$args = @(
    '-NoProfile',
    '-ExecutionPolicy', 'Bypass',
    '-File', ('"' + $script + '"'),
    '-SetupLog', ('"' + $log + '"')
) -join ' '
$p = Start-Process powershell.exe -Verb RunAs -ArgumentList $args -Wait -PassThru
Write-Host ''
Write-Host "Administrator setup returned exit code $($p.ExitCode)." -ForegroundColor $(if ($p.ExitCode -eq 0) { 'Green' } else { 'Yellow' })
Write-Host "Full log: $log" -ForegroundColor DarkGray
exit $p.ExitCode
