namespace SpacePilotBridge.App;

internal static class ProfileResolver
{
    public static string FromForeground(ForegroundAppInfo app, BridgeSettings settings)
    {
        string key = NormalizeProcessKey(app.ProcessName);
        if (!string.IsNullOrWhiteSpace(key) &&
            settings.AppProfileMappings.TryGetValue(key, out string? mapped) &&
            settings.Profiles.ContainsKey(mapped))
            return mapped.ToLowerInvariant();

        string suggested = SuggestedFromNames(app.ProcessName, app.DisplayName, app.WindowTitle);
        return settings.Profiles.ContainsKey(suggested) ? suggested : "global";
    }

    public static string FromForegroundName(string app) => SuggestedFromNames(app, app);

    public static string SuggestedFromNames(string? processName, string? displayName, string? windowTitle = null)
    {
        string n = $"{processName} {displayName} {windowTitle}".ToLowerInvariant();
        if (n.Contains("fusion")) return "fusion";
        if (n.Contains("maya")) return "maya";
        if (n.Contains("zbrush")) return "zbrush";
        if (n.Contains("resolve")) return "resolve";
        if (n.Contains("ableton") || NormalizeProcessKey(processName).Equals("live", StringComparison.OrdinalIgnoreCase)) return "ableton";
        if (n.Contains("illustrator")) return "illustrator";
        if (n.Contains("photoshop")) return "photoshop";
        return "global";
    }

    public static string NormalizeProcessKey(string? processName)
    {
        string value = (processName ?? string.Empty).Trim();
        if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) value = value[..^4];
        return value.ToLowerInvariant();
    }

    public static bool IsValidProfile(string? profile, BridgeSettings settings) =>
        !string.IsNullOrWhiteSpace(profile) && settings.Profiles.ContainsKey(profile);

    public static string DisplayName(string key, BridgeSettings? settings = null)
    {
        if (settings is not null && settings.Profiles.TryGetValue(key, out var p) && !string.IsNullOrWhiteSpace(p.DisplayName))
            return p.DisplayName;
        return key.ToLowerInvariant() switch
        {
            "fusion" => "Fusion 360",
            "maya" => "Maya",
            "zbrush" => "ZBrush",
            "resolve" => "DaVinci Resolve",
            "ableton" => "Ableton Live",
            "illustrator" => "Adobe Illustrator",
            "photoshop" => "Adobe Photoshop",
            "global" => "Global",
            _ => ToTitle(key)
        };
    }

    public static string MakeProfileKey(string displayName)
    {
        string raw = (displayName ?? string.Empty).Trim().ToLowerInvariant();
        var chars = raw.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        string key = new string(chars);
        while (key.Contains("--", StringComparison.Ordinal)) key = key.Replace("--", "-", StringComparison.Ordinal);
        key = key.Trim('-');
        return string.IsNullOrWhiteSpace(key) ? "custom" : key;
    }

    private static string ToTitle(string key)
    {
        string text = key.Replace('-', ' ').Replace('_', ' ').Trim();
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text);
    }
}
