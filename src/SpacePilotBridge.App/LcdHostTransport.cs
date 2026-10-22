using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using SpacePilotBridge.Core;
using SpacePilotBridge.Lcd;

namespace SpacePilotBridge.App;

/// <summary>
/// Runs all physical SpacePilot LCD I/O in a sacrificial child process of the
/// same executable. HidD_SetFeature is synchronous and can occasionally wedge
/// indefinitely in the Windows HID stack. A blocked thread inside the main app
/// cannot be recovered reliably; a blocked helper process can always be killed
/// and recreated without affecting the virtual SpaceMouse or settings UI.
/// </summary>
internal sealed class LcdHostClient : IDisposable
{
    private readonly object _lifecycleGate = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private NamedPipeServerStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Process? _process;
    private bool _disposed;
    private volatile bool _lastConnected;
    private int _generation;

    public int FullRefreshEveryFrames { get; set; } = 12;
    public int ReportDelayMilliseconds { get; set; } = 3;
    public bool ForceFullAnimationFrames { get; set; } = true;
    public double LastRoundTripMilliseconds { get; private set; }
    public bool IsConnected => _lastConnected;
    public int HostGeneration => Volatile.Read(ref _generation);

    public bool ShowStatus(
        string activeApp,
        bool sourceConnected,
        bool virtualConnected,
        SpacePilotState state,
        double sensitivity,
        bool dominant,
        bool rotationLocked,
        string profileName,
        double latencyMs,
        string ledPattern,
        bool gamepadEnabled,
        string nowPlaying,
        IReadOnlyList<string>? widgets,
        CancellationToken cancellationToken = default)
    {
        return Send(new LcdHostCommand
        {
            Type = "status",
            FullRefreshEveryFrames = FullRefreshEveryFrames,
            ReportDelayMilliseconds = ReportDelayMilliseconds,
            ForceFullAnimationFrames = ForceFullAnimationFrames,
            ActiveApp = activeApp,
            SourceConnected = sourceConnected,
            VirtualConnected = virtualConnected,
            State = LcdStateDto.FromState(state),
            Sensitivity = sensitivity,
            Dominant = dominant,
            RotationLocked = rotationLocked,
            ProfileName = profileName,
            LatencyMs = latencyMs,
            LedPattern = ledPattern,
            GamepadEnabled = gamepadEnabled,
            NowPlaying = nowPlaying,
            Widgets = widgets?.ToList() ?? new List<string>()
        }, cancellationToken);
    }

    public bool ShowAnimation(string animation, int frame, string ditherMode = "Threshold", CancellationToken cancellationToken = default)
    {
        return Send(new LcdHostCommand
        {
            Type = "animation",
            FullRefreshEveryFrames = FullRefreshEveryFrames,
            ReportDelayMilliseconds = ReportDelayMilliseconds,
            ForceFullAnimationFrames = ForceFullAnimationFrames,
            Animation = animation,
            Frame = frame,
            DitherMode = ditherMode
        }, cancellationToken);
    }

    public bool ShowMessage(string title, string line2 = "", string line3 = "", CancellationToken cancellationToken = default)
    {
        return Send(new LcdHostCommand
        {
            Type = "message",
            FullRefreshEveryFrames = FullRefreshEveryFrames,
            ReportDelayMilliseconds = ReportDelayMilliseconds,
            ForceFullAnimationFrames = ForceFullAnimationFrames,
            Title = title,
            Line2 = line2,
            Line3 = line3
        }, cancellationToken);
    }

    public void AbortCurrentIo() => HardReset();

