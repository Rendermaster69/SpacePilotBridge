using System.Text.Json.Serialization;

namespace SpacePilotBridge.App;

internal sealed class BridgeSettings
{
    public double Sensitivity { get; set; } = 1.0;
    public double SensitivityStep { get; set; } = 0.10;
    public bool DominantMode { get; set; }
    public bool RotationLocked { get; set; }

    public string DefaultScreensaver { get; set; } = "Starfield";
    public bool ScreensaverPlaylistEnabled { get; set; }
    public int ScreensaverPlaylistSeconds { get; set; } = 90;
    public bool ScreensaverPlaylistShuffle { get; set; }
    public List<string> ScreensaverPlaylist { get; set; } = new();

    public bool IdleScreensaverEnabled { get; set; } = true;
    public int IdleScreensaverSeconds { get; set; } = 60;

    public string LcdDitherMode { get; set; } = "Threshold";
    public int LcdFullRefreshFrames { get; set; } = 12;
    public int LcdReportDelayMilliseconds { get; set; } = 3;
    public bool LcdForceFullAnimationFrames { get; set; } = true;
    public List<string> LcdWidgets { get; set; } = new() { "Speed", "Profile", "Now Playing", "Latency" };

    public string LedPattern { get; set; } = "On";
    public bool StartMinimized { get; set; } = true;
    public bool SilentMode { get; set; } = true;
    public bool AutoStart { get; set; } = true;
    public bool GamepadEnabled { get; set; }

    public string LeftViewCarrier { get; set; } = "Roll CW";
    public string BackViewCarrier { get; set; } = "Menu";
    public int ViewHoldMilliseconds { get; set; } = 550;

    public Dictionary<string, ProfileSettings> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> AppProfileMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static BridgeSettings CreateDefaults()
    {
        var settings = new BridgeSettings();
        var global = new ProfileSettings { DisplayName = "Global", EditorControlsEnabled = false };
        global.Buttons[16] = ButtonBinding.BuiltIn("IncreaseSensitivity");
        global.Buttons[17] = ButtonBinding.BuiltIn("DecreaseSensitivity");
        global.Buttons[18] = ButtonBinding.BuiltIn("ToggleDominant");
        global.Buttons[20] = ButtonBinding.BuiltIn("OpenSettings");
        settings.Profiles["global"] = global;

        settings.Profiles["fusion"] = new ProfileSettings { DisplayName = "Fusion 360", EditorControlsEnabled = false, LockHorizon = true };
        settings.Profiles["fusion"].Buttons[15] = ButtonBinding.Disabled();
        settings.Profiles["maya"] = new ProfileSettings { DisplayName = "Maya", EditorControlsEnabled = false };
        settings.Profiles["zbrush"] = new ProfileSettings { DisplayName = "ZBrush", EditorControlsEnabled = false };

        var resolve = new ProfileSettings { DisplayName = "DaVinci Resolve", EditorMode = "Custom Axes", EditorControlsEnabled = true };
        // Top = play/pause.
        resolve.Buttons[6] = ButtonBinding.Shortcut("SPACE");
        resolve.AxisBindings["Rz"] = AxisBinding.Pair("Resolve Forward", "Resolve Reverse", "Resolve Stop", 0.12, 1.0);
        resolve.AxisBindings["Tx"] = AxisBinding.Pair("Frame Next", "Frame Previous", "None", 0.16, 1.0);
        resolve.AxisBindings["Ty"] = AxisBinding.Pair("Go To Out", "Go To In", "None", 0.22, 1.0, repeat: false);
        settings.Profiles["resolve"] = resolve;

        var ableton = new ProfileSettings { DisplayName = "Ableton Live", EditorMode = "Custom Axes", EditorControlsEnabled = true };
        ableton.AxisBindings["Rz"] = AxisBinding.Pair("Horizontal Scroll +", "Horizontal Scroll -", "None", 0.12, 1.0);
        settings.Profiles["ableton"] = ableton;

        var illustrator = new ProfileSettings { DisplayName = "Adobe Illustrator", EditorMode = "Custom Axes", EditorControlsEnabled = true, PanStyle = "Pointerless", RotateStyle = "None" };
        illustrator.AxisBindings["Tx"] = AxisBinding.Pair("Horizontal Scroll +", "Horizontal Scroll -", "None", 0.10, 1.0);
        illustrator.AxisBindings["Ty"] = AxisBinding.Pair("Vertical Scroll -", "Vertical Scroll +", "None", 0.10, 1.0);
        illustrator.AxisBindings["Tz"] = AxisBinding.Pair("Zoom Out", "Zoom In", "None", 0.12, 1.0);
        settings.Profiles["illustrator"] = illustrator;

        var photoshop = new ProfileSettings { DisplayName = "Adobe Photoshop", EditorMode = "Custom Axes", EditorControlsEnabled = true, PanStyle = "Pointerless", RotateStyle = "None" };
        photoshop.AxisBindings["Tx"] = AxisBinding.Pair("Horizontal Scroll +", "Horizontal Scroll -", "None", 0.10, 1.0);
        photoshop.AxisBindings["Ty"] = AxisBinding.Pair("Vertical Scroll -", "Vertical Scroll +", "None", 0.10, 1.0);
        photoshop.AxisBindings["Tz"] = AxisBinding.Pair("Zoom Out", "Zoom In", "None", 0.12, 1.0);
        settings.Profiles["photoshop"] = photoshop;

        settings.AppProfileMappings["resolve"] = "resolve";
        settings.AppProfileMappings["maya"] = "maya";
        settings.AppProfileMappings["zbrush"] = "zbrush";
        settings.AppProfileMappings["fusion360"] = "fusion";
        settings.AppProfileMappings["illustrator"] = "illustrator";
        settings.AppProfileMappings["photoshop"] = "photoshop";
        return settings;
    }
}

