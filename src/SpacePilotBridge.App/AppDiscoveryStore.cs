using System.Text.Json;

namespace SpacePilotBridge.App;

internal sealed class RecentAppEntry
{
    public string ProcessName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public DateTime LastSeenUtc { get; set; }

    public string ProcessKey => ProfileResolver.NormalizeProcessKey(ProcessName);
    public string ExecutableName => string.IsNullOrWhiteSpace(ProcessName) ? string.Empty : ProcessName + ".exe";
}

internal static class AppDiscoveryStore
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static Dictionary<string, RecentAppEntry>? _entries;
    private static DateTime _lastSaveUtc = DateTime.MinValue;

    private static string FilePath => Path.Combine(SettingsStore.DirectoryPath, "recent-apps.json");

    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "searchhost", "startmenuexperiencehost", "shellexperiencehost",
        "applicationframehost", "textinputhost", "lockapp", "dwm", "taskmgr"
    };

    public static void Observe(ForegroundAppInfo app)
    {
        if (app.IsBridge || string.IsNullOrWhiteSpace(app.ProcessName)) return;
        string key = ProfileResolver.NormalizeProcessKey(app.ProcessName);
        if (string.IsNullOrWhiteSpace(key) || Noise.Contains(key)) return;

        lock (Gate)
        {
            EnsureLoaded();
            bool isNew = !_entries!.TryGetValue(key, out RecentAppEntry? entry);
            entry ??= new RecentAppEntry();
            entry.ProcessName = app.ProcessName;
            entry.DisplayName = string.IsNullOrWhiteSpace(app.DisplayName) ? app.ProcessName : app.DisplayName;
            if (!string.IsNullOrWhiteSpace(app.WindowTitle)) entry.WindowTitle = app.WindowTitle;
            entry.LastSeenUtc = DateTime.UtcNow;
            _entries[key] = entry;

            if (isNew || DateTime.UtcNow - _lastSaveUtc > TimeSpan.FromSeconds(3)) SaveUnsafe();
        }
    }

    public static void AddExecutable(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath)) return;
        string processName = Path.GetFileNameWithoutExtension(executablePath);
        string key = ProfileResolver.NormalizeProcessKey(processName);
        if (string.IsNullOrWhiteSpace(key)) return;
        string displayName = processName;
        try
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(executablePath);
            if (!string.IsNullOrWhiteSpace(info.FileDescription)) displayName = info.FileDescription;
            else if (!string.IsNullOrWhiteSpace(info.ProductName)) displayName = info.ProductName;
        }
        catch { }
        lock (Gate)
        {
            EnsureLoaded();
            _entries![key] = new RecentAppEntry
            {
                ProcessName = processName,
                DisplayName = displayName,
                WindowTitle = string.Empty,
                LastSeenUtc = DateTime.UtcNow
            };
            SaveUnsafe();
        }
    }

    public static IReadOnlyList<RecentAppEntry> GetRecent()
    {
        lock (Gate)
        {
            EnsureLoaded();
            return _entries!.Values
                .OrderByDescending(x => x.LastSeenUtc)
                .Select(Clone)
                .ToList();
        }
    }

    public static void Forget(string processNameOrKey)
    {
        string key = ProfileResolver.NormalizeProcessKey(processNameOrKey);
        if (string.IsNullOrWhiteSpace(key)) return;
        lock (Gate)
        {
            EnsureLoaded();
            if (_entries!.Remove(key)) SaveUnsafe();
        }
    }

    private static void EnsureLoaded()
    {
        if (_entries is not null) return;
        _entries = new Dictionary<string, RecentAppEntry>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(FilePath)) return;
            var list = JsonSerializer.Deserialize<List<RecentAppEntry>>(File.ReadAllText(FilePath), JsonOptions) ?? new();
            foreach (var item in list)
            {
                string key = item.ProcessKey;
                if (!string.IsNullOrWhiteSpace(key)) _entries[key] = item;
            }
        }
        catch { }
    }

    private static void SaveUnsafe()
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.DirectoryPath);
            var list = _entries!.Values.OrderByDescending(x => x.LastSeenUtc).Take(100).ToList();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list, JsonOptions));
            _lastSaveUtc = DateTime.UtcNow;
        }
        catch { }
    }

    private static RecentAppEntry Clone(RecentAppEntry x) => new()
    {
        ProcessName = x.ProcessName,
        DisplayName = x.DisplayName,
        WindowTitle = x.WindowTitle,
        LastSeenUtc = x.LastSeenUtc
    };
}
