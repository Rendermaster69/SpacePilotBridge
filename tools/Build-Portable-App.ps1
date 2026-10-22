$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src\SpacePilotBridge.App\SpacePilotBridge.App.csproj'
$out = Join-Path $root 'dist\portable'

if (-not (Get-Command dotnet.exe -ErrorAction SilentlyContinue)) {
    throw '.NET 10 SDK is required to build the portable development package.'
}

Write-Host 'Building fully self-contained portable SpacePilotBridge.exe...' -ForegroundColor Cyan
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $out | Out-Null
& dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $out
if ($LASTEXITCODE -ne 0) { throw 'Portable app build failed.' }
Write-Host "Portable build: $out\SpacePilotBridge.exe" -ForegroundColor Green
Read-Host 'Press Enter to close'
