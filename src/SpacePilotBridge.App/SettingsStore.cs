using SpacePilotBridge.Core;
using System.Text.Json;

namespace SpacePilotBridge.App;

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SpacePilotBridge");

    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");

    public static BridgeSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<BridgeSettings>(File.ReadAllText(FilePath), JsonOptions);
                if (settings is not null) { Normalize(settings); return settings; }
            }
        }
        catch
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                if (File.Exists(FilePath)) File.Copy(FilePath, Path.Combine(DirectoryPath, $"settings-bad-{DateTime.Now:yyyyMMdd-HHmmss}.json"), true);
            }
            catch { }
        }
        var defaults = BridgeSettings.CreateDefaults(); Save(defaults); return defaults;
    }

    public static void Save(BridgeSettings settings)
    {
        Normalize(settings); Directory.CreateDirectory(DirectoryPath);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, FilePath, true);
    }

    public static BridgeSettings Clone(BridgeSettings settings)
    {
        Normalize(settings);
        string json = JsonSerializer.Serialize(settings, JsonOptions);
        var clone = JsonSerializer.Deserialize<BridgeSettings>(json, JsonOptions) ?? BridgeSettings.CreateDefaults();
        Normalize(clone); return clone;
    }

    private static void Normalize(BridgeSettings settings)
    {
        settings.Sensitivity = Math.Clamp(settings.Sensitivity, 0.10, 3.00);
        settings.SensitivityStep = Math.Clamp(settings.SensitivityStep, 0.05, 0.50);
        settings.DefaultScreensaver = string.IsNullOrWhiteSpace(settings.DefaultScreensaver) ? "Starfield" : settings.DefaultScreensaver;
        settings.ScreensaverPlaylistSeconds = Math.Clamp(settings.ScreensaverPlaylistSeconds, 10, 3600);
        settings.IdleScreensaverSeconds = Math.Clamp(settings.IdleScreensaverSeconds, 5, 3600);
        settings.LcdFullRefreshFrames = Math.Clamp(settings.LcdFullRefreshFrames, 1, 120);
        settings.LcdReportDelayMilliseconds = Math.Clamp(settings.LcdReportDelayMilliseconds, 0, 12);
        settings.ViewHoldMilliseconds = Math.Clamp(settings.ViewHoldMilliseconds, 250, 1500);
        if (!VirtualSpaceMouseButtons.ByName.ContainsKey(settings.LeftViewCarrier)) settings.LeftViewCarrier = "Roll CW";
        if (!VirtualSpaceMouseButtons.ByName.ContainsKey(settings.BackViewCarrier)) settings.BackViewCarrier = "Menu";

        settings.ScreensaverPlaylist ??= new List<string>();
        settings.ScreensaverPlaylist = settings.ScreensaverPlaylist.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        string[] ditherModes = { "Threshold", "Bayer 2x2", "Bayer 4x4", "Floyd-Steinberg" };
        if (!ditherModes.Contains(settings.LcdDitherMode, StringComparer.OrdinalIgnoreCase)) settings.LcdDitherMode = "Threshold";

        settings.LcdWidgets ??= new List<string>();
        string[] validWidgets = { "Speed", "Profile", "Latency", "Buttons", "Motion", "Clock", "LED", "Gamepad", "Now Playing" };
        settings.LcdWidgets = settings.LcdWidgets.Where(x => validWidgets.Contains(x, StringComparer.OrdinalIgnoreCase)).Take(4).ToList();
        string[] fallbackWidgets = { "Speed", "Profile", "Now Playing", "Latency" };
        while (settings.LcdWidgets.Count < 4) settings.LcdWidgets.Add(fallbackWidgets[settings.LcdWidgets.Count]);

        string[] ledPatterns = { "Off", "On", "Activity", "Slow Blink", "Fast Blink", "Heartbeat", "Breathe", "Screensaver Pulse" };
        if (string.IsNullOrWhiteSpace(settings.LedPattern) || !ledPatterns.Contains(settings.LedPattern, StringComparer.OrdinalIgnoreCase)) settings.LedPattern = "On";

        settings.Profiles ??= new Dictionary<string, ProfileSettings>(StringComparer.OrdinalIgnoreCase);
        if (settings.Profiles.Comparer != StringComparer.OrdinalIgnoreCase) settings.Profiles = new Dictionary<string, ProfileSettings>(settings.Profiles, StringComparer.OrdinalIgnoreCase);

        BridgeSettings defaults = BridgeSettings.CreateDefaults();
        foreach (var kv in defaults.Profiles)
        {
            if (!settings.Profiles.TryGetValue(kv.Key, out ProfileSettings? current))
            {
                settings.Profiles[kv.Key] = CloneProfile(kv.Value);
                continue;
            }
            if (string.IsNullOrWhiteSpace(current.DisplayName)) current.DisplayName = kv.Value.DisplayName;
            // Migrate the old hard-coded editor presets to the v0.9 configurable axes.
            if (current.AxisBindings is null || current.AxisBindings.Count == 0)
            {
                bool oldPreset = kv.Key switch
                {
                    "resolve" => current.EditorMode.Equals("Resolve Jog", StringComparison.OrdinalIgnoreCase),
                    "ableton" => current.EditorMode.Equals("Horizontal Scroll", StringComparison.OrdinalIgnoreCase),
                    "illustrator" => current.EditorMode.StartsWith("2D Pan", StringComparison.OrdinalIgnoreCase),
                    "photoshop" => current.EditorMode.StartsWith("2D Pan", StringComparison.OrdinalIgnoreCase),
                    _ => false
                };
                if (oldPreset)
                {
                    current.AxisBindings = kv.Value.AxisBindings.ToDictionary(x => x.Key, x => CloneAxis(x.Value), StringComparer.OrdinalIgnoreCase);
                    current.EditorMode = "Custom Axes";
                    current.PanStyle = kv.Value.PanStyle;
                    current.RotateStyle = kv.Value.RotateStyle;
                    current.LockHorizon = kv.Value.LockHorizon;
                    if (kv.Key == "resolve" && !current.Buttons.ContainsKey(6)) current.Buttons[6] = ButtonBinding.Shortcut("SPACE");
                }
            }
        }

        foreach (var kv in settings.Profiles.ToArray())
        {
            string key = string.IsNullOrWhiteSpace(kv.Key) ? "custom" : kv.Key.Trim().ToLowerInvariant();
            ProfileSettings p = kv.Value ?? new ProfileSettings();
            // v0.9.2: editor/mouse output is opt-in per profile. Migrate known 2D/editor
            // profiles and explicit custom profiles, but never auto-enable native 3D profiles.
            bool native3dProfile = key is "global" or "fusion" or "maya" or "zbrush";
            if (key == "fusion" && p.LockHorizon is null) p.LockHorizon = true;
            bool knownEditorProfile = key is "resolve" or "ableton" or "illustrator" or "photoshop";
            bool hasConfiguredEditorActions = p.AxisBindings is not null && p.AxisBindings.Values.Any(a =>
                !string.Equals(a.PositiveAction, "None", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(a.NegativeAction, "None", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(a.NeutralAction, "None", StringComparison.OrdinalIgnoreCase));
            if (p.EditorControlsEnabled is null)
                p.EditorControlsEnabled = !native3dProfile && (knownEditorProfile || hasConfiguredEditorActions || !string.Equals(p.EditorMode, "Off", StringComparison.OrdinalIgnoreCase));
            p.DisplayName = string.IsNullOrWhiteSpace(p.DisplayName) ? ProfileResolver.DisplayName(key) : p.DisplayName.Trim();
            p.Buttons ??= new Dictionary<int, ButtonBinding>();
            p.AxisBindings ??= new Dictionary<string, AxisBinding>(StringComparer.OrdinalIgnoreCase);
            if (p.AxisBindings.Comparer != StringComparer.OrdinalIgnoreCase) p.AxisBindings = new Dictionary<string, AxisBinding>(p.AxisBindings, StringComparer.OrdinalIgnoreCase);
            p.PanStyle = "Pointerless";
            p.RotateStyle = "None";
            p.EditorMode = NormalizeEditorMode(p.EditorMode, p.AxisBindings.Count > 0 ? "Custom Axes" : "Off");
            foreach (var axis in p.AxisBindings.Keys.ToArray())
            {
                if (!new[] { "Tx", "Ty", "Tz", "Rx", "Ry", "Rz" }.Contains(axis, StringComparer.OrdinalIgnoreCase)) { p.AxisBindings.Remove(axis); continue; }
                AxisBinding b = p.AxisBindings[axis] ?? new AxisBinding();
                b.Deadzone = Math.Clamp(b.Deadzone, 0.02, 0.80); b.Rate = Math.Clamp(b.Rate, 0.1, 5.0);
                b.PositiveAction ??= "None"; b.NegativeAction ??= "None"; b.NeutralAction ??= "None";
                b.PositiveValue ??= string.Empty; b.NegativeValue ??= string.Empty; b.NeutralValue ??= string.Empty;
                p.AxisBindings[axis] = b;
            }
            settings.Profiles[key] = p;
            if (!key.Equals(kv.Key, StringComparison.Ordinal)) settings.Profiles.Remove(kv.Key);
        }

        settings.Profiles.TryAdd("global", CloneProfile(defaults.Profiles["global"]));
        var global = settings.Profiles["global"];
        global.Buttons.TryAdd(16, ButtonBinding.BuiltIn("IncreaseSensitivity"));
        global.Buttons.TryAdd(17, ButtonBinding.BuiltIn("DecreaseSensitivity"));
        global.Buttons.TryAdd(18, ButtonBinding.BuiltIn("ToggleDominant"));
        global.Buttons.TryAdd(20, ButtonBinding.BuiltIn("OpenSettings"));
        settings.Profiles["fusion"].Buttons.TryAdd(15, ButtonBinding.Disabled());

        settings.AppProfileMappings ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (settings.AppProfileMappings.Comparer != StringComparer.OrdinalIgnoreCase) settings.AppProfileMappings = new Dictionary<string, string>(settings.AppProfileMappings, StringComparer.OrdinalIgnoreCase);
        settings.AppProfileMappings = settings.AppProfileMappings
            .Where(kv => !string.IsNullOrWhiteSpace(ProfileResolver.NormalizeProcessKey(kv.Key)) && settings.Profiles.ContainsKey(kv.Value))
            .ToDictionary(kv => ProfileResolver.NormalizeProcessKey(kv.Key), kv => kv.Value.ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);
    }

    private static ProfileSettings CloneProfile(ProfileSettings p) => new()
    {
        DisplayName = p.DisplayName,
        Buttons = p.Buttons.ToDictionary(x => x.Key, x => new ButtonBinding { Mode = x.Value.Mode, Value = x.Value.Value }),
        EditorControlsEnabled = p.EditorControlsEnabled,
        EditorMode = p.EditorMode, EditorAxis = p.EditorAxis, EditorDeadzone = p.EditorDeadzone, EditorRate = p.EditorRate,
        EditorInvertX = p.EditorInvertX, EditorInvertY = p.EditorInvertY, EditorZoomEnabled = p.EditorZoomEnabled,
        PanStyle = p.PanStyle, RotateStyle = p.RotateStyle, LockHorizon = p.LockHorizon,
        AxisBindings = p.AxisBindings.ToDictionary(x => x.Key, x => CloneAxis(x.Value), StringComparer.OrdinalIgnoreCase)
    };

    private static AxisBinding CloneAxis(AxisBinding b) => new()
    {
        PositiveAction = b.PositiveAction, NegativeAction = b.NegativeAction, NeutralAction = b.NeutralAction,
        PositiveValue = b.PositiveValue, NegativeValue = b.NegativeValue, NeutralValue = b.NeutralValue,
        Deadzone = b.Deadzone, Rate = b.Rate, Repeat = b.Repeat
    };

    private static string NormalizeEditorMode(string? value, string fallback)
    {
        string[] modes = { "Off", "Custom Axes", "Resolve Jog", "Horizontal Scroll", "Vertical Scroll", "Frame Jog", "2D Pan (MMB)", "2D Pan (Space+LMB)" };
        return modes.FirstOrDefault(x => x.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? fallback;
    }
}
