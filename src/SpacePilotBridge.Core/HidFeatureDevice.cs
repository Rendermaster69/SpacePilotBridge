using Microsoft.Win32.SafeHandles;

namespace SpacePilotBridge.Core;

/// <summary>
/// Small wrapper for sending HID feature reports to a specific HID collection.
/// Opens a dedicated shared handle so it can coexist with the streaming reader.
/// </summary>
public sealed class HidFeatureDevice : IDisposable
{
    private readonly SafeFileHandle _handle;
    public ushort VendorId { get; }
    public ushort ProductId { get; }
    public ushort FeatureReportLength { get; }
    public string Path { get; }

    private HidFeatureDevice(SafeFileHandle handle, DeviceInfo info)
    {
        _handle = handle;
        VendorId = info.VendorId;
        ProductId = info.ProductId;
        FeatureReportLength = info.FeatureReportLength;
        Path = info.Path;
    }

    public static HidFeatureDevice? TryOpen(ushort vendorId, ushort productId, int minimumFeatureReportLength = 1)
    {
        IEnumerable<DeviceInfo> devices;
        try { devices = HidEnumerator.Find(vendorId, productId).ToArray(); }
        catch { return null; }

        foreach (var info in devices.OrderByDescending(d => d.FeatureReportLength))
        {
            if (info.FeatureReportLength < minimumFeatureReportLength) continue;

            var handle = Native.CreateFile(info.Path,
                Native.GENERIC_READ | Native.GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                0, Native.OPEN_EXISTING, 0, 0);

            if (!handle.IsInvalid)
                return new HidFeatureDevice(handle, info);

            handle.Dispose();

            // Some HID collections accept feature requests with access=0.
            handle = Native.CreateFile(info.Path, 0,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                0, Native.OPEN_EXISTING, 0, 0);
            if (!handle.IsInvalid)
                return new HidFeatureDevice(handle, info);
            handle.Dispose();
        }

        return null;
    }

    public bool WriteFeature(ReadOnlySpan<byte> report)
    {
        if (_handle.IsInvalid || _handle.IsClosed) return false;
        int length = FeatureReportLength;
        if (length <= 0 || report.Length > length) return false;

        var buffer = new byte[length];
        report.CopyTo(buffer);
        return Native.HidD_SetFeature(_handle, buffer, buffer.Length);
    }

    public void Dispose() => _handle.Dispose();
}
