using SpacePilotBridge.Core;

namespace SpacePilotBridge.App;

/// <summary>
/// Drives the single binary LED in the original SpacePilot. There is no native
/// brightness register; "Breathe" is therefore a deliberately low-rate temporal
/// pulse pattern rather than true analogue dimming.
/// </summary>
internal sealed class LedController : IDisposable
{
    private readonly SpacePilotHub _hub;
    private readonly SpacePilotLed _led = new();
    private bool? _lastDesired;
    private string _lastPattern = string.Empty;
    private long _lastReconnectAttempt;

    public LedController(SpacePilotHub hub) => _hub = hub;

    public void Tick(string? pattern, bool screensaverActive)
    {
        string p = string.IsNullOrWhiteSpace(pattern) ? "On" : pattern.Trim();
        long ms = Environment.TickCount64;
        bool desired = Compute(p, ms, screensaverActive);

        if (!string.Equals(_lastPattern, p, StringComparison.OrdinalIgnoreCase))
        {
            _lastPattern = p;
            _lastDesired = null;
        }

        if (_lastDesired == desired && _led.IsConnected) return;

        // Do not hammer enumeration when the device is unplugged.
        if (!_led.IsConnected && ms - _lastReconnectAttempt < 750) return;
        if (!_led.IsConnected) _lastReconnectAttempt = ms;

        if (_led.Set(desired)) _lastDesired = desired;
    }

    private bool Compute(string pattern, long ms, bool screensaverActive)
    {
        switch (pattern.ToLowerInvariant())
        {
            case "off": return false;
            case "activity":
                var s = _hub.State;
                double peak = new[] { Math.Abs(s.Tx), Math.Abs(s.Ty), Math.Abs(s.Tz), Math.Abs(s.Rx), Math.Abs(s.Ry), Math.Abs(s.Rz) }.Max();
                return peak > 0.035 || s.Buttons.Count > 0;
            case "slow blink": return (ms % 1200) < 600;
            case "fast blink": return (ms % 300) < 150;
            case "heartbeat":
                long hb = ms % 1600;
                return hb < 90 || (hb >= 190 && hb < 280);
            case "breathe":
                // Binary-only hardware: approximate a smooth pulse with a 10 Hz
                // temporal density pattern. Kept intentionally slow to avoid USB spam.
                double phase = (ms % 2600) / 2600.0 * Math.PI * 2.0;
                double level = (Math.Sin(phase - Math.PI / 2.0) + 1.0) * 0.5; // 0..1
                int slot = (int)((ms / 100) % 10);
                return slot < (int)Math.Round(level * 9.0);
            case "screensaver pulse":
                if (!screensaverActive) return true;
                long sp = ms % 1400;
                return sp < 180 || (sp >= 300 && sp < 440);
            case "on":
            default: return true;
        }
    }

    public void Reset()
    {
        _led.Disconnect();
        _lastDesired = null;
    }

    public void Dispose()
    {
        try { _led.Set(false); } catch { }
        _led.Dispose();
    }
}