internal sealed class ProfileSettings
{
    public string DisplayName { get; set; } = string.Empty;
    public Dictionary<int, ButtonBinding> Buttons { get; set; } = new();

    // Hard gate for mouse/keyboard/editor output. Native CAD/3D profiles keep this false.
    public bool? EditorControlsEnabled { get; set; }

    // Legacy modes are still accepted for old settings files. New profiles use Custom Axes.
    public string EditorMode { get; set; } = "Off";
    public string EditorAxis { get; set; } = "Rz";
    public double EditorDeadzone { get; set; } = 0.12;
    public double EditorRate { get; set; } = 1.0;
    public bool EditorInvertX { get; set; }
    public bool EditorInvertY { get; set; }
    public bool EditorZoomEnabled { get; set; } = true;

    /// <summary>MMB or Space+LMB for Pan actions.</summary>
    public string PanStyle { get; set; } = "MMB";
    /// <summary>None, Photoshop or Illustrator for Rotate CW/CCW actions.</summary>
    public string RotateStyle { get; set; } = "None";
    public Dictionary<string, AxisBinding> AxisBindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Suppress HID Ry, which is the SpaceMouse roll axis, to keep the camera horizon level.</summary>
    public bool? LockHorizon { get; set; }
}

internal sealed class AxisBinding
{
    public string PositiveAction { get; set; } = "None";
    public string NegativeAction { get; set; } = "None";
    public string NeutralAction { get; set; } = "None";
    public string PositiveValue { get; set; } = string.Empty;
    public string NegativeValue { get; set; } = string.Empty;
    public string NeutralValue { get; set; } = string.Empty;
    public double Deadzone { get; set; } = 0.12;
    public double Rate { get; set; } = 1.0;
    public bool Repeat { get; set; } = true;

    public static AxisBinding Pair(string positive, string negative, string neutral = "None", double deadzone = 0.12, double rate = 1.0, bool repeat = true) =>
        new() { PositiveAction = positive, NegativeAction = negative, NeutralAction = neutral, Deadzone = deadzone, Rate = rate, Repeat = repeat };
}

internal sealed class ButtonBinding
{
    /// <summary>Inherit, Default, Virtual, Shortcut, BuiltIn or Disabled.</summary>
    public string Mode { get; set; } = "Inherit";
    public string Value { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(Mode) || Mode.Equals("Inherit", StringComparison.OrdinalIgnoreCase);

    public static ButtonBinding Default() => new() { Mode = "Default" };
    public static ButtonBinding Virtual(string target) => new() { Mode = "Virtual", Value = target };
    public static ButtonBinding Shortcut(string shortcut) => new() { Mode = "Shortcut", Value = shortcut };
    public static ButtonBinding BuiltIn(string action) => new() { Mode = "BuiltIn", Value = action };
    public static ButtonBinding Disabled() => new() { Mode = "Disabled" };
    public static ButtonBinding Inherit() => new() { Mode = "Inherit" };
}
