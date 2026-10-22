using Microsoft.Win32.SafeHandles;

namespace SpacePilotBridge.Core;

/// <summary>
/// Shared HID output-report writer. The original SpacePilot accepts report 4
/// as a normal HID Output report; bit 0 controls the device LED.
/// </summary>
public sealed class HidOutputDevice : IDisposable
{
    private SafeFileHandle? _handle;
    public ushort VendorId { get; }
    public ushort ProductId { get; }
    public ushort OutputReportLength { get; }
    public string Path { get; }

    private HidOutputDevice(SafeFileHandle handle, DeviceInfo info)
    {
        _handle = handle;
        VendorId = info.VendorId;
        ProductId = info.ProductId;
        OutputReportLength = info.OutputReportLength;
        Path = info.Path;
    }

    public static HidOutputDevice? TryOpen(ushort vendorId, ushort productId, int minimumOutputReportLength = 2)
    {
        IEnumerable<DeviceInfo> devices;
        try { devices = HidEnumerator.Find(vendorId, productId).ToArray(); }
        catch { return null; }

        foreach (var info in devices.OrderByDescending(d => d.OutputReportLength))
        {
            if (info.OutputReportLength < minimumOutputReportLength) continue;

            var handle = Native.CreateFile(info.Path,
                Native.GENERIC_READ | Native.GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                0, Native.OPEN_EXISTING, 0, 0);

            if (!handle.IsInvalid)
                return new HidOutputDevice(handle, info);

            handle.Dispose();
        }

        return null;
    }

    public bool WriteOutput(ReadOnlySpan<byte> report)
    {
        SafeFileHandle? handle = Volatile.Read(ref _handle);
        if (handle is null || handle.IsInvalid || handle.IsClosed) return false;
        int length = OutputReportLength;
        if (length <= 0 || report.Length > length) return false;

        var buffer = new byte[length];
        report.CopyTo(buffer);
        if (Native.WriteFile(handle, buffer, (uint)buffer.Length, out uint written, 0)
            && written == (uint)buffer.Length) return true;

        // Some HID stacks prefer the explicit SET_REPORT(Output) helper.
        return Native.HidD_SetOutputReport(handle, buffer, buffer.Length);
    }

    public void Abort()
    {
        SafeFileHandle? handle = Interlocked.Exchange(ref _handle, null);
        if (handle is null) return;
        try { Native.CancelIoEx(handle, 0); } catch { }
        try { handle.Dispose(); } catch { }
    }

    public void Dispose() => Abort();
}
