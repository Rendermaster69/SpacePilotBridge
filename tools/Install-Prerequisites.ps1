#requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'

function Have-MsBuild {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (!(Test-Path $vswhere)) { return $false }
    $p = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
    return [bool]$p
}
function Have-Wdk {
    return [bool](Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter inf2cat.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1)
}

if (-not (Get-Command winget.exe -ErrorAction SilentlyContinue)) {
    throw 'WinGet is required to install the build prerequisites automatically.'
}
if (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) {
    Write-Host 'Installing Git for Windows...' -ForegroundColor Cyan
    & winget install --id Git.Git --exact --accept-source-agreements --accept-package-agreements
}
if (-not (Get-Command dotnet.exe -ErrorAction SilentlyContinue) -or -not ((& dotnet --version) -like '10.*')) {
    Write-Host 'Installing .NET 10 SDK...' -ForegroundColor Cyan
    & winget install --id Microsoft.DotNet.SDK.10 --exact --accept-source-agreements --accept-package-agreements
}

if (-not (Have-MsBuild) -or -not (Have-Wdk)) {
    Write-Host ''
    Write-Host 'The virtual HID driver needs Visual Studio/Build Tools + Windows SDK + WDK.' -ForegroundColor Yellow
    Write-Host 'Microsoft''s official WinGet configuration will be used. This is a large install.' -ForegroundColor Yellow
    $answer = Read-Host 'Install the Microsoft driver-development prerequisites now? [Y/n]'
    if ($answer -and $answer -notmatch '^[Yy]') { throw 'Driver build prerequisites were not installed.' }
    & winget configure -f 'https://raw.githubusercontent.com/microsoft/Windows-driver-samples/main/_wdk_utils/winget/configs/wdk-vscommunity.dsc.yaml'
    if ($LASTEXITCODE -ne 0) { throw 'Microsoft WDK WinGet configuration failed.' }
}

Write-Host 'Prerequisites are installed.' -ForegroundColor Green
