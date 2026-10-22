/* SpacePilotBridge additions applied to Microsoft's vhidmini2 sample.
 * This header is shared by the UMDF HID minidriver and its control protocol.
 */
#ifndef __VHIDMINI_COMMON_H__
#define __VHIDMINI_COMMON_H__

#define HIDMINI_CONTROL_CODE_SET_ATTRIBUTES 0x00
#define HIDMINI_CONTROL_CODE_SET_STATE      0x53
#define HIDMINI_CONTROL_CODE_DUMMY1         0x01
#define HIDMINI_CONTROL_CODE_DUMMY2         0x02

#define CONTROL_COLLECTION_REPORT_ID 0x7F
#define MOTION_REPORT_ID             0x01
#define BUTTON_REPORT_ID             0x03
#define LED_REPORT_ID                0x04

#define MAXIMUM_STRING_LENGTH (126 * sizeof(WCHAR))
#define VHIDMINI_MANUFACTURER_STRING L"3Dconnexion"
#define VHIDMINI_PRODUCT_STRING      L"SpaceMouse Pro Wireless"
#define VHIDMINI_SERIAL_NUMBER_STRING L"SPB-C625"
#define VHIDMINI_DEVICE_STRING       L"SpacePilotBridge virtual SpaceMouse"
#define VHIDMINI_DEVICE_STRING_INDEX 5

#include <pshpack1.h>

typedef struct _MY_DEVICE_ATTRIBUTES {
    USHORT VendorID;
    USHORT ProductID;
    USHORT VersionNumber;
} MY_DEVICE_ATTRIBUTES, *PMY_DEVICE_ATTRIBUTES;

typedef struct _SPB_DEVICE_STATE {
    SHORT Tx;
    SHORT Ty;
    SHORT Tz;
    SHORT Rx;
    SHORT Ry;
    SHORT Rz;
    ULONG Buttons;
} SPB_DEVICE_STATE, *PSPB_DEVICE_STATE;

typedef struct _HIDMINI_CONTROL_INFO {
    UCHAR ReportId;
    UCHAR ControlCode;
    union {
        MY_DEVICE_ATTRIBUTES Attributes;
        SPB_DEVICE_STATE State;
        struct { ULONG Dummy1; ULONG Dummy2; } Dummy;
    } u;
} HIDMINI_CONTROL_INFO, *PHIDMINI_CONTROL_INFO;

typedef struct _SPB_MOTION_REPORT {
    UCHAR ReportId;
    SHORT Tx;
    SHORT Ty;
    SHORT Tz;
    SHORT Rx;
    SHORT Ry;
    SHORT Rz;
} SPB_MOTION_REPORT, *PSPB_MOTION_REPORT;

typedef struct _SPB_BUTTON_REPORT {
    UCHAR ReportId;
    ULONG Buttons;
} SPB_BUTTON_REPORT, *PSPB_BUTTON_REPORT;

typedef struct _SPB_LED_REPORT {
    UCHAR ReportId;
    UCHAR LedBits;
} SPB_LED_REPORT, *PSPB_LED_REPORT;

/* Keep sample names available for unmodified helper code. */
typedef SPB_BUTTON_REPORT HIDMINI_INPUT_REPORT, *PHIDMINI_INPUT_REPORT;
typedef SPB_LED_REPORT HIDMINI_OUTPUT_REPORT, *PHIDMINI_OUTPUT_REPORT;

#include <poppack.h>

#define FEATURE_REPORT_SIZE_CB ((USHORT)(sizeof(HIDMINI_CONTROL_INFO) - 1))
#define INPUT_REPORT_SIZE_CB   ((USHORT)(sizeof(SPB_MOTION_REPORT) - 1))
#define OUTPUT_REPORT_SIZE_CB  ((USHORT)(sizeof(SPB_LED_REPORT) - 1))

#endif
