using SpacePilotBridge.Core;

namespace SpacePilotBridge.App;

internal sealed class ButtonActionEngine : IDisposable
{
    private readonly SpacePilotHub _hub;
    private readonly object _gate = new();
    private HashSet<int> _previous = new();
    private string _profile = "global";
    private bool _suppressEscUntilRelease;
    private DateTime _lastActivityUtc = DateTime.UtcNow;
    private readonly Dictionary<int, CancellationTokenSource> _viewHoldTokens = new();
    private readonly HashSet<int> _viewLongFired = new();
    private uint _virtualPulseBits;
    private DateTime _virtualPulseUntilUtc = DateTime.MinValue;
    private const int ViewPulseMilliseconds = 120;

    public BridgeSettings Settings { get; private set; }
    public bool ScreensaverActive { get; private set; }
    public string ScreensaverName { get; private set; } = "Starfield";

    public event EventHandler? SettingsChanged;
    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? ScreensaverChanged;
    public event EventHandler? LcdRecoverRequested;
    public event Action<string>? ToastRequested;
    public event Action<int>? PhysicalButtonPressed;
    public event EventHandler? ActivityDetected;

    public DateTime LastActivityUtc { get { lock (_gate) return _lastActivityUtc; } }

    public ButtonActionEngine(SpacePilotHub hub, BridgeSettings settings)
    {
        _hub = hub;
        Settings = settings;
        _hub.StateChanged += HubOnStateChanged;
    }

    public void SetProfile(string profile)
    {
        lock (_gate) _profile = string.IsNullOrWhiteSpace(profile) ? "global" : profile;
    }

    public string CurrentProfile
    {
        get { lock (_gate) return _profile; }
    }

    public ProfileSettings GetCurrentProfileSettings()
    {
        lock (_gate)
        {
            if (Settings.Profiles.TryGetValue(_profile, out var p))
                return new ProfileSettings
                {
                    DisplayName = p.DisplayName,
                    EditorControlsEnabled = p.EditorControlsEnabled,
                    EditorMode = p.EditorMode,
                    EditorAxis = p.EditorAxis,
                    EditorDeadzone = p.EditorDeadzone,
                    EditorRate = p.EditorRate,
                    EditorInvertX = p.EditorInvertX,
                    EditorInvertY = p.EditorInvertY,
                    EditorZoomEnabled = p.EditorZoomEnabled,
                    PanStyle = p.PanStyle,
                    RotateStyle = p.RotateStyle,
                    LockHorizon = p.LockHorizon,
                    AxisBindings = p.AxisBindings.ToDictionary(kv => kv.Key, kv => new AxisBinding
                    {
                        PositiveAction = kv.Value.PositiveAction,
                        NegativeAction = kv.Value.NegativeAction,
                        NeutralAction = kv.Value.NeutralAction,
                        PositiveValue = kv.Value.PositiveValue,
                        NegativeValue = kv.Value.NegativeValue,
                        NeutralValue = kv.Value.NeutralValue,
                        Deadzone = kv.Value.Deadzone,
                        Rate = kv.Value.Rate,
                        Repeat = kv.Value.Repeat
                    }, StringComparer.OrdinalIgnoreCase),
                    Buttons = p.Buttons.ToDictionary(kv => kv.Key, kv => Clone(kv.Value))
                };
            return new ProfileSettings();
        }
    }

