using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SpacePilotBridge.Core;

namespace SpacePilotBridge.Lcd;

/// <summary>
/// Crisp framebuffer driver for the original SpacePilot 240x64 monochrome LCD.
/// v0.9 supports built-in effects plus hot-loadable GIF/image/.spbsaver files.
/// </summary>
public sealed class SpacePilotLcd : IDisposable
{
    public const int Width = 240;
    public const int Height = 64;

    private HidFeatureDevice? _device;
    private readonly object _connectGate = new();
    private bool _suppressed;
    private readonly byte[] _lastPacked = new byte[Width * 8];
    private bool _hasLastPacked;
    private int _framesSinceFullRefresh;
    public int FullRefreshEveryFrames { get; set; } = 12;
    public int ReportDelayMilliseconds { get; set; } = 3;
    public bool ForceFullAnimationFrames { get; set; } = true;
    private int _ioEpoch;
    private readonly Random _random = new();
    private readonly Star[] _stars = new Star[40];
    private readonly int[] _matrixY = new int[38];
    private bool _animationInitialized;
    private readonly Dictionary<string, CachedMedia> _mediaCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _mediaGate = new();

    public bool IsConnected => _device is not null;

    public bool EnsureConnected()
    {
        if (Volatile.Read(ref _device) is not null) return true;
        lock (_connectGate)
        {
            if (_device is not null) return true;
            _device = HidFeatureDevice.TryOpen(SpacePilotHub.VendorId, SpacePilotHub.ProductId, 8);
            _suppressed = false;
            _hasLastPacked = false;
        _framesSinceFullRefresh = 0;
            return _device is not null;
        }
    }

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
        var c = new MonoCanvas(Width, Height);
        string app = PrettyAppName(activeApp);
        string ready = sourceConnected && virtualConnected ? "READY" : sourceConnected ? "VHID OFF" : "OFFLINE";

        c.DrawText(app, 2, 1, 2);
        int readyWidth = c.MeasureText(ready, 1);
        c.DrawText(ready, Width - readyWidth - 2, 3, 1);
        c.HLine(1, 17, Width - 2);

        string[] slots = (widgets ?? Array.Empty<string>()).Take(4).ToArray();
        if (slots.Length == 0) slots = new[] { "Speed", "Profile", "Latency", "Buttons" };
        int[] ys = { 20, 29, 38, 47 };
        for (int i = 0; i < slots.Length && i < ys.Length; i++)
            c.DrawText(RenderWidget(slots[i], state, sensitivity, dominant, rotationLocked, profileName, latencyMs, ledPattern, gamepadEnabled, nowPlaying), 2, ys[i], 1);

