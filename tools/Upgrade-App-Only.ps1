#requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$target = "$env:ProgramFiles\SpacePilotBridge"
$project = Join-Path $root 'src\SpacePilotBridge.App\SpacePilotBridge.App.csproj'
$out = Join-Path $root 'dist\app-upgrade'

if (-not (Get-Command dotnet.exe -ErrorAction SilentlyContinue)) {
    throw '.NET 10 SDK is required to build this development package.'
}

Write-Host 'Building slim single-file SpacePilotBridge.exe...' -ForegroundColor Cyan
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $out | Out-Null
& dotnet publish $project -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:PublishTrimmed=false -p:EnableCompressionInSingleFile=false -o $out
if ($LASTEXITCODE -ne 0) { throw 'App build failed.' }
$exe = Join-Path $out 'SpacePilotBridge.exe'
if (!(Test-Path $exe)) { throw 'Published SpacePilotBridge.exe was not found.' }

$installedExe = Join-Path $target 'SpacePilotBridge.exe'

function Stop-InstalledBridge {
    Write-Host 'Stopping the currently running SpacePilotBridge...' -ForegroundColor Cyan

    # Older development builds have used both process names. Stop them if present.
    # Get-Process with SilentlyContinue avoids treating "process not found" as a fatal error.
    foreach ($processName in @('SpacePilotBridge', 'SpacePilotBridge.App')) {
        Get-Process -Name $processName -ErrorAction SilentlyContinue |
            Stop-Process -Force -ErrorAction SilentlyContinue
    }

    # Stop any remaining process whose executable is the installed bridge.
    try {
        Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
            Where-Object { $_.ExecutablePath -and [string]::Equals($_.ExecutablePath, $installedExe, [StringComparison]::OrdinalIgnoreCase) } |
            ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    } catch { }

    # Stop-Process/taskkill can return before Windows releases the executable image.
    # Wait up to 15 seconds for the file to become replaceable.
    if (Test-Path $installedExe) {
        $unlocked = $false
        for ($i = 0; $i -lt 60; $i++) {
            try {
                $stream = [System.IO.File]::Open($installedExe, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
                $stream.Dispose()
                $unlocked = $true
                break
            }
            catch {
                Start-Sleep -Milliseconds 250
            }
        }
        if (-not $unlocked) {
            throw "The old SpacePilotBridge.exe is still locked after 15 seconds. Exit SpacePilotBridge from the tray or reboot once, then run Upgrade-App-Only.cmd again."
        }
    }
}

Stop-InstalledBridge
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item $exe $installedExe -Force
Write-Host "Updated: $installedExe" -ForegroundColor Green
Write-Host 'Existing virtual HID driver was left untouched.' -ForegroundColor DarkGray
Start-Process $installedExe
Write-Host 'SpacePilotBridge v0.9.2 started.' -ForegroundColor Green
Read-Host 'Press Enter to close'