    public void ReplaceSettings(BridgeSettings settings)
    {
        lock (_gate) Settings = settings;
        SettingsStore.Save(Settings);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StartScreensaver(string name)
    {
        lock (_gate)
        {
            ScreensaverName = string.IsNullOrWhiteSpace(name) ? Settings.DefaultScreensaver : name;
            ScreensaverActive = true;
        }
        ScreensaverChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StopScreensaver()
    {
        bool changed;
        lock (_gate)
        {
            changed = ScreensaverActive;
            ScreensaverActive = false;
        }
        if (changed) ScreensaverChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Transforms motion only. Button mapping is handled separately.</summary>
    public SpacePilotState TransformState(SpacePilotState source)
    {
        BridgeSettings s;
        lock (_gate) s = Settings;

        double tx = source.Tx * s.Sensitivity;
        double ty = source.Ty * s.Sensitivity;
        double tz = source.Tz * s.Sensitivity;
        double rx = s.RotationLocked ? 0 : source.Rx * s.Sensitivity;
        double ry = s.RotationLocked ? 0 : source.Ry * s.Sensitivity;
        double rz = s.RotationLocked ? 0 : source.Rz * s.Sensitivity;

        // HID convention: Ry is roll. Per-profile horizon lock suppresses only
        // that native rotational axis while keeping pitch/yaw available.
        bool lockHorizon = false;
        lock (_gate)
        {
            if (Settings.Profiles.TryGetValue(_profile, out var activeProfile))
                lockHorizon = activeProfile.LockHorizon == true;
        }
        if (lockHorizon) ry = 0;

        if (s.DominantMode)
        {
            double[] values = { tx, ty, tz, rx, ry, rz };
            int best = 0;
            for (int i = 1; i < values.Length; i++)
                if (Math.Abs(values[i]) > Math.Abs(values[best])) best = i;
            for (int i = 0; i < values.Length; i++)
                if (i != best) values[i] = 0;
            (tx, ty, tz, rx, ry, rz) = (values[0], values[1], values[2], values[3], values[4], values[5]);
        }

        return source with { Tx = tx, Ty = ty, Tz = tz, Rx = rx, Ry = ry, Rz = rz };
    }

    public uint MapVirtualButtons(IReadOnlyCollection<int> sourceButtons)
    {
        string profile;
        bool suppressEsc;
        lock (_gate)
        {
            profile = _profile;
            suppressEsc = _suppressEscUntilRelease;
        }

        uint bits = 0;
        foreach (int raw in sourceButtons)
        {
            if (raw == 10 && suppressEsc) continue;
            ButtonBinding binding = ResolveBinding(profile, raw);
            string mode = binding.Mode.Trim();

            // Left/Right use delayed short-press / long-press handling when left at
            // their default mapping. This prevents a Right view flash before a long
            // hold resolves to Back view. Custom mappings are not intercepted.
            if ((raw == 7 || raw == 8) && mode.Equals("Default", StringComparison.OrdinalIgnoreCase))
                continue;

            if (mode.Equals("Disabled", StringComparison.OrdinalIgnoreCase) ||
                mode.Equals("Shortcut", StringComparison.OrdinalIgnoreCase) ||
                mode.Equals("BuiltIn", StringComparison.OrdinalIgnoreCase))
                continue;

            int? target = null;
            if (mode.Equals("Virtual", StringComparison.OrdinalIgnoreCase))
            {
                if (VirtualSpaceMouseButtons.ByName.TryGetValue(binding.Value, out int customBit))
                    target = customBit;
                else if (int.TryParse(binding.Value, out customBit) && customBit is >= 0 and < 32)
                    target = customBit;
            }
            else // Default / Inherit fallback
            {
                target = SpacePilotButtons.GetDefaultVirtualBit(raw);
            }

            if (target is >= 0 and < 32) bits |= 1u << target.Value;
        }
        lock (_gate)
        {
            if (_virtualPulseUntilUtc > DateTime.UtcNow)
                bits |= _virtualPulseBits;
            else
                _virtualPulseBits = 0;
        }
        return bits;
    }

    public ButtonBinding ResolveBinding(string profile, int raw)
    {
        lock (_gate)
        {
            if (!profile.Equals("global", StringComparison.OrdinalIgnoreCase) &&
                Settings.Profiles.TryGetValue(profile, out var p) &&
                p.Buttons.TryGetValue(raw, out var appBinding) && !appBinding.IsEmpty)
                return Clone(appBinding);

            if (Settings.Profiles.TryGetValue("global", out var global) &&
                global.Buttons.TryGetValue(raw, out var globalBinding) && !globalBinding.IsEmpty)
                return Clone(globalBinding);
        }
        return ButtonBinding.Default();
    }

    private void HubOnStateChanged(object? sender, SpacePilotState state)
    {
        var current = state.Buttons.ToHashSet();
        int[] pressed;
        int[] released;
        bool escReleased;
        bool activity = Math.Abs(state.Tx) > 0.03 || Math.Abs(state.Ty) > 0.03 || Math.Abs(state.Tz) > 0.03 ||
                        Math.Abs(state.Rx) > 0.03 || Math.Abs(state.Ry) > 0.03 || Math.Abs(state.Rz) > 0.03;
        lock (_gate)
        {
            pressed = current.Except(_previous).ToArray();
            released = _previous.Except(current).ToArray();
            escReleased = _suppressEscUntilRelease && !current.Contains(10);
            if (pressed.Length > 0 || !_previous.SetEquals(current)) activity = true;
            _previous = current;
            if (escReleased) _suppressEscUntilRelease = false;
            if (activity) _lastActivityUtc = DateTime.UtcNow;
        }

        if (activity) ActivityDetected?.Invoke(this, EventArgs.Empty);

        foreach (int raw in pressed)
        {
            PhysicalButtonPressed?.Invoke(raw);
            if (ShouldUseViewHold(raw))
                StartViewHold(raw);
            else
                HandlePress(raw);
        }

        foreach (int raw in released)
            if (raw == 7 || raw == 8) FinishViewHold(raw);
    }

    private bool ShouldUseViewHold(int raw)
    {
        if (raw is not (7 or 8)) return false;
        string profile;
        lock (_gate) profile = _profile;
        return ResolveBinding(profile, raw).Mode.Equals("Default", StringComparison.OrdinalIgnoreCase);
    }

    private void StartViewHold(int raw)
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_viewHoldTokens.Remove(raw, out var old)) { old.Cancel(); old.Dispose(); }
            cts = new CancellationTokenSource();
            _viewHoldTokens[raw] = cts;
            _viewLongFired.Remove(raw);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                int holdMs; string backCarrier;
                lock (_gate) { holdMs = Settings.ViewHoldMilliseconds; backCarrier = Settings.BackViewCarrier; }
                await Task.Delay(holdMs, cts.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    if (!_previous.Contains(raw) || cts.IsCancellationRequested) return;
                    _viewLongFired.Add(raw);
                }
                PulseVirtualButton(backCarrier);
                ToastRequested?.Invoke($"Back view carrier -> {backCarrier}");
            }
            catch (OperationCanceledException) { }
        });
    }

    private void FinishViewHold(int raw)
    {
        bool longFired;
        lock (_gate)
        {
            if (_viewHoldTokens.Remove(raw, out var cts)) { cts.Cancel(); cts.Dispose(); }
            longFired = _viewLongFired.Remove(raw);
        }
        if (!longFired)
        {
            string target;
            if (raw == 7)
            {
                lock (_gate) target = Settings.LeftViewCarrier;
                PulseVirtualButton(target);
                ToastRequested?.Invoke($"Left view carrier -> {target}");
            }
            else
            {
                target = "Right";
                PulseVirtualButton(target);
                ToastRequested?.Invoke("Right view");
            }
        }
    }

    private void PulseVirtualButton(string virtualName)
    {
        if (!VirtualSpaceMouseButtons.ByName.TryGetValue(virtualName, out int bit)) return;
        lock (_gate)
        {
            _virtualPulseBits = 1u << bit;
            _virtualPulseUntilUtc = DateTime.UtcNow.AddMilliseconds(ViewPulseMilliseconds);
        }
    }

    private void HandlePress(int raw)
    {
        if (raw == 10)
        {
            bool wasScreensaver;
            lock (_gate)
            {
                wasScreensaver = ScreensaverActive;
                if (wasScreensaver)
                {
                    ScreensaverActive = false;
                    _suppressEscUntilRelease = true;
                }
            }
            if (wasScreensaver)
            {
                ScreensaverChanged?.Invoke(this, EventArgs.Empty);
                ToastRequested?.Invoke("LCD screensaver stopped");
                return;
            }
        }

        string profile;
        lock (_gate) profile = _profile;
        ButtonBinding binding = ResolveBinding(profile, raw);

        if (binding.Mode.Equals("Shortcut", StringComparison.OrdinalIgnoreCase))
        {
            if (ShortcutSender.Send(binding.Value))
                ToastRequested?.Invoke($"{SpacePilotButtons.GetName(raw)} -> {binding.Value}");
            return;
        }

        if (binding.Mode.Equals("BuiltIn", StringComparison.OrdinalIgnoreCase))
            RunBuiltIn(binding.Value);
    }

    public void ExecuteBuiltIn(string action) => RunBuiltIn(action);

    private void RunBuiltIn(string action)
    {
        BridgeSettings settings;
        lock (_gate) settings = Settings;
        string a = (action ?? string.Empty).Trim();
        if (a.StartsWith("StartScreensaver:", StringComparison.OrdinalIgnoreCase))
        {
            StartScreensaver(a.Substring("StartScreensaver:".Length).Trim());
            return;
        }
        if (a.StartsWith("SetLedPattern:", StringComparison.OrdinalIgnoreCase))
        {
            settings.LedPattern = a.Substring("SetLedPattern:".Length).Trim();
            SaveAndNotify($"LED {settings.LedPattern}");
            return;
        }
        switch (a.ToLowerInvariant())
        {
            case "increasesensitivity":
                settings.Sensitivity = Math.Clamp(Math.Round(settings.Sensitivity + settings.SensitivityStep, 2), 0.10, 3.00);
                SaveAndNotify($"Speed {settings.Sensitivity:0.00}x");
                break;
            case "decreasesensitivity":
                settings.Sensitivity = Math.Clamp(Math.Round(settings.Sensitivity - settings.SensitivityStep, 2), 0.10, 3.00);
                SaveAndNotify($"Speed {settings.Sensitivity:0.00}x");
                break;
            case "toggledominant":
                settings.DominantMode = !settings.DominantMode;
                SaveAndNotify($"Dominant {(settings.DominantMode ? "ON" : "OFF")}");
                break;
            case "togglerotationlock":
                settings.RotationLocked = !settings.RotationLocked;
                SaveAndNotify($"Rotation {(settings.RotationLocked ? "LOCKED" : "FREE")}");
                break;
            case "opensettings":
                OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
                break;
            case "startstarfield": StartScreensaver("Starfield"); break;
            case "startmatrix": StartScreensaver("Matrix"); break;
            case "startbounce": StartScreensaver("Bounce"); break;
            case "startwarptunnel": StartScreensaver("Warp Tunnel"); break;
            case "startpong": StartScreensaver("Pong"); break;
            case "startorbit": StartScreensaver("Orbit"); break;
            case "startwave": StartScreensaver("Wave"); break;
            case "startrain": StartScreensaver("Digital Rain"); break;
            case "startscanner": StartScreensaver("Scanner"); break;
            case "startclock": StartScreensaver("Clock"); break;
            case "stopscreensaver": StopScreensaver(); break;
            case "recoverlcd": LcdRecoverRequested?.Invoke(this, EventArgs.Empty); break;
            case "ledon": settings.LedPattern = "On"; SaveAndNotify("LED ON"); break;
            case "ledoff": settings.LedPattern = "Off"; SaveAndNotify("LED OFF"); break;
            case "toggleled":
                settings.LedPattern = settings.LedPattern.Equals("Off", StringComparison.OrdinalIgnoreCase) ? "On" : "Off";
                SaveAndNotify($"LED {settings.LedPattern.ToUpperInvariant()}");
                break;
            case "cycleledpattern":
            {
                string[] patterns = { "On", "Activity", "Slow Blink", "Heartbeat", "Breathe", "Screensaver Pulse", "Off" };
                int idx = Array.FindIndex(patterns, x => x.Equals(settings.LedPattern, StringComparison.OrdinalIgnoreCase));
                settings.LedPattern = patterns[(idx + 1 + patterns.Length) % patterns.Length];
                SaveAndNotify($"LED {settings.LedPattern}");
                break;
            }
        }
    }

    private void SaveAndNotify(string message)
    {
        SettingsStore.Save(Settings);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        ToastRequested?.Invoke(message);
    }

    private static ButtonBinding Clone(ButtonBinding b) => new() { Mode = b.Mode, Value = b.Value };

    public void Dispose()
    {
        _hub.StateChanged -= HubOnStateChanged;
        lock (_gate)
        {
            foreach (var cts in _viewHoldTokens.Values) { cts.Cancel(); cts.Dispose(); }
            _viewHoldTokens.Clear();
        }
    }
}
