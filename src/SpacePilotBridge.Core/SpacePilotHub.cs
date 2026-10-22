using Microsoft.Win32.SafeHandles;

namespace SpacePilotBridge.Core;

public sealed class SpacePilotHub : IDisposable
{
    public const ushort VendorId = 0x046D;
    public const ushort ProductId = 0xC625;

    private static readonly Lazy<SpacePilotHub> _lazy = new(() => new SpacePilotHub());
    public static SpacePilotHub Instance => _lazy.Value;

    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Thread? _thread;
    private SafeFileHandle? _activeHandle;
    private int _clients;
    private bool _disposed;
    private SpacePilotState _state = SpacePilotState.Zero;

    public bool IsConnected { get; private set; }
    public SpacePilotState State { get { lock (_gate) return _state; } }

    public event EventHandler<SpacePilotState>? StateChanged;
    public event EventHandler? ConnectionChanged;

    private SpacePilotHub() { }

    public void Acquire()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _clients++;
            if (_thread is not null) return;
            _cts = new CancellationTokenSource();
            _thread = new Thread(() => Run(_cts.Token))
            {
                IsBackground = true,
                Name = "SpacePilot HID reader"
            };
            _thread.Start();
        }
    }

    public void Release()
    {
        lock (_gate)
        {
            if (_clients > 0) _clients--;
            if (_clients != 0) return;
            StopLocked();
        }
    }

    private void Run(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            DeviceInfo? info = null;
            try
            {
                info = HidEnumerator.Find(VendorId, ProductId)
                    .Where(d => d.InputReportLength >= 7)
                    .OrderByDescending(d => d.InputReportLength)
                    .FirstOrDefault();
            }
            catch { }

            if (info is null)
            {
                SetConnected(false);
                token.WaitHandle.WaitOne(800);
                continue;
            }

            using SafeFileHandle h = Native.CreateFile(info.Path, Native.GENERIC_READ,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, 0,
                Native.OPEN_EXISTING, 0, 0);
            if (h.IsInvalid)
            {
                SetConnected(false);
                token.WaitHandle.WaitOne(800);
                continue;
            }

            lock (_gate) _activeHandle = h;
            SetConnected(true);

            int reportLength = Math.Clamp(info.InputReportLength, (ushort)7, (ushort)128);
            var buffer = new byte[reportLength];
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (!Native.ReadFile(h, buffer, (uint)buffer.Length, out uint read, 0) || read == 0)
                        break;
                    Parse(buffer.AsSpan(0, checked((int)read)));
                }
            }
            catch { }
            finally
            {
                lock (_gate) if (ReferenceEquals(_activeHandle, h)) _activeHandle = null;
                SetConnected(false);
            }
        }
    }

    private void Parse(ReadOnlySpan<byte> report)
    {
        if (report.Length < 1) return;
        SpacePilotState next;
        lock (_gate)
        {
            var s = _state;
            switch (report[0])
            {
                case 0x01 when report.Length >= 7:
                    next = s with
                    {
                        // Preserve the device's native 3Dconnexion axis order.
                        // The virtual 256F:C631 target uses the same six-axis
                        // convention; 3DxWare/application profiles own any
                        // user-facing inversion/remapping.
                        Tx = Axis(report, 1),
                        Ty = Axis(report, 3),
                        Tz = Axis(report, 5),
                        TimestampUtc = DateTime.UtcNow
                    };
                    break;
                case 0x02 when report.Length >= 7:
                    next = s with
                    {
                        Rx = Axis(report, 1),
                        Ry = Axis(report, 3),
                        Rz = Axis(report, 5),
                        TimestampUtc = DateTime.UtcNow
                    };
                    break;
                case 0x03 when report.Length >= 2:
                    var buttons = new List<int>();
                    for (int b = 1; b < report.Length; b++)
                        for (int bit = 0; bit < 8; bit++)
                            if ((report[b] & (1 << bit)) != 0)
                                buttons.Add((b - 1) * 8 + bit);
                    next = s with { Buttons = buttons, TimestampUtc = DateTime.UtcNow };
                    break;
                default:
                    return;
            }
            _state = next;
        }
        StateChanged?.Invoke(this, next);
    }

    private static double Axis(ReadOnlySpan<byte> r, int offset)
    {
        short raw = unchecked((short)(r[offset] | (r[offset + 1] << 8)));
        // TDxInput is unitless. Normalize typical SpacePilot full travel to ~1.0.
        return raw / 350.0;
    }

    private void SetConnected(bool value)
    {
        bool changed;
        lock (_gate)
        {
            changed = IsConnected != value;
            IsConnected = value;
            if (!value) _state = _state with { Tx = 0, Ty = 0, Tz = 0, Rx = 0, Ry = 0, Rz = 0 };
        }
        if (changed) ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void StopLocked()
    {
        if (_cts is null) return;
        _cts.Cancel();
        try { if (_activeHandle is { IsInvalid: false, IsClosed: false }) Native.CancelIoEx(_activeHandle, 0); } catch { }
        // Do not dispose the CTS while the reader thread may still observe its token.
        _cts = null;
        _thread = null;
        _activeHandle = null;
        IsConnected = false;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SpacePilotHub));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _clients = 0;
            StopLocked();
        }
    }
}
