$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$cache = Join-Path $root 'driver\windows-driver-samples'
$sample = Join-Path $cache 'hid\vhidmini2'

if (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) {
    throw 'Git for Windows is required. Install Git and run this script again.'
}

if (!(Test-Path (Join-Path $sample 'driver\vhidmini.c'))) {
    Write-Host 'Fetching Microsoft Windows-driver-samples / hid/vhidmini2 ...' -ForegroundColor Cyan
    Remove-Item $cache -Recurse -Force -ErrorAction SilentlyContinue
    & git clone --depth 1 --filter=blob:none --sparse https://github.com/microsoft/Windows-driver-samples.git $cache
    if ($LASTEXITCODE -ne 0) { throw 'git clone failed.' }
    Push-Location $cache
    try {
        & git sparse-checkout set hid/vhidmini2
        if ($LASTEXITCODE -ne 0) { throw 'git sparse-checkout failed.' }
    } finally { Pop-Location }
}

$driverDir = Join-Path $sample 'driver'
$incDir = Join-Path $sample 'inc'
$cPath = Join-Path $driverDir 'vhidmini.c'
$hPath = Join-Path $driverDir 'vhidmini.h'
$infPath = Join-Path $driverDir 'umdf2\VhidminiUm.inx'

# Start from the pristine sample every time so patching is repeatable.
Push-Location $cache
try {
    & git checkout -- hid/vhidmini2
    if ($LASTEXITCODE -ne 0) { throw 'Could not reset Microsoft sample.' }
} finally { Pop-Location }

Copy-Item (Join-Path $root 'driver\patches\common.h') (Join-Path $incDir 'common.h') -Force

function Replace-One([string]$text, [string]$pattern, [string]$replacement, [string]$name) {
    $rx = [regex]::new($pattern, [Text.RegularExpressions.RegexOptions]::Singleline)
    $m = $rx.Matches($text)
    if ($m.Count -ne 1) { throw "Patch '$name' expected one match, found $($m.Count). Microsoft sample may have changed." }
    return $rx.Replace($text, $replacement, 1)
}

$h = Get-Content $hPath -Raw
$h = $h -replace '#define HIDMINI_PID\s+0xFEED', '#define HIDMINI_PID             0xC631'
$h = $h -replace '#define HIDMINI_VID\s+0xDEED', '#define HIDMINI_VID             0x256F'
$h = $h -replace '#define HIDMINI_VERSION\s+0x0101', '#define HIDMINI_VERSION         0x0200'
$h = $h -replace 'BYTE\s+DeviceData;', "BYTE                    DeviceData;`r`n    SPB_DEVICE_STATE        State;`r`n    ULONG                   LastSentButtons;`r`n    UCHAR                   LedState;"
Set-Content $hPath $h -Encoding ascii

$c = Get-Content $cPath -Raw

$descriptor = @'
HID_REPORT_DESCRIPTOR       G_DefaultReportDescriptor[] = {
    /* Public multi-axis collection. Compatibility identity is intentionally
       SpaceMouse Pro Wireless (cabled), VID 256F PID C631. */
    0x05,0x01, 0x09,0x08, 0xA1,0x01,
    0xA1,0x00, 0x85,0x01,
    0x16,0xA2,0xFE, 0x26,0x5E,0x01,
    0x36,0x88,0xFA, 0x46,0x78,0x05,
    0x55,0x0C, 0x65,0x11,
    0x09,0x30, 0x09,0x31, 0x09,0x32,
    0x09,0x33, 0x09,0x34, 0x09,0x35,
    0x75,0x10, 0x95,0x06, 0x81,0x02,
    0xC0,
    0xA1,0x00, 0x85,0x03,
    0x15,0x00, 0x25,0x01, 0x75,0x01, 0x95,0x20,
    0x05,0x09, 0x19,0x01, 0x29,0x20, 0x81,0x02,
    0xC0,
    0xA1,0x02, 0x85,0x04,
    0x05,0x08, 0x09,0x4B, 0x15,0x00, 0x25,0x01,
    0x95,0x01, 0x75,0x01, 0x91,0x02,
    0x95,0x01, 0x75,0x07, 0x91,0x03,
    0xC0,
    0xC0,

    /* Private vendor collection used only by SpacePilotBridge.exe to feed
       the current physical SpacePilot state into this UMDF device. */
    0x06,0x00,0xFF, 0x09,0x01, 0xA1,0x01,
    0x85,0x7F, 0x09,0x01,
    0x15,0x00, 0x26,0xFF,0x00,
    0x75,0x08, 0x95,0x11, 0xB1,0x02,
    0xC0
};
'@
$c = Replace-One $c 'HID_REPORT_DESCRIPTOR\s+G_DefaultReportDescriptor\[\]\s*=\s*\{.*?\n\};' $descriptor 'report descriptor'

