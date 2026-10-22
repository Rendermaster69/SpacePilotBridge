using System.Diagnostics;
using SpacePilotBridge.Core;
using SpacePilotBridge.Lcd;
using SpacePilotBridge.Virtual;

namespace SpacePilotBridge.App;

internal sealed class BridgeApplicationContext : ApplicationContext
{
    private readonly SpacePilotHub _hub = SpacePilotHub.Instance;
    private readonly VirtualSpaceMouseWriter _virtual;
    private readonly LcdHostClient _lcd = new();
    private readonly LedController _led;
    private readonly EditorAxisController _editor;
    private readonly NowPlayingProvider _nowPlaying = new();
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ButtonActionEngine _actions;
    private SettingsForm? _settingsForm;

    private string _lastApp = string.Empty;
    private string _lastProcess = string.Empty;
    private string _lastProfile = "global";
    private string _lastLcdSignature = string.Empty;
    private DateTime _lastLcdWrite = DateTime.MinValue;
    private DateTime _lastForegroundPoll = DateTime.MinValue;
    private DateTime _lastAnimationWrite = DateTime.MinValue;
    private DateTime _lastPlaylistChange = DateTime.MinValue;
    private int _animationFrame;
    private int _playlistIndex = -1;
    private bool _idleSaverActive;
    private readonly Random _random = new();
    private volatile bool _openSettingsPending;
    private volatile int _lastPressedRaw = -1;
    private string? _pendingToast;

    private readonly object _lcdControlGate = new();
    private CancellationTokenSource _lcdCts = new();
    private int _lcdGeneration;
    private int _lcdBusy;
    private Task? _lcdTask;
    private DateTime _lcdOperationStarted = DateTime.MinValue;
    private DateTime _lastLcdRecovery = DateTime.MinValue;
    private volatile bool _lcdStopping;
    private int _lcdRecoveries;

