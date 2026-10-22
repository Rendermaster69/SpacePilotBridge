using System.Buffers.Binary;
using SpacePilotBridge.Core;

namespace SpacePilotBridge.Virtual;

public sealed record VirtualWriterMetrics(
    double LastLatencyMs,
    double AverageLatencyMs,
    double PeakLatencyMs,
    double ReportsPerSecond,
    long ReportsSent,
    bool GamepadSupported);

/// <summary>
/// Streams the physical SpacePilot state into the private control collection
/// exposed by the SpacePilotBridge virtual HID minidriver.
/// v0.7 remains backward-compatible with the v0.6 18-byte control report; an
/// optional v0.7 driver exposes a longer report that also enables a generic
/// gamepad top-level collection.
/// </summary>
public sealed class VirtualSpaceMouseWriter : IDisposable
{
    public const ushort VendorId = 0x256F;
    public const ushort ProductId = 0xC631;
    public const byte ControlReportId = 0x7F;
    public const byte SetStateControlCode = 0x53;

    private readonly SpacePilotHub _hub;
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;
    private readonly object _metricsGate = new();
    private HidFeatureDevice? _device;
    private bool _disposed;

    private long _reportsSent;
    private long _reportsWindow;
    private DateTime _windowStart = DateTime.UtcNow;
    private double _lastLatencyMs;
    private double _avgLatencyMs;
    private double _peakLatencyMs;
    private double _reportsPerSecond;
    private DateTime _lastMeasuredTimestamp = DateTime.MinValue;

    public bool IsConnected => _device is not null;
    public bool GamepadSupported => _device?.FeatureReportLength >= 23;
    public event EventHandler? ConnectionChanged;

    public Func<SpacePilotState, SpacePilotState>? StateTransform { get; set; }
    public Func<IReadOnlyCollection<int>, uint>? ButtonMapper { get; set; }
    public Func<bool>? GamepadEnabledProvider { get; set; }

    public VirtualSpaceMouseWriter(SpacePilotHub hub)
    {
        _hub = hub;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "SpacePilot virtual SpaceMouse writer"
        };
        _thread.Start();
    }

    public VirtualWriterMetrics GetMetrics()
    {
        lock (_metricsGate)
            return new VirtualWriterMetrics(_lastLatencyMs, _avgLatencyMs, _peakLatencyMs, _reportsPerSecond, _reportsSent, GamepadSupported);
    }

    private void Run()
    {
        bool previous = false;
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                _device ??= HidFeatureDevice.TryOpen(VendorId, ProductId, minimumFeatureReportLength: 18);
                bool now = _device is not null;
                if (now != previous)
                {
                    previous = now;
                    ConnectionChanged?.Invoke(this, EventArgs.Empty);
                }

                if (_device is null)
                {
                    _cts.Token.WaitHandle.WaitOne(500);
                    continue;
                }

                SpacePilotState rawState = _hub.IsConnected ? _hub.State : SpacePilotState.Zero;
                SpacePilotState state = StateTransform is null ? rawState : StateTransform(rawState);
                bool extended = _device.FeatureReportLength >= 23;
                bool gamepadEnabled = extended && (GamepadEnabledProvider?.Invoke() ?? false);
                byte[] report = BuildStateReport(state, rawState.Buttons, ButtonMapper, extended, gamepadEnabled);
                if (!_device.WriteFeature(report))
                {
                    DropVirtualDevice(ref previous);
                    _cts.Token.WaitHandle.WaitOne(250);
                    continue;
                }

                UpdateMetrics(rawState);
                _cts.Token.WaitHandle.WaitOne(8);
            }
            catch
            {
                DropVirtualDevice(ref previous);
                _cts.Token.WaitHandle.WaitOne(500);
            }
        }
    }

    private void UpdateMetrics(SpacePilotState state)
    {
        double latency = Math.Max(0, (DateTime.UtcNow - state.TimestampUtc).TotalMilliseconds);
        lock (_metricsGate)
        {
            _reportsSent++;
            _reportsWindow++;
            if (state.TimestampUtc != _lastMeasuredTimestamp && latency < 1000)
            {
                _lastMeasuredTimestamp = state.TimestampUtc;
                _lastLatencyMs = latency;
                _avgLatencyMs = _avgLatencyMs <= 0 ? latency : (_avgLatencyMs * 0.94 + latency * 0.06);
                _peakLatencyMs = Math.Max(_peakLatencyMs * 0.999, latency);
            }
            double seconds = (DateTime.UtcNow - _windowStart).TotalSeconds;
            if (seconds >= 1.0)
            {
                _reportsPerSecond = _reportsWindow / seconds;
                _reportsWindow = 0;
                _windowStart = DateTime.UtcNow;
            }
        }
    }

    private void DropVirtualDevice(ref bool previous)
    {
        _device?.Dispose();
        _device = null;
        if (previous)
        {
            previous = false;
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal static byte[] BuildStateReport(
        SpacePilotState state,
        IReadOnlyCollection<int> rawButtons,
        Func<IReadOnlyCollection<int>, uint>? buttonMapper,
        bool extended,
        bool gamepadEnabled)
    {
        var bytes = new byte[extended ? 23 : 18];
        bytes[0] = ControlReportId;
        bytes[1] = SetStateControlCode;
        WriteAxis(bytes.AsSpan(2, 2), state.Tx);
        WriteAxis(bytes.AsSpan(4, 2), state.Ty);
        WriteAxis(bytes.AsSpan(6, 2), state.Tz);
        WriteAxis(bytes.AsSpan(8, 2), state.Rx);
        WriteAxis(bytes.AsSpan(10, 2), state.Ry);
        WriteAxis(bytes.AsSpan(12, 2), state.Rz);
        uint buttonBits = buttonMapper?.Invoke(state.Buttons) ?? MapButtons(state.Buttons);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14, 4), buttonBits);

        if (extended)
        {
            uint rawBits = 0;
            foreach (int raw in rawButtons)
                if (raw is >= 0 and < 32) rawBits |= 1u << raw;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(18, 4), rawBits);
            bytes[22] = gamepadEnabled ? (byte)1 : (byte)0;
        }
        return bytes;
    }

    private static void WriteAxis(Span<byte> destination, double normalized)
    {
        short value = (short)Math.Clamp((int)Math.Round(normalized * 350.0), -350, 350);
        BinaryPrimitives.WriteInt16LittleEndian(destination, value);
    }

    private static uint MapButtons(IReadOnlyCollection<int> sourceButtons)
    {
        uint result = 0;
        foreach (int raw in sourceButtons)
        {
            int? target = SpacePilotButtons.GetDefaultVirtualBit(raw);
            if (target is >= 0 and < 32) result |= 1u << target.Value;
        }
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _thread.Join(1000);
        _device?.Dispose();
        _cts.Dispose();
    }
}
