$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'Upgrade-App-Only.ps1'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($isAdmin) {
    & $script
    exit $LASTEXITCODE
}
Write-Host 'Requesting administrator permission for the Program Files update...' -ForegroundColor Cyan
$args = @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $script + '"')) -join ' '
$p = Start-Process powershell.exe -Verb RunAs -ArgumentList $args -Wait -PassThru
exit $p.ExitCode
