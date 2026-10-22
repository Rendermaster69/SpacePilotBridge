$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dist = Join-Path $root 'dist'

if (-not (Get-Command dotnet.exe -ErrorAction SilentlyContinue)) { throw '.NET 10 SDK not found.' }
$ver = (& dotnet --version)
if (-not $ver.StartsWith('10.')) { throw ".NET 10 SDK required; found $ver" }

Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $dist | Out-Null

Write-Host 'Publishing SpacePilotBridge tray app...' -ForegroundColor Cyan
& dotnet publish (Join-Path $root 'src\SpacePilotBridge.App\SpacePilotBridge.App.csproj') -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:PublishTrimmed=false -o (Join-Path $dist 'app')
if ($LASTEXITCODE -ne 0) { throw 'Tray app build failed.' }

Write-Host 'Publishing diagnostic status monitor...' -ForegroundColor Cyan
& dotnet publish (Join-Path $root 'src\SpacePilotBridge.Status\SpacePilotBridge.Status.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o (Join-Path $dist 'status')
if ($LASTEXITCODE -ne 0) { throw 'Status monitor build failed.' }

& (Join-Path $PSScriptRoot 'Build-VirtualDriver.ps1')
Write-Host "Release built: $dist" -ForegroundColor Green