    public BridgeApplicationContext()
    {
        BridgeSettings settings = SettingsStore.Load();
        AutoStartManager.Apply(settings.AutoStart);

        _hub.Acquire();
        _actions = new ButtonActionEngine(_hub, settings);
        _virtual = new VirtualSpaceMouseWriter(_hub)
        {
            StateTransform = _actions.TransformState,
            ButtonMapper = _actions.MapVirtualButtons,
            GamepadEnabledProvider = () => _actions.Settings.GamepadEnabled
        };
        _led = new LedController(_hub);
        _editor = new EditorAxisController(_hub, _actions);
        _lcd.FullRefreshEveryFrames = settings.LcdFullRefreshFrames;
        _lcd.ReportDelayMilliseconds = settings.LcdReportDelayMilliseconds;
        _lcd.ForceFullAnimationFrames = settings.LcdForceFullAnimationFrames;

        var menu = BuildTrayMenu();
        _tray = new NotifyIcon
        {
            Text = "SpacePilotBridge",
            Icon = TrayIconFactory.CreateIcon(),
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => ShowSettings();

        _actions.OpenSettingsRequested += (_, _) => _openSettingsPending = true;
        _actions.SettingsChanged += (_, _) => OnSettingsChanged();
        _actions.ScreensaverChanged += (_, _) =>
        {
            _animationFrame = 0;
            _lastPlaylistChange = DateTime.UtcNow;
            RestartLcdPipeline("screensaver changed", log: false);
        };
        _actions.LcdRecoverRequested += (_, _) => RefreshLcdNow();
        _actions.ToastRequested += text => _pendingToast = text;
        _actions.PhysicalButtonPressed += raw => _lastPressedRaw = raw;
        _actions.ActivityDetected += (_, _) => OnPhysicalActivity();

        _hub.ConnectionChanged += (_, _) =>
        {
            _lastLcdSignature = string.Empty;
            _led.Reset();
            RestartLcdPipeline("SpacePilot connection changed", log: false);
        };
        _virtual.ConnectionChanged += (_, _) => _lastLcdSignature = string.Empty;

        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        if (!settings.StartMinimized) _openSettingsPending = true;
        Log("SpacePilotBridge v0.9.2 started");
        Tick();
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings...", null, (_, _) => ShowSettings());
        menu.Items.Add("Show status", null, (_, _) => ShowStatusBalloon());

        var speed = new ToolStripMenuItem("Speed");
        speed.DropDownItems.Add("Faster (+)", null, (_, _) => _actions.ExecuteBuiltIn("IncreaseSensitivity"));
        speed.DropDownItems.Add("Slower (-)", null, (_, _) => _actions.ExecuteBuiltIn("DecreaseSensitivity"));
        menu.Items.Add(speed);

        var led = new ToolStripMenuItem("SpacePilot LED");
        led.DropDownOpening += (_, _) => PopulateLedMenu(led);
        menu.Items.Add(led);

        var lcd = new ToolStripMenuItem("LCD screensaver");
        lcd.DropDownOpening += (_, _) => PopulateScreensaverMenu(lcd);
        menu.Items.Add(lcd);

        menu.Items.Add("Refresh / recover LCD", null, (_, _) => RefreshLcdNow());
        menu.Items.Add("Open log/settings folder", null, (_, _) => OpenLogFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        return menu;
    }

    private void OnSettingsChanged()
    {
        BridgeSettings s = _actions.Settings;
        _lastLcdSignature = string.Empty;
        _lcd.FullRefreshEveryFrames = s.LcdFullRefreshFrames;
        _lcd.ReportDelayMilliseconds = s.LcdReportDelayMilliseconds;
        _lcd.ForceFullAnimationFrames = s.LcdForceFullAnimationFrames;
        _led.Reset();
        AutoStartManager.Apply(s.AutoStart);
    }

    private void OnPhysicalActivity()
    {
        if (_idleSaverActive)
        {
            _idleSaverActive = false;
            _actions.StopScreensaver();
            _lastLcdSignature = string.Empty;
        }
    }

    private void Tick()
    {
        DateTime now = DateTime.UtcNow;

        if (Volatile.Read(ref _lcdBusy) != 0 &&
            _lcdOperationStarted != DateTime.MinValue &&
            now - _lcdOperationStarted > TimeSpan.FromSeconds(10) &&
            now - _lastLcdRecovery > TimeSpan.FromSeconds(1.5))
        {
            _lastLcdRecovery = now;
            _lcdRecoveries++;
            RestartLcdPipeline("LCD watchdog timeout", log: true);
        }

        if (now - _lastForegroundPoll >= TimeSpan.FromMilliseconds(350))
        {
            _lastForegroundPoll = now;
            ForegroundAppInfo foreground = ForegroundApp.Get();

            // Never emit editor/key/wheel output in the bridge UI or in the Global
            // profile. The exact foreground profile is evaluated below before the
            // controller is enabled.
            _editor.Enabled = false;

            if (!foreground.IsBridge && !string.IsNullOrWhiteSpace(foreground.ProcessName))
            {
                AppDiscoveryStore.Observe(foreground);
                string app = foreground.DisplayName;
                string profile = ProfileResolver.FromForeground(foreground, _actions.Settings);
                if (!string.Equals(app, _lastApp, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(foreground.ExecutableName, _lastProcess, StringComparison.OrdinalIgnoreCase))
                {
                    _lastApp = app;
                    _lastProcess = foreground.ExecutableName;
                    Log($"Foreground: {app} [{_lastProcess}]");
                    _lastLcdSignature = string.Empty;
                }
                if (!string.Equals(profile, _lastProfile, StringComparison.OrdinalIgnoreCase))
                {
                    _editor.ResetOutputState();
                    _lastProfile = profile;
                    _actions.SetProfile(profile);
                    Log($"Profile: {profile}");
                    _lastLcdSignature = string.Empty;
                }

                ProfileSettings foregroundProfile = _actions.GetCurrentProfileSettings();
                _editor.Enabled = !profile.Equals("global", StringComparison.OrdinalIgnoreCase) && foregroundProfile.EditorControlsEnabled == true;
            }
        }

        if (_openSettingsPending)
        {
            _openSettingsPending = false;
            ShowSettings();
        }

        if (_lastPressedRaw >= 0)
        {
            int raw = _lastPressedRaw;
            _lastPressedRaw = -1;
            _settingsForm?.NotifyPhysicalButton(raw);
        }

        BridgeSettings settings = _actions.Settings;

        if (!string.IsNullOrWhiteSpace(_pendingToast))
        {
            string text = _pendingToast!;
            _pendingToast = null;
            if (!settings.SilentMode)
            {
                _tray.BalloonTipTitle = "SpacePilotBridge";
                _tray.BalloonTipText = text;
                _tray.ShowBalloonTip(1000);
            }
            _lastLcdSignature = string.Empty;
        }

        HandleIdleAndPlaylist(now, settings);
        _led.Tick(settings.LedPattern, _actions.ScreensaverActive);

        if (_actions.ScreensaverActive)
        {
            // The original SpacePilot LCD is slow. Pace animation frames by the
            // measured time of the previous complete LCD flush and never advance
            // the GIF frame until that flush has succeeded.
            int requestedInterval = ScreensaverLibrary.GetIntervalMilliseconds(_actions.ScreensaverName);
            int measuredInterval = _lcd.LastRoundTripMilliseconds > 1
                ? (int)Math.Ceiling(_lcd.LastRoundTripMilliseconds * 1.20 + 50)
                : 0;
            int interval = Math.Clamp(Math.Max(requestedInterval, measuredInterval), 900, 10000);
            if (now - _lastAnimationWrite >= TimeSpan.FromMilliseconds(interval))
            {
                string saver = _actions.ScreensaverName;
                int frame = Volatile.Read(ref _animationFrame);
                string dither = settings.LcdDitherMode;
                bool queued = QueueLcd(token =>
                {
                    bool ok = _lcd.ShowAnimation(saver, frame, dither, token);
                    if (ok) Interlocked.CompareExchange(ref _animationFrame, frame + 1, frame);
                    return ok;
                });
                if (queued) _lastAnimationWrite = now;
            }
        }
        else
        {
            UpdateLcd(force: false);
        }

        _tray.Text = _hub.IsConnected
            ? (_virtual.IsConnected ? $"SpacePilotBridge - ready - {settings.Sensitivity:0.00}x" : "SpacePilotBridge - virtual HID offline")
            : "SpacePilotBridge - SpacePilot offline";
    }

    private void HandleIdleAndPlaylist(DateTime now, BridgeSettings settings)
    {
        if (!_actions.ScreensaverActive && settings.IdleScreensaverEnabled &&
            now - _actions.LastActivityUtc >= TimeSpan.FromSeconds(settings.IdleScreensaverSeconds))
        {
            _idleSaverActive = true;
            _actions.StartScreensaver(PickInitialSaver(settings));
            _lastPlaylistChange = now;
        }

        if (_actions.ScreensaverActive && settings.ScreensaverPlaylistEnabled && settings.ScreensaverPlaylist.Count > 0 &&
            now - _lastPlaylistChange >= TimeSpan.FromSeconds(settings.ScreensaverPlaylistSeconds))
        {
            _lastPlaylistChange = now;
            _actions.StartScreensaver(PickNextSaver(settings));
        }
    }

    private string PickInitialSaver(BridgeSettings settings)
    {
        if (settings.ScreensaverPlaylistEnabled && settings.ScreensaverPlaylist.Count > 0)
        {
            _playlistIndex = -1;
            return PickNextSaver(settings);
        }
        return settings.DefaultScreensaver;
    }

    private string PickNextSaver(BridgeSettings settings)
    {
        var list = settings.ScreensaverPlaylist.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (list.Count == 0) return settings.DefaultScreensaver;
        if (settings.ScreensaverPlaylistShuffle)
        {
            int next = list.Count == 1 ? 0 : _random.Next(list.Count);
            if (list.Count > 1 && next == _playlistIndex) next = (next + 1) % list.Count;
            _playlistIndex = next;
        }
        else
        {
            _playlistIndex = (_playlistIndex + 1 + list.Count) % list.Count;
        }
        return list[_playlistIndex];
    }

    private void UpdateLcd(bool force)
    {
        if (_actions.ScreensaverActive && !force) return;
        SpacePilotState state = _hub.State;
        BridgeSettings s = _actions.Settings;
        VirtualWriterMetrics vm = _virtual.GetMetrics();
        string nowPlaying = _nowPlaying.Line;
        string sig = $"{_lastApp}|{_lastProfile}|{_hub.IsConnected}|{_virtual.IsConnected}|{s.Sensitivity:0.00}|{s.DominantMode}|{s.RotationLocked}|{s.LedPattern}|{s.GamepadEnabled}|{vm.AverageLatencyMs:0.0}|{string.Join(',', state.Buttons)}|{string.Join(',', s.LcdWidgets)}|{nowPlaying}";
        if (!force && sig == _lastLcdSignature && DateTime.UtcNow - _lastLcdWrite < TimeSpan.FromSeconds(2)) return;

        string app = _lastApp;
        bool physical = _hub.IsConnected;
        bool virtualConnected = _virtual.IsConnected;
        string profile = ProfileResolver.DisplayName(_lastProfile, _actions.Settings);

        QueueLcd(token =>
        {
            bool ok = _lcd.ShowStatus(app, physical, virtualConnected, state, s.Sensitivity, s.DominantMode, s.RotationLocked,
                profile, vm.AverageLatencyMs, s.LedPattern, s.GamepadEnabled, nowPlaying, s.LcdWidgets, token);
            if (ok)
            {
                _lastLcdSignature = sig;
                _lastLcdWrite = DateTime.UtcNow;
            }
            return ok;
        });
    }

    private bool QueueLcd(Func<CancellationToken, bool> work)
    {
        if (_lcdStopping) return false;
        int generation; CancellationToken token;
        lock (_lcdControlGate)
        {
            if (_lcdStopping) return false;
            if (Interlocked.CompareExchange(ref _lcdBusy, 1, 0) != 0) return false;
            generation = _lcdGeneration; token = _lcdCts.Token; _lcdOperationStarted = DateTime.UtcNow;
        }

        _lcdTask = Task.Run(() =>
        {
            try { if (!token.IsCancellationRequested && generation == Volatile.Read(ref _lcdGeneration)) work(token); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log($"LCD worker error: {ex.GetType().Name}: {ex.Message}"); }
            finally
            {
                if (generation == Volatile.Read(ref _lcdGeneration))
                {
                    _lcdOperationStarted = DateTime.MinValue;
                    Interlocked.Exchange(ref _lcdBusy, 0);
                }
            }
        });
        return true;
    }

    private void RestartLcdPipeline(string reason, bool log)
    {
        CancellationTokenSource old;
        lock (_lcdControlGate)
        {
            old = _lcdCts; _lcdCts = new CancellationTokenSource(); Interlocked.Increment(ref _lcdGeneration);
            Interlocked.Exchange(ref _lcdBusy, 0); _lcdOperationStarted = DateTime.MinValue;
            _lastLcdSignature = string.Empty; _lastLcdWrite = DateTime.MinValue; _lastAnimationWrite = DateTime.MinValue;
        }
        try { old.Cancel(); } catch { }
        try { _lcd.AbortCurrentIo(); } catch { }
        try { old.Dispose(); } catch { }
        if (log) Log(reason);
    }

    private void RefreshLcdNow()
    {
        _lcdRecoveries++;
        RestartLcdPipeline("Manual LCD refresh/recover", log: true);
        if (!_actions.ScreensaverActive) UpdateLcd(force: true);
    }

    private DiagnosticsSnapshot GetDiagnostics()
    {
        SpacePilotState s = _hub.State;
        VirtualWriterMetrics m = _virtual.GetMetrics();
        double ram = 0;
        try { ram = Process.GetCurrentProcess().WorkingSet64 / 1024d / 1024d; } catch { }
        ProfileSettings activeProfile = _actions.GetCurrentProfileSettings();
        return new DiagnosticsSnapshot(_hub.IsConnected, _virtual.IsConnected, m.GamepadSupported, _actions.Settings.GamepadEnabled,
            _lastApp, _lastProcess, ProfileResolver.DisplayName(_lastProfile, _actions.Settings), activeProfile.EditorMode, _editor.LastAction, _editor.EmitCount, _editor.LastSendSucceeded, s.Tx, s.Ty, s.Tz, s.Rx, s.Ry, s.Rz,
            s.Buttons.Count == 0 ? "-" : string.Join("+", s.Buttons.Select(SpacePilotButtons.GetName)),
            m.LastLatencyMs, m.AverageLatencyMs, m.PeakLatencyMs, m.ReportsPerSecond,
            Volatile.Read(ref _lcdBusy) != 0, _lcdRecoveries, _actions.ScreensaverActive ? _actions.ScreensaverName : "OFF", ram);
    }

    private void ShowSettings()
    {
        if (_settingsForm is null || _settingsForm.IsDisposed)
        {
            _settingsForm = new SettingsForm(_actions, GetDiagnostics);
            _settingsForm.Saved += SettingsFormOnSaved;
            _settingsForm.FormClosed += SettingsFormOnClosed;
            _settingsForm.Show();
            return;
        }
        if (_settingsForm.WindowState == FormWindowState.Minimized) _settingsForm.WindowState = FormWindowState.Normal;
        _settingsForm.BringToFront(); _settingsForm.Activate();
    }

    private void SettingsFormOnSaved(object? sender, EventArgs e)
    {
        OnSettingsChanged();
        if (!_actions.ScreensaverActive) UpdateLcd(force: true);
    }

    private void SettingsFormOnClosed(object? sender, FormClosedEventArgs e)
    {
        if (_settingsForm is not null)
        {
            _settingsForm.Saved -= SettingsFormOnSaved; _settingsForm.FormClosed -= SettingsFormOnClosed;
            _settingsForm.Dispose(); _settingsForm = null;
        }
        MemoryTrimmer.TrimSoon();
    }

    private void PopulateLedMenu(ToolStripMenuItem menu)
    {
        menu.DropDownItems.Clear();
        string current = _actions.Settings.LedPattern;
        foreach (string pattern in new[] { "Off", "On", "Activity", "Slow Blink", "Fast Blink", "Heartbeat", "Breathe", "Screensaver Pulse" })
        {
            var item = new ToolStripMenuItem(pattern) { Checked = current.Equals(pattern, StringComparison.OrdinalIgnoreCase) };
            item.Click += (_, _) => _actions.ExecuteBuiltIn($"SetLedPattern:{pattern}");
            menu.DropDownItems.Add(item);
        }
    }

    private void PopulateScreensaverMenu(ToolStripMenuItem menu)
    {
        menu.DropDownItems.Clear();
        foreach (ScreensaverInfo info in ScreensaverLibrary.GetAll())
        {
            string name = info.Name; var item = new ToolStripMenuItem(name); item.Click += (_, _) => { _idleSaverActive = false; _actions.StartScreensaver(name); }; menu.DropDownItems.Add(item);
        }
        menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add("Stop", null, (_, _) => { _idleSaverActive = false; _actions.StopScreensaver(); });
    }

    private void ShowStatusBalloon()
    {
        var s = _actions.Settings; var m = _virtual.GetMetrics();
        _tray.BalloonTipTitle = "SpacePilotBridge 0.9.2";
        _tray.BalloonTipText = $"SpacePilot: {(_hub.IsConnected ? "connected" : "offline")}\nVirtual SpaceMouse: {(_virtual.IsConnected ? "connected" : "offline")}\nProfile: {ProfileResolver.DisplayName(_lastProfile, _actions.Settings)}\nLatency: {m.AverageLatencyMs:0.0} ms\nGamepad: {(s.GamepadEnabled ? (m.GamepadSupported ? "on" : "driver upgrade needed") : "off")}";
        _tray.ShowBalloonTip(2200);
    }

    private static string LogDirectory => SettingsStore.DirectoryPath;
    private static void Log(string text) { try { Directory.CreateDirectory(LogDirectory); File.AppendAllText(Path.Combine(LogDirectory, "bridge.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {text}{Environment.NewLine}"); } catch { } }
    private static void OpenLogFolder() { Directory.CreateDirectory(LogDirectory); Process.Start(new ProcessStartInfo("explorer.exe", LogDirectory) { UseShellExecute = true }); }

    protected override void ExitThreadCore()
    {
        _timer.Stop(); _lcdStopping = true; RestartLcdPipeline("shutdown", log: false); try { _lcdTask?.Wait(500); } catch { }
        _settingsForm?.Dispose(); _tray.Visible = false; _tray.Dispose(); _editor.Dispose(); _nowPlaying.Dispose(); _actions.Dispose(); _led.Dispose(); _lcd.Dispose(); _virtual.Dispose(); _hub.Release();
        Log("SpacePilotBridge stopped"); base.ExitThreadCore();
    }
}
