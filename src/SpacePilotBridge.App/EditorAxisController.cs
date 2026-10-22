using System.Runtime.InteropServices;
using SpacePilotBridge.Core;

namespace SpacePilotBridge.App;

/// <summary>
/// Per-app 2D/editor controller. v0.9 supports six independently configurable
/// SpacePilot axes. Native SpaceMouse motion is still sent separately for CAD/3D apps.
/// </summary>
internal sealed class EditorAxisController : IDisposable
{
    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_HWHEEL = 0x1000;

    private static readonly string[] AxisNames = { "Tx", "Ty", "Tz", "Rx", "Ry", "Rz" };

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public nuint dwExtraInfo;
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);

    private readonly SpacePilotHub _hub;
    private readonly ButtonActionEngine _actions;
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _lastDirections = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _lastEmits = new(StringComparer.OrdinalIgnoreCase);
    private bool _panActive;
    private bool _panUsesSpace;
    private bool _rotateActive;
    private string _rotateStyle = "None";
    private bool _disposed;
    private volatile bool _enabled = true;
    private long _emitCount;
    private volatile string _lastAction = "-";
    private volatile bool _lastSendSucceeded = true;

    public long EmitCount => Interlocked.Read(ref _emitCount);
    public string LastAction => _lastAction;
    public bool LastSendSucceeded => _lastSendSucceeded;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value)
            {
                lock (_gate)
                {
                    EndPanUnsafe();
                    EndRotateUnsafe();
                    _lastDirections.Clear();
                }
            }
        }
    }

    public EditorAxisController(SpacePilotHub hub, ButtonActionEngine actions)
    {
        _hub = hub; _actions = actions; _hub.StateChanged += OnStateChanged;
    }

    private void OnStateChanged(object? sender, SpacePilotState state)
    {
        if (_disposed || !_enabled) return;
        ProfileSettings profile = _actions.GetCurrentProfileSettings();
        lock (_gate)
        {
            // Never emit mouse/keyboard editing gestures unless this exact profile
            // explicitly opts in. This prevents a stale/custom mapping from panning
            // unrelated applications.
            if (profile.EditorControlsEnabled != true)
            {
                EndPanUnsafe();
                EndRotateUnsafe();
                _lastDirections.Clear();
                return;
            }
            if (profile.AxisBindings.Count > 0 || profile.EditorMode.Equals("Custom Axes", StringComparison.OrdinalIgnoreCase))
            {
                HandleCustomAxesUnsafe(state, profile);
                return;
            }
            HandleLegacyUnsafe(state, profile);
        }
    }

    private void HandleCustomAxesUnsafe(SpacePilotState state, ProfileSettings profile)
    {
        DateTime now = DateTime.UtcNow;

        foreach (string axisName in AxisNames)
        {
            if (!profile.AxisBindings.TryGetValue(axisName, out AxisBinding? binding)) continue;
            double raw = GetAxis(state, axisName);
            double deadzone = Math.Clamp(binding.Deadzone, 0.02, 0.80);
            double value = ApplyDeadzone(raw, deadzone);
            int dir = Math.Sign(value);
            int previous = _lastDirections.TryGetValue(axisName, out int p) ? p : 0;
            string action = dir > 0 ? binding.PositiveAction : dir < 0 ? binding.NegativeAction : binding.NeutralAction;
            string customValue = dir > 0 ? binding.PositiveValue : dir < 0 ? binding.NegativeValue : binding.NeutralValue;
            double magnitude = Math.Abs(value);
            double rate = Math.Clamp(binding.Rate, 0.1, 5.0);

            // v0.9.2 safety rule: the bridge never moves the Windows pointer.
            // Legacy Pan actions are converted to wheel scrolling. Rotate actions
            // require an explicit custom shortcut because Adobe's stock rotate
            // tools are drag-based.
            if (action.StartsWith("Pan ", StringComparison.OrdinalIgnoreCase) && dir != 0)
            {
                string pointerless = action.Contains("Right", StringComparison.OrdinalIgnoreCase) ? "Horizontal Scroll +"
                    : action.Contains("Left", StringComparison.OrdinalIgnoreCase) ? "Horizontal Scroll -"
                    : action.Contains("Down", StringComparison.OrdinalIgnoreCase) ? "Vertical Scroll -"
                    : "Vertical Scroll +";
                HandleDiscreteActionUnsafe(axisName, pointerless, string.Empty, dir, previous, magnitude, rate, binding.Repeat, now);
            }
            else if (action.StartsWith("Rotate ", StringComparison.OrdinalIgnoreCase))
            {
                if (dir != 0 && !string.IsNullOrWhiteSpace(customValue))
                    HandleDiscreteActionUnsafe(axisName, "Shortcut", customValue, dir, previous, magnitude, rate, binding.Repeat, now);
            }
            else
            {
                HandleDiscreteActionUnsafe(axisName, action, customValue, dir, previous, magnitude, rate, binding.Repeat, now);
            }

            _lastDirections[axisName] = dir;
        }

        // Release anything left over from pre-v0.9.2 drag modes.
        EndPanUnsafe();
        EndRotateUnsafe();
    }

    private void HandleDiscreteActionUnsafe(string axis, string action, string customValue, int dir, int previous, double magnitude, double rate, bool repeat, DateTime now)
    {
        action = (action ?? "None").Trim();
        if (action.Equals("None", StringComparison.OrdinalIgnoreCase)) return;

        bool edge = dir != previous;
        int interval = (int)Math.Clamp(150.0 / Math.Max(0.15, magnitude * rate), 25, 350);
        DateTime last = _lastEmits.TryGetValue(axis, out DateTime l) ? l : DateTime.MinValue;
        bool due = edge || (repeat && dir != 0 && (now - last).TotalMilliseconds >= interval);

        if (dir == 0)
        {
            if (edge && !action.Equals("None", StringComparison.OrdinalIgnoreCase)) EmitAction(action, customValue);
            return;
        }
        if (!due) return;

        EmitAction(action, customValue);
        _lastEmits[axis] = now;
    }

    private void EmitAction(string action, string customValue)
    {
        bool ok = true;
        switch (action.Trim().ToLowerInvariant())
        {
            case "resolve forward": ok = EmitShortcut("L"); break;
            case "resolve reverse": ok = EmitShortcut("J"); break;
            case "resolve stop": ok = EmitShortcut("K"); break;
            case "frame next": ok = EmitShortcut("RIGHT"); break;
            case "frame previous": ok = EmitShortcut("LEFT"); break;
            case "go to in": ok = EmitShortcut("SHIFT+I"); break;
            case "go to out": ok = EmitShortcut("SHIFT+O"); break;
            case "zoom in": ok = EmitWheel(+1, false); break;
            case "zoom out": ok = EmitWheel(-1, false); break;
            case "horizontal scroll +": ok = EmitWheel(+1, true); break;
            case "horizontal scroll -": ok = EmitWheel(-1, true); break;
            case "vertical scroll +": ok = EmitWheel(+1, false); break;
            case "vertical scroll -": ok = EmitWheel(-1, false); break;
            case "shortcut": ok = EmitShortcut(customValue); break;
            default: return;
        }
        _lastSendSucceeded = ok;
    }

    private void HandleLegacyUnsafe(SpacePilotState state, ProfileSettings profile)
    {
        string mode = profile.EditorMode ?? "Off";
        if (mode.StartsWith("2D Pan", StringComparison.OrdinalIgnoreCase))
        {
            HandleLegacy2DPanUnsafe(state, profile, mode); return;
        }
        EndPanUnsafe(); EndRotateUnsafe();
        if (mode.Equals("Off", StringComparison.OrdinalIgnoreCase)) return;

        double axis = GetAxis(state, profile.EditorAxis);
        double deadzone = Math.Clamp(profile.EditorDeadzone, 0.02, 0.80);
        int direction = axis > deadzone ? 1 : axis < -deadzone ? -1 : 0;
        double magnitude = Math.Clamp((Math.Abs(axis) - deadzone) / Math.Max(0.01, 1.0 - deadzone), 0, 1);
        double rate = Math.Clamp(profile.EditorRate, 0.1, 5.0);
        DateTime now = DateTime.UtcNow;
        int previous = _lastDirections.TryGetValue("legacy", out int p) ? p : 0;
        int interval = (int)Math.Clamp(140.0 / Math.Max(0.15, magnitude * rate), 18, 300);
        DateTime last = _lastEmits.TryGetValue("legacy", out DateTime l) ? l : DateTime.MinValue;

        if (mode.Equals("Resolve Jog", StringComparison.OrdinalIgnoreCase))
        {
            if (direction == 0)
            {
                if (previous != 0) EmitShortcut("K");
            }
            else if (magnitude < 0.35)
            {
                if ((now-last).TotalMilliseconds >= interval) { EmitShortcut(direction > 0 ? "RIGHT" : "LEFT"); _lastEmits["legacy"] = now; }
            }
            else if (direction != previous || (now-last).TotalMilliseconds >= 500)
            {
                EmitShortcut(direction > 0 ? "L" : "J"); _lastEmits["legacy"] = now;
            }
        }
        else if (direction != 0 && (now-last).TotalMilliseconds >= interval)
        {
            if (mode.Equals("Frame Jog", StringComparison.OrdinalIgnoreCase)) EmitShortcut(direction > 0 ? "RIGHT" : "LEFT");
            else if (mode.Equals("Horizontal Scroll", StringComparison.OrdinalIgnoreCase)) EmitWheel(direction, true);
            else if (mode.Equals("Vertical Scroll", StringComparison.OrdinalIgnoreCase)) EmitWheel(direction, false);
            _lastEmits["legacy"] = now;
        }
        _lastDirections["legacy"] = direction;
    }

    private void HandleLegacy2DPanUnsafe(SpacePilotState state, ProfileSettings profile, string mode)
    {
        double dz = Math.Clamp(profile.EditorDeadzone, 0.02, 0.80);
        double rate = Math.Clamp(profile.EditorRate, 0.1, 5.0);
        double x = ApplyDeadzone(state.Tx, dz) * (profile.EditorInvertX ? -1 : 1);
        double y = ApplyDeadzone(state.Ty, dz) * (profile.EditorInvertY ? 1 : -1);
        DateTime now = DateTime.UtcNow;

        EmitPointerlessContinuous("legacy-x", x, rate, true, now);
        EmitPointerlessContinuous("legacy-y", -y, rate, false, now);

        if (profile.EditorZoomEnabled)
        {
            double z = ApplyDeadzone(state.Tz, dz);
            EmitPointerlessContinuous("legacy-z", -z, rate, false, now);
        }

        EndPanUnsafe();
        EndRotateUnsafe();
    }

    private void EmitPointerlessContinuous(string key, double value, double rate, bool horizontal, DateTime now)
    {
        int dir = Math.Sign(value);
        int previous = _lastDirections.TryGetValue(key, out int p) ? p : 0;
        if (dir == 0) { _lastDirections[key] = 0; return; }
        double magnitude = Math.Abs(value);
        int interval = (int)Math.Clamp(150.0 / Math.Max(0.15, magnitude * rate), 30, 350);
        DateTime last = _lastEmits.TryGetValue(key, out DateTime l) ? l : DateTime.MinValue;
        if (dir != previous || (now - last).TotalMilliseconds >= interval)
        {
            EmitWheel(dir, horizontal);
            _lastEmits[key] = now;
        }
        _lastDirections[key] = dir;
    }

    private void BeginPanUnsafe(bool useSpace)
    {
        // Intentionally disabled: SpacePilotBridge never moves or drags the OS cursor.
        EndPanUnsafe();
    }

    private void EndPanUnsafe()
    {
        if (!_panActive) return;
        bool ok;
        if (_panUsesSpace)
        {
            bool mouse = SendMouse(0,0,0,MOUSEEVENTF_LEFTUP);
            bool key = ShortcutSender.SetKeyState("SPACE", false); ok = mouse && key;
        }
        else ok = SendMouse(0,0,0,MOUSEEVENTF_MIDDLEUP);
        _panActive=false; _panUsesSpace=false; RecordEmit("Pan STOP", ok);
    }

    private void BeginRotateUnsafe(string style)
    {
        // Intentionally disabled: Adobe rotate-view tools require pointer dragging.
        // Use a custom Shortcut action if you have assigned keyboard commands.
        EndRotateUnsafe();
    }

    private void EndRotateUnsafe()
    {
        if (!_rotateActive) return;
        bool ok = SendMouse(0,0,0,MOUSEEVENTF_LEFTUP);
        if (_rotateStyle.Equals("Photoshop", StringComparison.OrdinalIgnoreCase)) ok &= ShortcutSender.SetKeyState("R", false);
        else if (_rotateStyle.Equals("Illustrator", StringComparison.OrdinalIgnoreCase))
        {
            ok &= ShortcutSender.SetKeyState("H", false);
            ok &= ShortcutSender.SetKeyState("SHIFT", false);
        }
        _rotateActive=false; _rotateStyle="None"; RecordEmit("Rotate STOP", ok);
    }

    private static double ApplyDeadzone(double value, double deadzone)
    {
        if (Math.Abs(value) <= deadzone) return 0;
        double n=(Math.Abs(value)-deadzone)/Math.Max(0.01,1.0-deadzone);
        return Math.Sign(value)*Math.Clamp(n,0,1);
    }

    private static int SignedPixels(double value, double rate)
    {
        if (Math.Abs(value) < 0.001) return 0;
        double curved=Math.Sign(value)*Math.Pow(Math.Abs(value),1.25);
        int px=(int)Math.Round(curved*(4+22*rate));
        if(px==0)px=Math.Sign(value); return Math.Clamp(px,-80,80);
    }

    private static double GetAxis(SpacePilotState s,string? axis)=>(axis??"Rz").ToUpperInvariant() switch
    { "TX"=>s.Tx,"TY"=>s.Ty,"TZ"=>s.Tz,"RX"=>s.Rx,"RY"=>s.Ry,_=>s.Rz };

    private bool EmitShortcut(string shortcut)
    {
        bool ok=ShortcutSender.Send(shortcut); RecordEmit(ok?shortcut:$"{shortcut} ERR={ShortcutSender.LastWin32Error}",ok); return ok;
    }
    private bool EmitWheel(int clicks,bool horizontal)
    {
        bool ok=SendWheel(clicks,horizontal); RecordEmit($"{(horizontal?"HWheel":"Wheel")} {clicks:+#;-#;0}",ok); return ok;
    }
    private void RecordEmit(string action,bool ok){_lastAction=action;_lastSendSucceeded=ok;Interlocked.Increment(ref _emitCount);}
    private static bool SendWheel(int clicks,bool horizontal)=>SendMouse(0,0,unchecked((uint)(clicks*120)),horizontal?MOUSEEVENTF_HWHEEL:MOUSEEVENTF_WHEEL);
    private static bool SendMouse(int dx,int dy,uint mouseData,uint flags)
    {
        // Hard safety barrier: this application is never allowed to move the OS
        // pointer or hold mouse buttons. Wheel/HWheel reports are the only mouse
        // class input allowed, and they leave the cursor coordinates untouched.
        uint allowed = MOUSEEVENTF_WHEEL | MOUSEEVENTF_HWHEEL;
        if ((flags & ~allowed) != 0 || dx != 0 || dy != 0) return false;
        var input=new INPUT{type=INPUT_MOUSE,U=new InputUnion{mi=new MOUSEINPUT{dx=0,dy=0,mouseData=mouseData,dwFlags=flags}}};
        return SendInput(1,new[]{input},Marshal.SizeOf<INPUT>())==1;
    }


    public void ResetOutputState()
    {
        lock (_gate)
        {
            EndPanUnsafe();
            EndRotateUnsafe();
            _lastDirections.Clear();
            _lastEmits.Clear();
        }
    }

    public void Dispose()
    {
        if(_disposed)return;_disposed=true;_hub.StateChanged-=OnStateChanged;ResetOutputState();
    }
}
