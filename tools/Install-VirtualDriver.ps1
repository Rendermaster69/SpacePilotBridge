#requires -RunAsAdministrator
param([string]$DriverPath = (Join-Path $PSScriptRoot '..\dist\driver'))
$ErrorActionPreference = 'Stop'
$DriverPath = (Resolve-Path $DriverPath).Path
$inf = Join-Path $DriverPath 'SpacePilotBridgeVhid.inf'
$cat = Join-Path $DriverPath 'SpacePilotBridgeVhid.cat'
if (!(Test-Path $inf) -or !(Test-Path $cat)) { throw 'Virtual driver package is incomplete. Build it first.' }

# Create/reuse a machine-local code-signing certificate for internal/private use.
$subject = 'CN=SpacePilotBridge Local Driver'
$cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } | Sort-Object NotAfter -Descending | Select-Object -First 1
if (!$cert) {
    Write-Host 'Creating local driver signing certificate...' -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -CertStoreLocation Cert:\LocalMachine\My -HashAlgorithm SHA256 -KeyAlgorithm RSA -KeyLength 3072 -KeyExportPolicy Exportable -NotAfter (Get-Date).AddYears(5)
}
$cer = Join-Path $DriverPath 'SpacePilotBridge-LocalDriver.cer'
Export-Certificate -Cert $cert -FilePath $cer -Force | Out-Null
& certutil.exe -f -addstore Root $cer | Out-Null
& certutil.exe -f -addstore TrustedPublisher $cer | Out-Null

$binRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
$signtool = Get-ChildItem $binRoot -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } | Sort-Object FullName -Descending | Select-Object -First 1
if (!$signtool) { throw 'signtool.exe not found. Install the WDK.' }

Write-Host "Signing local driver catalog with certificate $($cert.Thumbprint) from LocalMachine\My..." -ForegroundColor Cyan
# The certificate is deliberately created in LocalMachine\My. SignTool searches
# CurrentUser\My by default, so /sm is required to select the machine store.
& $signtool.FullName sign /v /fd SHA256 /sm /s My /sha1 $cert.Thumbprint $cat
if ($LASTEXITCODE -ne 0) { throw 'Driver catalog signing failed.' }

$devcon = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10" -Filter devcon.exe -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '\\x64\\devcon\.exe$' } | Sort-Object FullName -Descending | Select-Object -First 1
if (!$devcon) { throw 'devcon.exe not found in the WDK.' }

# Remove an older instance first. Ignore 'not found'.
& $devcon.FullName remove 'root\SpacePilotBridgeVhid' | Out-Host
Write-Host 'Installing virtual HID device...' -ForegroundColor Cyan
& $devcon.FullName install $inf 'root\SpacePilotBridgeVhid' | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw @'
Windows rejected the locally signed UMDF driver package. On managed PCs, WDAC/driver policies can forbid local test certificates. See README.md -> Driver signing/troubleshooting. Do not disable Secure Boot unless you explicitly choose the test-signing fallback.
'@
}

Write-Host 'Virtual HID installed.' -ForegroundColor Green
