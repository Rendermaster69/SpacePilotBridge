param([string]$SetupLog = (Join-Path $env:LOCALAPPDATA 'SpacePilotBridge\setup.log'))
$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'ERROR: Build-And-Install.ps1 must be run as administrator.' -ForegroundColor Red
    exit 1
}

$logDir = Split-Path -Parent $SetupLog
New-Item -ItemType Directory -Force $logDir | Out-Null
$exitCode = 0
try {
    Start-Transcript -Path $SetupLog -Append -Force | Out-Null
    function BuildToolsReady {
        $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
        $msbuild = $false
        if (Test-Path $vswhere) {
            $p = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
            $msbuild = [bool]$p
        }
        $wdk = [bool](Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter inf2cat.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1)
        return $msbuild -and $wdk
    }

    Write-Host 'SpacePilotBridge v0.9 setup' -ForegroundColor Cyan
    Write-Host 'Physical SpacePilot -> virtual SpaceMouse-class HID -> current 3DxWare' -ForegroundColor DarkCyan
    Write-Host ''

    # The user's earlier OpenNDOF experiments may have overwritten the official
    # TDxInput ProgIDs. Remove only registrations that explicitly point at the
    # custom OpenNDOF .NET comhost; never remove a 3Dconnexion installation.
    & (Join-Path $PSScriptRoot 'Cleanup-Legacy-TDxInput.ps1')

    Get-Process OpenNDOF -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    if (-not (Get-Command git.exe -ErrorAction SilentlyContinue) -or
        -not (Get-Command dotnet.exe -ErrorAction SilentlyContinue) -or
        -not (BuildToolsReady)) {
        & (Join-Path $PSScriptRoot 'Install-Prerequisites.ps1')
    }

    & (Join-Path $PSScriptRoot 'Build-Release.ps1')
    & (Join-Path $PSScriptRoot 'Install.ps1') -Source (Join-Path $PSScriptRoot '..\dist')

    Write-Host ''
    Write-Host 'SpacePilotBridge v0.9 is installed.' -ForegroundColor Green
    Write-Host 'The physical LCD is controlled by SpacePilotBridge and the tray app now feeds the virtual HID.' -ForegroundColor Cyan
    Write-Host ''

    $threeDxRoots = @(
        "$env:ProgramFiles\3Dconnexion",
        "${env:ProgramFiles(x86)}\3Dconnexion"
    ) | Where-Object { Test-Path $_ }
    $repairFlag = Join-Path $env:LOCALAPPDATA 'SpacePilotBridge\registry-backup\3DxWare-repair-recommended.flag'

    if ($threeDxRoots.Count -eq 0 -or (Test-Path $repairFlag)) {
        Write-Host 'ACTION NEEDED ONCE: install/repair the CURRENT 64-bit 3DxWare 10.' -ForegroundColor Yellow
        Write-Host 'This supplies the normal Fusion/Maya/ZBrush application integration.' -ForegroundColor Yellow
        $answer = Read-Host 'Open the official 3Dconnexion 3DxWare download page now? [Y/n]'
        if (-not $answer -or $answer -match '^[Yy]') {
            Start-Process 'https://3dconnexion.com/drivers-application/3dxware-10/'
        }
    } else {
        Write-Host '3DxWare installation detected.' -ForegroundColor Green
    }

    Write-Host ''
    Write-Host 'After 3DxWare is installed/repaired:' -ForegroundColor White
    Write-Host '  1. Unplug/replug the SpacePilot once.'
    Write-Host '  2. Start SpacePilotBridge (it is also added to Startup).'
    Write-Host '  3. Fully restart Fusion, Maya and ZBrush.'
    Write-Host '  4. Run Run-Diagnostics.cmd if needed.'
    Write-Host ''

}
catch {
    $exitCode = 1
    Write-Host ''
    Write-Host 'SETUP FAILED' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ''
    Write-Host "Full log: $SetupLog" -ForegroundColor Yellow
}
finally {
    try { Stop-Transcript | Out-Null } catch {}
    Write-Host ''
    if ($exitCode -eq 0) {
        Write-Host 'Setup finished successfully.' -ForegroundColor Green
    } else {
        Write-Host 'Setup did not finish. The window is staying open so the error is visible.' -ForegroundColor Yellow
    }
    Write-Host "Log saved to: $SetupLog" -ForegroundColor DarkGray
    Read-Host 'Press Enter to close this administrator window'
}
exit $exitCode
