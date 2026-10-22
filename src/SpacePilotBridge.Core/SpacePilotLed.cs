namespace SpacePilotBridge.Core;

/// <summary>
/// LED control for the original SpacePilot (046d:c625).
/// HID output report 4 contains one data byte; bit 0 is the LED state.
/// The hardware exposes no brightness value, so richer effects are created by
/// timing ordinary on/off reports in the application layer.
/// </summary>
public sealed class SpacePilotLed : IDisposable
{
    private HidOutputDevice? _device;
    private bool? _lastState;

    public bool IsConnected => _device is not null;

    public bool EnsureConnected()
    {
        if (_device is not null) return true;
        _device = HidOutputDevice.TryOpen(SpacePilotHub.VendorId, SpacePilotHub.ProductId, 2);
        _lastState = null;
        return _device is not null;
    }

    public bool Set(bool on)
    {
        if (_lastState == on && _device is not null) return true;
        if (!EnsureConnected() || _device is null) return false;

        // HID WriteFile framing includes the report id as byte 0.
        bool ok = _device.WriteOutput(new byte[] { 0x04, on ? (byte)0x01 : (byte)0x00 });
        if (ok)
        {
            _lastState = on;
            return true;
        }

        Disconnect();
        return false;
    }

    public void Disconnect()
    {
        _device?.Dispose();
        _device = null;
        _lastState = null;
    }

    public void Dispose() => Disconnect();
}