        c.HLine(1, 55, Width - 2);
        c.DrawText("SPB 0.9.2", 2, 57, 1);
        string time = DateTime.Now.ToString("HH:mm");
        c.DrawText(time, Width - c.MeasureText(time, 1) - 2, 57, 1);
        return WriteCanvas(c, cancellationToken);
    }

    private static string RenderWidget(string widget, SpacePilotState state, double sensitivity, bool dominant, bool rotationLocked, string profile, double latencyMs, string ledPattern, bool gamepadEnabled, string nowPlaying)
    {
        return (widget ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "profile" => $"PROFILE {profile.ToUpperInvariant()}",
            "latency" => $"LATENCY {latencyMs:0.0} MS",
            "buttons" => FormatButtons(state.Buttons),
            "motion" => $"T {state.Tx:+0.00;-0.00;0.00} {state.Ty:+0.00;-0.00;0.00} {state.Tz:+0.00;-0.00;0.00}",
            "clock" => $"CLOCK {DateTime.Now:HH:mm:ss}",
            "led" => $"LED {ledPattern.ToUpperInvariant()}",
            "gamepad" => $"GAMEPAD {(gamepadEnabled ? "ON" : "OFF")}",
            "spotify" or "now playing" => TruncateWidget(nowPlaying, 38),
            _ => $"SPEED {sensitivity:0.00}X  DOM {(dominant ? "ON" : "OFF")}  ROT {(rotationLocked ? "LOCK" : "FREE")}"
        };
    }

    private static string TruncateWidget(string? value, int max)
    {
        string text = string.IsNullOrWhiteSpace(value) ? "SPOTIFY -" : value.Trim();
        return text.Length <= max ? text : text[..Math.Max(1, max - 1)] + "~";
    }

    public bool ShowAnimation(string animation, int frame, string ditherMode = "Threshold", CancellationToken cancellationToken = default)
    {
        if (ForceFullAnimationFrames)
        {
            _hasLastPacked = false;
            _framesSinceFullRefresh = 0;
        }

        if (!_animationInitialized)
        {
            InitAnimations();
            _animationInitialized = true;
        }

        ScreensaverInfo info = ScreensaverLibrary.Resolve(animation);
        var c = new MonoCanvas(Width, Height);
        int f = (int)Math.Round(frame * Math.Clamp(info.Speed, 0.2, 4.0));
        string effect = info.Effect.Trim().ToLowerInvariant();

        if (effect == "image")
            return ShowMedia(info, f, ditherMode, cancellationToken);

        switch (effect)
        {
            case "matrix":
            case "digital rain": DrawMatrix(c, f); break;
            case "bounce": DrawBounce(c, f, string.IsNullOrWhiteSpace(info.Text) ? "SPB" : info.Text); break;
            case "warp":
            case "warp tunnel": DrawWarpTunnel(c, f, info.Text); break;
            case "pong": DrawPong(c, f); break;
            case "orbit": DrawOrbit(c, f); break;
            case "wave": DrawWave(c, f, info.Text); break;
            case "rain": DrawRain(c, f); break;
            case "scanner": DrawScanner(c, f, info.Text); break;
            case "clock": DrawClock(c); break;
            case "starfield":
            default: DrawStarfield(c, f, info.Text); break;
        }

        if (info.Invert) c.Invert();
        return WriteCanvas(c, cancellationToken);
    }

    public bool ShowMessage(string title, string line2 = "", string line3 = "", CancellationToken cancellationToken = default)
    {
        var c = new MonoCanvas(Width, Height);
        c.DrawText(title.ToUpperInvariant(), 2, 6, 2);
        if (!string.IsNullOrWhiteSpace(line2)) c.DrawText(line2.ToUpperInvariant(), 2, 31, 1);
        if (!string.IsNullOrWhiteSpace(line3)) c.DrawText(line3.ToUpperInvariant(), 2, 45, 1);
        return WriteCanvas(c, cancellationToken);
    }

    private void InitAnimations()
    {
        for (int i = 0; i < _stars.Length; i++)
            _stars[i] = new Star(_random.Next(Width), _random.Next(Height), _random.Next(1, 4));
        for (int i = 0; i < _matrixY.Length; i++) _matrixY[i] = _random.Next(-64, 64);
    }

    private void DrawStarfield(MonoCanvas c, int frame, string text)
    {
        int cx = Width / 2, cy = Height / 2;
        for (int i = 0; i < _stars.Length; i++)
        {
            Star s = _stars[i];
            int dx = s.X - cx;
            int dy = s.Y - cy;
            s.X += Math.Sign(dx == 0 ? (i % 2 == 0 ? 1 : -1) : dx) * s.Speed;
            s.Y += Math.Sign(dy == 0 ? (i % 3 == 0 ? 1 : -1) : dy) * Math.Max(1, s.Speed / 2);
            if (s.X < 0 || s.X >= Width || s.Y < 0 || s.Y >= Height)
                s = new Star(cx + _random.Next(-8, 9), cy + _random.Next(-5, 6), _random.Next(1, 4));
            _stars[i] = s;
            c.Set(s.X, s.Y);
            if (s.Speed >= 3 && s.X + 1 < Width) c.Set(s.X + 1, s.Y);
        }
        string caption = string.IsNullOrWhiteSpace(text) ? "SPACEPILOT" : text.ToUpperInvariant();
        if ((frame / 12) % 2 == 0) c.DrawText(caption, Math.Max(2, (Width - c.MeasureText(caption)) / 2), 55, 1);
    }

    private void DrawMatrix(MonoCanvas c, int frame)
    {
        for (int col = 0; col < _matrixY.Length; col++)
        {
            if ((frame + col) % 2 == 0) _matrixY[col] += 2 + (col % 3);
            if (_matrixY[col] > Height + 18) _matrixY[col] = _random.Next(-36, -4);
            int x = col * 6 + 2;
            for (int tail = 0; tail < 4; tail++)
            {
                int y = _matrixY[col] - tail * 9;
                if (y < -7 || y >= Height) continue;
                char ch = (char)('0' + ((frame + col * 7 + tail * 3) % 10));
                c.DrawText(ch.ToString(), x, y, 1);
            }
        }
    }

    private static void DrawBounce(MonoCanvas c, int frame, string text)
    {
        string label = string.IsNullOrWhiteSpace(text) ? "SPB" : text.ToUpperInvariant();
        int boxW = Math.Clamp(c.MeasureText(label) + 14, 34, 110);
        int boxH = 18;
        int maxX = Width - boxW;
        int maxY = Height - boxH;
        int periodX = Math.Max(1, maxX * 2);
        int periodY = Math.Max(1, maxY * 2);
        int px = (frame * 5) % periodX;
        int py = (frame * 3) % periodY;
        int x = px <= maxX ? px : periodX - px;
        int y = py <= maxY ? py : periodY - py;
        c.Rect(x, y, boxW, boxH);
        c.DrawText(label, x + 7, y + 6, 1);
    }

    private static void DrawWarpTunnel(MonoCanvas c, int frame, string text)
    {
        int cx = Width / 2;
        int cy = Height / 2;
        for (int i = 0; i < 9; i++)
        {
            int phase = (frame * 3 + i * 17) % 90;
            int w = 12 + phase * 3;
            int h = 4 + phase;
            int x = cx - w / 2;
            int y = cy - h / 2;
            c.Rect(x, y, w, h);
        }
        if (!string.IsNullOrWhiteSpace(text))
        {
            string t = text.ToUpperInvariant();
            c.DrawText(t, Math.Max(2, (Width - c.MeasureText(t)) / 2), 55, 1);
        }
    }

    private static void DrawPong(MonoCanvas c, int frame)
    {
        int leftY = 8 + (int)((Math.Sin(frame * 0.18) + 1) * 18);
        int rightY = 8 + (int)((Math.Sin(frame * 0.18 + 2.2) + 1) * 18);
        c.FillRect(4, leftY, 3, 18);
        c.FillRect(Width - 7, rightY, 3, 18);
        c.VLine(Width / 2, 0, Height - 1, dotted: true);
        int maxX = Width - 16;
        int period = maxX * 2;
        int px = (frame * 5) % period;
        int x = 8 + (px <= maxX ? px : period - px);
        int y = 29 + (int)(Math.Sin(frame * 0.31) * 23);
        c.FillRect(x - 2, y - 2, 4, 4);
        c.DrawText($"{(frame / 80) % 10}  {(frame / 110) % 10}", 103, 2, 1);
    }

    private static void DrawOrbit(MonoCanvas c, int frame)
    {
        int cx = Width / 2, cy = Height / 2;
        c.Circle(cx, cy, 8);
        for (int i = 0; i < 3; i++)
        {
            int rx = 30 + i * 24;
            int ry = 10 + i * 7;
            c.Ellipse(cx, cy, rx, ry);
            double angle = frame * (0.10 + i * 0.025) + i * 2.1;
            int x = cx + (int)(Math.Cos(angle) * rx);
            int y = cy + (int)(Math.Sin(angle) * ry);
            c.FillRect(x - 1, y - 1, 3, 3);
        }
    }

    private static void DrawWave(MonoCanvas c, int frame, string text)
    {
        for (int layer = 0; layer < 3; layer++)
        {
            int prevY = 0;
            for (int x = 0; x < Width; x++)
            {
                double phase = x * (0.055 + layer * 0.008) + frame * (0.18 + layer * 0.04);
                int y = 31 + (int)(Math.Sin(phase) * (8 + layer * 5));
                if (x > 0) c.Line(x - 1, prevY, x, y);
                prevY = y;
            }
        }
        if (!string.IsNullOrWhiteSpace(text)) c.DrawText(text.ToUpperInvariant(), 3, 3, 1);
    }

    private static void DrawRain(MonoCanvas c, int frame)
    {
        for (int i = 0; i < 30; i++)
        {
            int x = (i * 47 + 13) % Width;
            int y = (frame * (2 + i % 4) + i * 19) % (Height + 20) - 10;
            int len = 3 + (i % 6);
            c.VLine(x, y, y + len);
            if (i % 7 == 0 && y > 48) c.HLine(x - 2, Math.Min(Height - 1, y + len + 1), x + 2);
        }
    }

    private static void DrawScanner(MonoCanvas c, int frame, string text)
    {
        int max = Width - 24;
        int period = max * 2;
        int p = (frame * 7) % period;
        int x = 12 + (p <= max ? p : period - p);
        for (int d = -10; d <= 10; d++)
            if (Math.Abs(d) % 3 == 0) c.VLine(x + d, 23 + Math.Abs(d) / 2, 40 - Math.Abs(d) / 2);
        c.Rect(8, 18, Width - 16, 28);
        string label = string.IsNullOrWhiteSpace(text) ? "SCANNING" : text.ToUpperInvariant();
        c.DrawText(label, Math.Max(2, (Width - c.MeasureText(label)) / 2), 51, 1);
    }

    private static void DrawClock(MonoCanvas c)
    {
        string time = DateTime.Now.ToString("HH:mm");
        int tw = c.MeasureText(time, 3);
        c.DrawText(time, (Width - tw) / 2, 7, 3);
        string date = DateTime.Now.ToString("ddd  dd MMM").ToUpperInvariant();
        c.DrawText(date, Math.Max(2, (Width - c.MeasureText(date)) / 2), 42, 1);
        c.HLine(35, 56, Width - 36);
    }

    private bool ShowMedia(ScreensaverInfo info, int frame, string ditherMode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(info.FilePath) || !File.Exists(info.FilePath))
            return ShowMessage("FILE MISSING", cancellationToken: cancellationToken);

        CachedMedia media = GetMedia(info.FilePath, ditherMode, cancellationToken);
        if (media.Frames.Count == 0)
            return ShowMessage("BAD IMAGE", cancellationToken: cancellationToken);

        byte[] packed = media.Frames[Math.Abs(frame) % media.Frames.Count];
        if (!info.Invert)
            return WritePackedFrame(packed, cancellationToken);

        var inverted = new byte[packed.Length];
        for (int i = 0; i < packed.Length; i++) inverted[i] = (byte)~packed[i];
        return WritePackedFrame(inverted, cancellationToken);
    }

    private CachedMedia GetMedia(string path, string ditherMode, CancellationToken cancellationToken)
    {
        DateTime stamp = File.GetLastWriteTimeUtc(path);
        lock (_mediaGate)
        {
            string cacheKey = path + "|" + ditherMode;
            if (_mediaCache.TryGetValue(cacheKey, out CachedMedia? cachedHit) && cachedHit.LastWriteUtc == stamp)
                return cachedHit;
        }

        var frames = new List<byte[]>();
        try
        {
            using Image image = Image.FromFile(path);
            int frameCount = 1;
            FrameDimension? dimension = null;
            if (image.FrameDimensionsList.Length > 0)
            {
                dimension = new FrameDimension(image.FrameDimensionsList[0]);
                frameCount = Math.Clamp(image.GetFrameCount(dimension), 1, 120);
            }

            for (int i = 0; i < frameCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (dimension is not null) image.SelectActiveFrame(dimension, i);

                using var target = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
                using (Graphics g = Graphics.FromImage(target))
                {
                    g.Clear(Color.White);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    double scale = Math.Min((double)Width / image.Width, (double)Height / image.Height);
                    int w = Math.Max(1, (int)Math.Round(image.Width * scale));
                    int h = Math.Max(1, (int)Math.Round(image.Height * scale));
                    int x = (Width - w) / 2;
                    int y = (Height - h) / 2;
                    g.DrawImage(image, x, y, w, h);
                }

                // Store media directly in the SpacePilot native page/column layout.
                frames.Add(PackBitmap(target, ditherMode, cancellationToken));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }

        var cached = new CachedMedia(stamp, frames);
        lock (_mediaGate)
        {
            string cacheKey = path + "|" + ditherMode;
            _mediaCache[cacheKey] = cached;

            // Avoid unbounded RAM growth if the user experiments with many GIFs.
            if (_mediaCache.Count > 8)
            {
                foreach (string key in _mediaCache.Keys.Where(k => !k.Equals(cacheKey, StringComparison.OrdinalIgnoreCase)).Take(_mediaCache.Count - 8).ToArray())
                    _mediaCache.Remove(key);
            }
        }
        return cached;
    }

    private static byte[] PackBitmap(Bitmap target, string ditherMode, CancellationToken cancellationToken)
    {
        var gray = new double[Width * Height];
        var rect = new Rectangle(0, 0, Width, Height);
        BitmapData bits = target.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            int stride = Math.Abs(bits.Stride);
            var bytes = new byte[stride * Height];
            Marshal.Copy(bits.Scan0, bytes, 0, bytes.Length);
            for (int y = 0; y < Height; y++)
            {
                int row = bits.Stride >= 0 ? y * stride : (Height - 1 - y) * stride;
                for (int x = 0; x < Width; x++)
                {
                    int px = row + x * 3;
                    int b = bytes[px], g = bytes[px + 1], r = bytes[px + 2];
                    gray[y * Width + x] = (r * 299 + g * 587 + b * 114) / 1000.0;
                }
            }
        }
        finally { target.UnlockBits(bits); }

        var black = new bool[Width * Height];
        if (ditherMode.Equals("Floyd-Steinberg", StringComparison.OrdinalIgnoreCase))
        {
            var work = (double[])gray.Clone();
            for (int y = 0; y < Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < Width; x++)
                {
                    int idx = y * Width + x;
                    double oldPixel = work[idx];
                    double newPixel = oldPixel < 128 ? 0 : 255;
                    black[idx] = newPixel == 0;
                    double err = oldPixel - newPixel;
                    if (x + 1 < Width) work[idx + 1] += err * 7.0 / 16.0;
                    if (y + 1 < Height)
                    {
                        if (x > 0) work[idx + Width - 1] += err * 3.0 / 16.0;
                        work[idx + Width] += err * 5.0 / 16.0;
                        if (x + 1 < Width) work[idx + Width + 1] += err * 1.0 / 16.0;
                    }
                }
            }
        }
        else
        {
            int[,] b2 = { { 0, 2 }, { 3, 1 } };
            int[,] b4 = { {0,8,2,10},{12,4,14,6},{3,11,1,9},{15,7,13,5} };
            for (int y = 0; y < Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < Width; x++)
                {
                    double threshold = 135;
                    if (ditherMode.Equals("Bayer 2x2", StringComparison.OrdinalIgnoreCase)) threshold = 48 + b2[y & 1, x & 1] * 53;
                    else if (ditherMode.Equals("Bayer 4x4", StringComparison.OrdinalIgnoreCase)) threshold = 24 + b4[y & 3, x & 3] * 14.5;
                    black[y * Width + x] = gray[y * Width + x] < threshold;
                }
            }
        }

        var packed = new byte[Width * 8];
        for (int y = 0; y < Height; y++)
        {
            int page = y >> 3, bit = y & 7;
            for (int x = 0; x < Width; x++)
                if (black[y * Width + x]) packed[page * Width + x] |= (byte)(1 << bit);
        }
        return packed;
    }

    private static string PrettyAppName(string activeApp)
    {
        if (string.IsNullOrWhiteSpace(activeApp)) return "SPACEPILOT";
        string name = Path.GetFileNameWithoutExtension(activeApp).Trim();
        string n = name.ToLowerInvariant();
        if (n.Contains("fusion")) return "FUSION 360";
        if (n.Contains("maya")) return "MAYA";
        if (n.Contains("zbrush")) return "ZBRUSH";
        if (n.Contains("resolve")) return "DAVINCI RESOLVE";
        if (n.Contains("ableton") || n == "live") return "ABLETON LIVE";
        return name.ToUpperInvariant();
    }

    private static string FormatButtons(IReadOnlyCollection<int>? buttons)
    {
        if (buttons is null || buttons.Count == 0) return "KEYS -";
        return "KEYS " + string.Join("+", buttons.Take(5).Select(SpacePilotButtons.GetName)).ToUpperInvariant();
    }

    private bool WriteCanvas(MonoCanvas canvas, CancellationToken cancellationToken)
        => WritePackedFrame(PackCanvas(canvas), cancellationToken);

    private static byte[] PackCanvas(MonoCanvas canvas)
    {
        var packed = new byte[Width * 8];
        for (int page = 0; page < 8; page++)
        {
            int y0 = page * 8;
            for (int x = 0; x < Width; x++)
            {
                byte column = 0;
                for (int bit = 0; bit < 8; bit++)
                    if (canvas.Get(x, y0 + bit)) column |= (byte)(1 << bit);
                packed[page * Width + x] = column;
            }
        }
        return packed;
    }

    private bool WritePackedFrame(ReadOnlySpan<byte> packed, CancellationToken cancellationToken)
    {
        if (packed.Length != Width * 8) return false;
        cancellationToken.ThrowIfCancellationRequested();
        int epoch = Volatile.Read(ref _ioEpoch);
        if (!EnsureConnected()) return false;
        HidFeatureDevice? device = Volatile.Read(ref _device);
        if (device is null) return false;

        try
        {
            if (!_suppressed)
            {
                if (!device.WriteFeature(new byte[] { 0x12, 0x00, 0x00, 0x2F, 0, 0, 0, 0 })) return false;
                _suppressed = true;
            }

            bool forceFull = !_hasLastPacked || _framesSinceFullRefresh >= Math.Max(1, FullRefreshEveryFrames);
            for (int page = 0; page < 8; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (epoch != Volatile.Read(ref _ioEpoch)) return false;
                if (!forceFull && !PageChanged(page, packed)) continue;
                if (!WriteWholePage(device, page, packed, cancellationToken, epoch)) return false;
            }

            packed.CopyTo(_lastPacked);
            _hasLastPacked = true;
            _framesSinceFullRefresh = forceFull ? 0 : _framesSinceFullRefresh + 1;
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            Disconnect();
            return false;
        }
    }

    private bool WriteWholePage(HidFeatureDevice device, int page, ReadOnlySpan<byte> packed, CancellationToken cancellationToken, int epoch)
    {
        int row = page * Width;

        // A single sequential run covers columns 0..237 without crossing the
        // physical 240-column boundary. A final overlapped 233..239 write fills
        // the tail. This avoids v0.6's wrapped partial blocks while keeping the
        // number of feature reports low enough for the original LCD.
        if (!device.WriteFeature(new byte[] { 0x0C, (byte)page, 0x00, 0x00, 0, 0, 0, 0 })) return false;
        DelayAfterReport(cancellationToken);
        int writes = 0;
        for (int col = 0; col <= 231; col += 7)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (epoch != Volatile.Read(ref _ioEpoch)) return false;
            var report = new byte[8];
            report[0] = 0x0D;
            for (int i = 0; i < 7; i++) report[i + 1] = packed[row + col + i];
            if (!device.WriteFeature(report)) return false;
            DelayAfterReport(cancellationToken);
            writes++;
        }

        const int tail = Width - 7;
        if (!device.WriteFeature(new byte[] { 0x0C, (byte)page, (byte)tail, 0x00, 0, 0, 0, 0 })) return false;
        DelayAfterReport(cancellationToken);
        var tailReport = new byte[8];
        tailReport[0] = 0x0D;
        for (int i = 0; i < 7; i++) tailReport[i + 1] = packed[row + tail + i];
        bool tailOk = device.WriteFeature(tailReport);
        if (tailOk) DelayAfterReport(cancellationToken);
        return tailOk;
    }

    private void DelayAfterReport(CancellationToken cancellationToken)
    {
        int delay = Math.Clamp(ReportDelayMilliseconds, 0, 12);
        if (delay <= 0) return;
        if (cancellationToken.WaitHandle.WaitOne(delay))
            cancellationToken.ThrowIfCancellationRequested();
    }

    private bool PageChanged(int page, ReadOnlySpan<byte> packed)
    {
        if (!_hasLastPacked) return true;
        int row = page * Width;
        for (int x = 0; x < Width; x++)
            if (packed[row + x] != _lastPacked[row + x]) return true;
        return false;
    }

    /// <summary>
    /// Immediately closes the current LCD HID handle. This is safe to call from
    /// the UI/watchdog thread and is used to break a synchronous HidD_SetFeature
    /// that has stopped making progress.
    /// </summary>
    public void AbortCurrentIo()
    {
        Interlocked.Increment(ref _ioEpoch);
        Disconnect();
    }

    private void Disconnect()
    {
        HidFeatureDevice? device = Interlocked.Exchange(ref _device, null);
        try { device?.Dispose(); } catch { }
        _suppressed = false;
        _hasLastPacked = false;
        _framesSinceFullRefresh = 0;
    }

    public void Dispose()
    {
        lock (_mediaGate) _mediaCache.Clear();
        AbortCurrentIo();
    }

    private struct Star
    {
        public int X;
        public int Y;
        public int Speed;
        public Star(int x, int y, int speed) { X = x; Y = y; Speed = speed; }
    }

    private sealed record CachedMedia(DateTime LastWriteUtc, List<byte[]> Frames);
}
internal sealed class MonoCanvas
{
    private readonly bool[] _pixels;
    public int Width { get; }
    public int Height { get; }