    private bool Send(LcdHostCommand command, CancellationToken cancellationToken)
    {
        if (_disposed) return false;
        if (!_sendGate.Wait(0)) return false;
        try
        {
            if (!EnsureHost()) return false;
            StreamWriter? writer = _writer;
            StreamReader? reader = _reader;
            if (writer is null || reader is null) return false;

            var stopwatch = Stopwatch.StartNew();
            string json = JsonSerializer.Serialize(command, LcdHostJson.Options);
            writer.WriteLine(json);
            writer.Flush();

            // This timeout is deliberately shorter than the outer UI watchdog.
            // If the child is stuck in a native HID request, kill the entire child.
            string? responseLine;
            try
            {
                responseLine = reader.ReadLineAsync()
                    .WaitAsync(TimeSpan.FromMilliseconds(command.Type?.Equals("animation", StringComparison.OrdinalIgnoreCase) == true ? 8000 : 4000), cancellationToken)
                    .GetAwaiter().GetResult();
            }
            catch (TimeoutException)
            {
                HardReset();
                return false;
            }
            catch (OperationCanceledException)
            {
                HardReset();
                return false;
            }
            catch
            {
                HardReset();
                return false;
            }

            if (string.IsNullOrWhiteSpace(responseLine))
            {
                HardReset();
                return false;
            }

            LcdHostResponse? response = JsonSerializer.Deserialize<LcdHostResponse>(responseLine, LcdHostJson.Options);
            if (response is null)
            {
                HardReset();
                return false;
            }
            stopwatch.Stop();
            LastRoundTripMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            _lastConnected = response.Connected;
            return response.Ok;
        }
        catch
        {
            HardReset();
            return false;
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private bool EnsureHost()
    {
        lock (_lifecycleGate)
        {
            if (_disposed) return false;
            if (_process is { HasExited: false } && _pipe?.IsConnected == true && _reader is not null && _writer is not null)
                return true;

            TearDownUnsafe(kill: true);

            string exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return false;

            string pipeName = $"SpacePilotBridge.Lcd.{Environment.ProcessId}.{Guid.NewGuid():N}";
            var pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                4096,
                4096);

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            psi.ArgumentList.Add("--lcd-host");
            psi.ArgumentList.Add(pipeName);

            Process? child = Process.Start(psi);
            if (child is null)
            {
                pipe.Dispose();
                return false;
            }

            try
            {
                pipe.WaitForConnectionAsync()
                    .WaitAsync(TimeSpan.FromSeconds(2))
                    .GetAwaiter().GetResult();
            }
            catch
            {
                try { if (!child.HasExited) child.Kill(entireProcessTree: true); } catch { }
                child.Dispose();
                pipe.Dispose();
                return false;
            }

            _process = child;
            _pipe = pipe;
            _reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
            _writer = new StreamWriter(pipe, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
            _lastConnected = false;
            Interlocked.Increment(ref _generation);
            return true;
        }
    }

    private void HardReset()
    {
        lock (_lifecycleGate)
        {
            TearDownUnsafe(kill: true);
            _lastConnected = false;
            Interlocked.Increment(ref _generation);
        }
    }

    private void TearDownUnsafe(bool kill)
    {
        // Kill the sacrificial LCD host FIRST. Disposing a pipe whose peer is
        // wedged in a native HID call can itself block on some Windows builds.
        Process? process = _process;
        _process = null;
        if (process is not null)
        {
            try { if (kill && !process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { process.WaitForExit(500); } catch { }
        }

        try { _pipe?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }
        try { _writer?.Dispose(); } catch { }
        _writer = null;
        _reader = null;
        _pipe = null;

        try { process?.Dispose(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        HardReset();
        _sendGate.Dispose();
    }
}

internal static class LcdHostServer
{
    public static int Run(string pipeName)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.None);
            pipe.Connect(3000);
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
            using var lcd = new SpacePilotLcd();

            while (pipe.IsConnected)
            {
                string? line = reader.ReadLine();
                if (line is null) break;
                bool ok = false;
                string? error = null;
                try
                {
                    LcdHostCommand? command = JsonSerializer.Deserialize<LcdHostCommand>(line, LcdHostJson.Options);
                    if (command is null) throw new InvalidDataException("Invalid LCD command.");
                    lcd.FullRefreshEveryFrames = Math.Clamp(command.FullRefreshEveryFrames, 1, 120);
                    lcd.ReportDelayMilliseconds = Math.Clamp(command.ReportDelayMilliseconds, 0, 12);
                    lcd.ForceFullAnimationFrames = command.ForceFullAnimationFrames;
                    ok = Execute(lcd, command);
                }
                catch (Exception ex)
                {
                    error = $"{ex.GetType().Name}: {ex.Message}";
                }

                writer.WriteLine(JsonSerializer.Serialize(new LcdHostResponse
                {
                    Ok = ok,
                    Connected = lcd.IsConnected,
                    Error = error
                }, LcdHostJson.Options));
            }
            return 0;
        }
        catch
        {
            return 2;
        }
    }

    private static bool Execute(SpacePilotLcd lcd, LcdHostCommand c)
    {
        return c.Type?.ToLowerInvariant() switch
        {
            "status" => lcd.ShowStatus(
                c.ActiveApp ?? string.Empty,
                c.SourceConnected,
                c.VirtualConnected,
                c.State?.ToState() ?? SpacePilotState.Zero,
                c.Sensitivity,
                c.Dominant,
                c.RotationLocked,
                c.ProfileName ?? "Global",
                c.LatencyMs,
                c.LedPattern ?? "On",
                c.GamepadEnabled,
                c.NowPlaying ?? "SPOTIFY -",
                c.Widgets),
            "animation" => lcd.ShowAnimation(c.Animation ?? "Starfield", c.Frame, c.DitherMode ?? "Threshold"),
            "message" => lcd.ShowMessage(c.Title ?? string.Empty, c.Line2 ?? string.Empty, c.Line3 ?? string.Empty),
            _ => false
        };
    }
}

internal sealed class LcdHostCommand
{
    public string? Type { get; set; }
    public int FullRefreshEveryFrames { get; set; } = 12;
    public int ReportDelayMilliseconds { get; set; } = 3;
    public bool ForceFullAnimationFrames { get; set; } = true;
    public string? ActiveApp { get; set; }
    public bool SourceConnected { get; set; }
    public bool VirtualConnected { get; set; }
    public LcdStateDto? State { get; set; }
    public double Sensitivity { get; set; }
    public bool Dominant { get; set; }
    public bool RotationLocked { get; set; }
    public string? ProfileName { get; set; }
    public double LatencyMs { get; set; }
    public string? LedPattern { get; set; }
    public bool GamepadEnabled { get; set; }
    public string? NowPlaying { get; set; }
    public List<string> Widgets { get; set; } = new();
    public string? Animation { get; set; }
    public int Frame { get; set; }
    public string? DitherMode { get; set; }
    public string? Title { get; set; }
    public string? Line2 { get; set; }
    public string? Line3 { get; set; }
}

internal sealed class LcdHostResponse
{
    public bool Ok { get; set; }
    public bool Connected { get; set; }
    public string? Error { get; set; }
}

internal sealed class LcdStateDto
{
    public double Tx { get; set; }
    public double Ty { get; set; }
    public double Tz { get; set; }
    public double Rx { get; set; }
    public double Ry { get; set; }
    public double Rz { get; set; }
    public List<int> Buttons { get; set; } = new();
    public DateTime TimestampUtc { get; set; }

    public static LcdStateDto FromState(SpacePilotState s) => new()
    {
        Tx = s.Tx, Ty = s.Ty, Tz = s.Tz,
        Rx = s.Rx, Ry = s.Ry, Rz = s.Rz,
        Buttons = s.Buttons.ToList(),
        TimestampUtc = s.TimestampUtc
    };

    public SpacePilotState ToState() => new(Tx, Ty, Tz, Rx, Ry, Rz, Buttons, TimestampUtc);
}

internal static class LcdHostJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