# Initialize bridge state in EvtDeviceAdd.
$c = $c -replace 'deviceContext->DeviceData\s*=\s*0;', "deviceContext->DeviceData   = 0;`r`n    RtlZeroMemory(&deviceContext->State, sizeof(deviceContext->State));`r`n    deviceContext->LastSentButtons = 0;`r`n    deviceContext->LedState = 0;"

$writeReport = @'
NTSTATUS
WriteReport(
    _In_  PQUEUE_CONTEXT QueueContext,
    _In_  WDFREQUEST Request
    )
{
    NTSTATUS status;
    HID_XFER_PACKET packet;
    status = RequestGetHidXferPacket_ToWriteToDevice(Request, &packet);
    if (!NT_SUCCESS(status)) return status;

    if (packet.reportId == LED_REPORT_ID && packet.reportBufferLen >= sizeof(SPB_LED_REPORT)) {
        PSPB_LED_REPORT led = (PSPB_LED_REPORT)packet.reportBuffer;
        QueueContext->DeviceContext->LedState = led->LedBits;
        WdfRequestSetInformation(Request, sizeof(SPB_LED_REPORT));
        return STATUS_SUCCESS;
    }
    return STATUS_INVALID_PARAMETER;
}

HRESULT
GetFeature(
'@
$c = Replace-One $c 'NTSTATUS\s*\r?\nWriteReport\(.*?\r?\nHRESULT\s*\r?\nGetFeature\(' $writeReport 'WriteReport'

$setFeature = @'
NTSTATUS
SetFeature(
    _In_  PQUEUE_CONTEXT QueueContext,
    _In_  WDFREQUEST Request
    )
{
    NTSTATUS status;
    HID_XFER_PACKET packet;
    ULONG reportSize;
    PHIDMINI_CONTROL_INFO controlInfo;
    PHID_DEVICE_ATTRIBUTES hidAttributes = &QueueContext->DeviceContext->HidDeviceAttributes;

    status = RequestGetHidXferPacket_ToWriteToDevice(Request, &packet);
    if (!NT_SUCCESS(status)) return status;
    if (packet.reportId != CONTROL_COLLECTION_REPORT_ID) return STATUS_INVALID_PARAMETER;

    reportSize = sizeof(HIDMINI_CONTROL_INFO);
    if (packet.reportBufferLen < reportSize) return STATUS_INVALID_BUFFER_SIZE;
    controlInfo = (PHIDMINI_CONTROL_INFO)packet.reportBuffer;

    switch (controlInfo->ControlCode) {
    case HIDMINI_CONTROL_CODE_SET_ATTRIBUTES:
        hidAttributes->ProductID = controlInfo->u.Attributes.ProductID;
        hidAttributes->VendorID = controlInfo->u.Attributes.VendorID;
        hidAttributes->VersionNumber = controlInfo->u.Attributes.VersionNumber;
        break;
    case HIDMINI_CONTROL_CODE_SET_STATE:
        QueueContext->DeviceContext->State = controlInfo->u.State;
        break;
    default:
        return STATUS_NOT_IMPLEMENTED;
    }

    WdfRequestSetInformation(Request, reportSize);
    return STATUS_SUCCESS;
}

NTSTATUS
GetInputReport(
'@
$c = Replace-One $c 'NTSTATUS\s*\r?\nSetFeature\(.*?\r?\nNTSTATUS\s*\r?\nGetInputReport\(' $setFeature 'SetFeature'

$getInput = @'
NTSTATUS
GetInputReport(
    _In_  PQUEUE_CONTEXT QueueContext,
    _In_  WDFREQUEST Request
    )
{
    NTSTATUS status;
    HID_XFER_PACKET packet;
    status = RequestGetHidXferPacket_ToReadFromDevice(Request, &packet);
    if (!NT_SUCCESS(status)) return status;

    if (packet.reportId == MOTION_REPORT_ID) {
        SPB_MOTION_REPORT r;
        r.ReportId = MOTION_REPORT_ID;
        r.Tx = QueueContext->DeviceContext->State.Tx;
        r.Ty = QueueContext->DeviceContext->State.Ty;
        r.Tz = QueueContext->DeviceContext->State.Tz;
        r.Rx = QueueContext->DeviceContext->State.Rx;
        r.Ry = QueueContext->DeviceContext->State.Ry;
        r.Rz = QueueContext->DeviceContext->State.Rz;
        if (packet.reportBufferLen < sizeof(r)) return STATUS_INVALID_BUFFER_SIZE;
        RtlCopyMemory(packet.reportBuffer, &r, sizeof(r));
        WdfRequestSetInformation(Request, sizeof(r));
        return STATUS_SUCCESS;
    }

    if (packet.reportId == BUTTON_REPORT_ID) {
        SPB_BUTTON_REPORT r;
        r.ReportId = BUTTON_REPORT_ID;
        r.Buttons = QueueContext->DeviceContext->State.Buttons;
        if (packet.reportBufferLen < sizeof(r)) return STATUS_INVALID_BUFFER_SIZE;
        RtlCopyMemory(packet.reportBuffer, &r, sizeof(r));
        WdfRequestSetInformation(Request, sizeof(r));
        return STATUS_SUCCESS;
    }

    return STATUS_INVALID_PARAMETER;
}

NTSTATUS
SetOutputReport(
'@
$c = Replace-One $c 'NTSTATUS\s*\r?\nGetInputReport\(.*?\r?\nNTSTATUS\s*\r?\nSetOutputReport\(' $getInput 'GetInputReport'

$setOutput = @'
NTSTATUS
SetOutputReport(
    _In_  PQUEUE_CONTEXT QueueContext,
    _In_  WDFREQUEST Request
    )
{
    NTSTATUS status;
    HID_XFER_PACKET packet;
    status = RequestGetHidXferPacket_ToWriteToDevice(Request, &packet);
    if (!NT_SUCCESS(status)) return status;
    if (packet.reportId != LED_REPORT_ID || packet.reportBufferLen < sizeof(SPB_LED_REPORT))
        return STATUS_INVALID_PARAMETER;

    PSPB_LED_REPORT led = (PSPB_LED_REPORT)packet.reportBuffer;
    QueueContext->DeviceContext->LedState = led->LedBits;
    WdfRequestSetInformation(Request, sizeof(SPB_LED_REPORT));
    return STATUS_SUCCESS;
}

NTSTATUS
GetStringId(
'@
$c = Replace-One $c 'NTSTATUS\s*\r?\nSetOutputReport\(.*?\r?\nNTSTATUS\s*\r?\nGetStringId\(' $setOutput 'SetOutputReport'

# 8 ms HID polling service. Start quickly instead of the sample's 1 second.
$c = $c -replace 'ULONG\s+timerPeriodInSeconds\s*=\s*5;', 'ULONG timerPeriodInMilliseconds = 8;'
$c = $c -replace 'timerPeriodInSeconds\s*\*\s*1000', 'timerPeriodInMilliseconds'
$c = $c -replace 'WDF_REL_TIMEOUT_IN_SEC\(1\)', 'WDF_REL_TIMEOUT_IN_MS(8)'

$timer = @'
void
EvtTimerFunc(
    _In_  WDFTIMER Timer
    )
{
    NTSTATUS status;
    WDFQUEUE queue;
    PMANUAL_QUEUE_CONTEXT queueContext;
    WDFREQUEST request;

    queue = (WDFQUEUE)WdfTimerGetParentObject(Timer);
    queueContext = GetManualQueueContext(queue);
    status = WdfIoQueueRetrieveNextRequest(queueContext->Queue, &request);
    if (!NT_SUCCESS(status)) return;

    if (queueContext->DeviceContext->State.Buttons != queueContext->DeviceContext->LastSentButtons) {
        SPB_BUTTON_REPORT r;
        r.ReportId = BUTTON_REPORT_ID;
        r.Buttons = queueContext->DeviceContext->State.Buttons;
        status = RequestCopyFromBuffer(request, &r, sizeof(r));
        if (NT_SUCCESS(status)) {
            queueContext->DeviceContext->LastSentButtons = queueContext->DeviceContext->State.Buttons;
        }
    } else {
        SPB_MOTION_REPORT r;
        r.ReportId = MOTION_REPORT_ID;
        r.Tx = queueContext->DeviceContext->State.Tx;
        r.Ty = queueContext->DeviceContext->State.Ty;
        r.Tz = queueContext->DeviceContext->State.Tz;
        r.Rx = queueContext->DeviceContext->State.Rx;
        r.Ry = queueContext->DeviceContext->State.Ry;
        r.Rz = queueContext->DeviceContext->State.Rz;
        status = RequestCopyFromBuffer(request, &r, sizeof(r));
    }
    WdfRequestComplete(request, status);
}

NTSTATUS
CheckRegistryForDescriptor(
'@
$c = Replace-One $c 'void\s*\r?\nEvtTimerFunc\(.*?\r?\nNTSTATUS\s*\r?\nCheckRegistryForDescriptor\(' $timer 'EvtTimerFunc'

Set-Content $cPath $c -Encoding ascii

$inf = Get-Content $infPath -Raw
$inf = $inf -replace 'root\\VhidminiUm', 'root\SpacePilotBridgeVhid'
$inf = $inf -replace 'ProviderString\s*="[^"]*"', 'ProviderString     ="SpacePilotBridge"'
$inf = $inf -replace 'ManufacturerString\s*="[^"]*"', 'ManufacturerString ="SpacePilotBridge"'
$inf = $inf -replace 'DeviceDesc\s*="[^"]*"', 'DeviceDesc         ="SpacePilotBridge Virtual SpaceMouse"'
Set-Content $infPath $inf -Encoding ascii

Write-Host "Virtual driver sample prepared at: $sample" -ForegroundColor Green
Write-Host 'Compatibility identity: 256F:C631 (private interoperability use only).' -ForegroundColor Yellow