    public MonoCanvas(int width, int height)
    {
        Width = width;
        Height = height;
        _pixels = new bool[width * height];
    }

    public void Set(int x, int y, bool value = true)
    {
        if ((uint)x >= Width || (uint)y >= Height) return;
        _pixels[y * Width + x] = value;
    }

    public bool Get(int x, int y) => (uint)x < Width && (uint)y < Height && _pixels[y * Width + x];

    public void HLine(int x1, int y, int x2)
    {
        for (int x = Math.Max(0, x1); x <= Math.Min(Width - 1, x2); x++) Set(x, y);
    }

    public void VLine(int x, int y1, int y2, bool dotted = false)
    {
        int start = Math.Max(0, Math.Min(y1, y2));
        int end = Math.Min(Height - 1, Math.Max(y1, y2));
        for (int y = start; y <= end; y++)
            if (!dotted || ((y - start) & 1) == 0) Set(x, y);
    }

    public void FillRect(int x, int y, int w, int h)
    {
        for (int yy = y; yy < y + h; yy++) HLine(x, yy, x + w - 1);
    }

    public void Line(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            Set(x0, y0);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    public void Circle(int cx, int cy, int radius) => Ellipse(cx, cy, radius, radius);

    public void Ellipse(int cx, int cy, int rx, int ry)
    {
        if (rx <= 0 || ry <= 0) return;
        int steps = Math.Max(24, (rx + ry) * 3);
        for (int i = 0; i < steps; i++)
        {
            double a = i * Math.PI * 2.0 / steps;
            Set(cx + (int)Math.Round(Math.Cos(a) * rx), cy + (int)Math.Round(Math.Sin(a) * ry));
        }
    }

    public void Invert()
    {
        for (int i = 0; i < _pixels.Length; i++) _pixels[i] = !_pixels[i];
    }

    public void Rect(int x, int y, int w, int h)
    {
        HLine(x, y, x + w - 1);
        HLine(x, y + h - 1, x + w - 1);
        for (int yy = y; yy < y + h; yy++) { Set(x, yy); Set(x + w - 1, yy); }
    }

    public int MeasureText(string text, int scale = 1) => string.IsNullOrEmpty(text) ? 0 : text.Length * 6 * scale - scale;

    public void DrawText(string text, int x, int y, int scale = 1)
    {
        int cursor = x;
        foreach (char raw in text)
        {
            char ch = char.ToUpperInvariant(raw);
            byte[] glyph = PixelFont.Get(ch);
            for (int col = 0; col < 5; col++)
            {
                byte bits = glyph[col];
                for (int row = 0; row < 7; row++)
                {
                    if ((bits & (1 << row)) == 0) continue;
                    for (int sx = 0; sx < scale; sx++)
                        for (int sy = 0; sy < scale; sy++)
                            Set(cursor + col * scale + sx, y + row * scale + sy);
                }
            }
            cursor += 6 * scale;
            if (cursor >= Width) break;
        }
    }
}

internal static class PixelFont
{
    private static readonly Dictionary<char, byte[]> Glyphs = new()
    {
        [' '] = G("00000","00000","00000","00000","00000","00000","00000"),
        ['A'] = G("01110","10001","10001","11111","10001","10001","10001"),
        ['B'] = G("11110","10001","10001","11110","10001","10001","11110"),
        ['C'] = G("01111","10000","10000","10000","10000","10000","01111"),
        ['D'] = G("11110","10001","10001","10001","10001","10001","11110"),
        ['E'] = G("11111","10000","10000","11110","10000","10000","11111"),
        ['F'] = G("11111","10000","10000","11110","10000","10000","10000"),
        ['G'] = G("01111","10000","10000","10111","10001","10001","01111"),
        ['H'] = G("10001","10001","10001","11111","10001","10001","10001"),
        ['I'] = G("11111","00100","00100","00100","00100","00100","11111"),
        ['J'] = G("00111","00010","00010","00010","10010","10010","01100"),
        ['K'] = G("10001","10010","10100","11000","10100","10010","10001"),
        ['L'] = G("10000","10000","10000","10000","10000","10000","11111"),
        ['M'] = G("10001","11011","10101","10101","10001","10001","10001"),
        ['N'] = G("10001","11001","10101","10011","10001","10001","10001"),
        ['O'] = G("01110","10001","10001","10001","10001","10001","01110"),
        ['P'] = G("11110","10001","10001","11110","10000","10000","10000"),
        ['Q'] = G("01110","10001","10001","10001","10101","10010","01101"),
        ['R'] = G("11110","10001","10001","11110","10100","10010","10001"),
        ['S'] = G("01111","10000","10000","01110","00001","00001","11110"),
        ['T'] = G("11111","00100","00100","00100","00100","00100","00100"),
        ['U'] = G("10001","10001","10001","10001","10001","10001","01110"),
        ['V'] = G("10001","10001","10001","10001","10001","01010","00100"),
        ['W'] = G("10001","10001","10001","10101","10101","10101","01010"),
        ['X'] = G("10001","10001","01010","00100","01010","10001","10001"),
        ['Y'] = G("10001","10001","01010","00100","00100","00100","00100"),
        ['Z'] = G("11111","00001","00010","00100","01000","10000","11111"),
        ['0'] = G("01110","10001","10011","10101","11001","10001","01110"),
        ['1'] = G("00100","01100","00100","00100","00100","00100","01110"),
        ['2'] = G("01110","10001","00001","00010","00100","01000","11111"),
        ['3'] = G("11110","00001","00001","01110","00001","00001","11110"),
        ['4'] = G("00010","00110","01010","10010","11111","00010","00010"),
        ['5'] = G("11111","10000","10000","11110","00001","00001","11110"),
        ['6'] = G("01110","10000","10000","11110","10001","10001","01110"),
        ['7'] = G("11111","00001","00010","00100","01000","01000","01000"),
        ['8'] = G("01110","10001","10001","01110","10001","10001","01110"),
        ['9'] = G("01110","10001","10001","01111","00001","00001","01110"),
        ['-'] = G("00000","00000","00000","11111","00000","00000","00000"),
        ['+'] = G("00000","00100","00100","11111","00100","00100","00000"),
        ['.'] = G("00000","00000","00000","00000","00000","01100","01100"),
        [':'] = G("00000","01100","01100","00000","01100","01100","00000"),
        ['/'] = G("00001","00010","00010","00100","01000","01000","10000"),
        ['='] = G("00000","11111","00000","11111","00000","00000","00000"),
        ['_'] = G("00000","00000","00000","00000","00000","00000","11111"),
        ['?'] = G("01110","10001","00001","00010","00100","00000","00100"),
        ['%'] = G("11001","11010","00100","01000","10110","00110","00000"),
        ['('] = G("00010","00100","01000","01000","01000","00100","00010"),
        [')'] = G("01000","00100","00010","00010","00010","00100","01000"),
        ['['] = G("01110","01000","01000","01000","01000","01000","01110"),
        [']'] = G("01110","00010","00010","00010","00010","00010","01110"),
    };

    public static byte[] Get(char c) => Glyphs.TryGetValue(c, out var g) ? g : Glyphs['?'];

    private static byte[] G(params string[] rows)
    {
        var cols = new byte[5];
        for (int y = 0; y < Math.Min(7, rows.Length); y++)
            for (int x = 0; x < Math.Min(5, rows[y].Length); x++)
                if (rows[y][x] == '1') cols[x] |= (byte)(1 << y);
        return cols;
    }
}
