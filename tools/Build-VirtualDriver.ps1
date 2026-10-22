$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
& (Join-Path $PSScriptRoot 'Prepare-VirtualDriver.ps1')

$sample = Join-Path $root 'driver\windows-driver-samples\hid\vhidmini2'
$project = Join-Path $sample 'driver\umdf2\VhidminiUm.vcxproj'
$driverDist = Join-Path $root 'dist\driver'
New-Item -ItemType Directory -Force $driverDist | Out-Null

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (!(Test-Path $vswhere)) { throw 'Visual Studio/Build Tools not found. Run tools\Install-Prerequisites.ps1.' }
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
if (!$msbuild) { throw 'MSBuild not found. Run tools\Install-Prerequisites.ps1.' }

Write-Host 'Building UMDF2 virtual HID driver...' -ForegroundColor Cyan
& $msbuild $project /m /t:Rebuild /p:Configuration=Release /p:Platform=x64 /p:SignMode=Off /verbosity:minimal
if ($LASTEXITCODE -ne 0) { throw "Virtual driver build failed with exit code $LASTEXITCODE" }

$inf = Get-ChildItem (Join-Path $sample 'driver\umdf2') -Filter VhidminiUm.inf -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$dll = Get-ChildItem (Join-Path $sample 'driver\umdf2') -Filter VhidminiUm.dll -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (!$inf -or !$dll) { throw 'Built VhidminiUm.inf / VhidminiUm.dll not found.' }

Copy-Item $inf.FullName (Join-Path $driverDist 'SpacePilotBridgeVhid.inf') -Force
Copy-Item $dll.FullName (Join-Path $driverDist 'VhidminiUm.dll') -Force

# Make sure the service binary name remains the actual DLL filename while using
# a friendly INF filename.
$installedInf = Join-Path $driverDist 'SpacePilotBridgeVhid.inf'
$text = Get-Content $installedInf -Raw
$text = $text -replace 'CatalogFile\s*=\s*[^\r\n]+', 'CatalogFile=SpacePilotBridgeVhid.cat'
Set-Content $installedInf $text -Encoding ascii

$binRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"

# Inf2Cat is not guaranteed to be installed in the x64 subfolder.
# Depending on the WDK release it may live under x86, x64, or another
# architecture folder. Search all installed kit versions and prefer the
# newest version, with x64 first when both architectures exist.
$inf2catCandidates = Get-ChildItem $binRoot -Filter Inf2Cat.exe -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
    $version = [version]'0.0'
    if ($_.FullName -match '\\bin\\(?<ver>10\.0\.\d+\.\d+)\\') {
        try { $version = [version]$Matches.ver } catch {}
    }
    $archRank = if ($_.FullName -match '\\x64\\Inf2Cat\.exe$') { 2 } elseif ($_.FullName -match '\\x86\\Inf2Cat\.exe$') { 1 } else { 0 }
    [pscustomobject]@{ File = $_; Version = $version; ArchRank = $archRank }
} | Sort-Object Version, ArchRank -Descending

$inf2cat = $inf2catCandidates | Select-Object -First 1
if (!$inf2cat) {
    throw "Inf2Cat.exe not found anywhere under '$binRoot'. The WDK installation may be incomplete."
}

Write-Host "Using Inf2Cat: $($inf2cat.File.FullName)" -ForegroundColor DarkGray
Remove-Item (Join-Path $driverDist '*.cat') -Force -ErrorAction SilentlyContinue
Write-Host 'Creating driver catalog...' -ForegroundColor Cyan
& $inf2cat.File.FullName /driver:$driverDist /os:10_X64
if ($LASTEXITCODE -ne 0) { throw "Inf2Cat failed with exit code $LASTEXITCODE" }

Write-Host "Virtual driver package built: $driverDist" -ForegroundColor Green
