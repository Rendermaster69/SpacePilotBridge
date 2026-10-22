using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SpacePilotBridge.Core;

internal static class HidEnumerator
{
    public static IReadOnlyList<DeviceInfo> Find(ushort vid, ushort pid)
    {
        var result = new List<DeviceInfo>();
        Guid hidGuid = Guid.Empty;
        Native.HidD_GetHidGuid(ref hidGuid);
        nint set = Native.SetupDiGetClassDevsW(ref hidGuid, 0, 0,
            Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
        if (set == new nint(-1)) return result;

        try
        {
            for (uint i = 0; ; i++)
            {
                var data = new Native.SpDeviceInterfaceData
                {
                    cbSize = Marshal.SizeOf<Native.SpDeviceInterfaceData>()
                };
                if (!Native.SetupDiEnumDeviceInterfaces(set, 0, ref hidGuid, i, ref data)) break;

                int required = 0;
                Native.SetupDiGetDeviceInterfaceDetailW(set, ref data, 0, 0, ref required, 0);
                if (required <= 0) continue;

                nint buffer = Marshal.AllocHGlobal(required);
                try
                {
                    Marshal.WriteInt32(buffer, Environment.Is64BitProcess ? 8 : 6);
                    if (!Native.SetupDiGetDeviceInterfaceDetailW(set, ref data, buffer, required, ref required, 0)) continue;
                    string? path = Marshal.PtrToStringUni(buffer + 4);
                    if (string.IsNullOrWhiteSpace(path)) continue;

                    using SafeFileHandle h = Native.CreateFile(path, 0,
                        Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, 0,
                        Native.OPEN_EXISTING, 0, 0);
                    if (h.IsInvalid) continue;

                    var a = new Native.HiddAttributes { Size = Marshal.SizeOf<Native.HiddAttributes>() };
                    if (!Native.HidD_GetAttributes(h, ref a)) continue;
                    if (a.VendorID != vid || a.ProductID != pid) continue;

                    ushort input = 0, output = 0, feature = 0;
                    if (Native.HidD_GetPreparsedData(h, out nint preparsed))
                    {
                        try
                        {
                            var caps = new Native.HidpCaps { Reserved = new ushort[17] };
                            Native.HidP_GetCaps(preparsed, ref caps);
                            input = caps.InputReportByteLength;
                            output = caps.OutputReportByteLength;
                            feature = caps.FeatureReportByteLength;
                        }
                        finally { Native.HidD_FreePreparsedData(preparsed); }
                    }

                    result.Add(new DeviceInfo(path, a.VendorID, a.ProductID, a.VersionNumber, input, output, feature));
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        finally { Native.SetupDiDestroyDeviceInfoList(set); }

        return result;
    }
}
