using System.Text.Json;

namespace SpacePilotBridge.Lcd;

public sealed class ScreensaverDefinition
{
    public string Name { get; set; } = "Custom Saver";
    public string Effect { get; set; } = "Warp Tunnel";
    public int Fps { get; set; } = 5;
    public double Speed { get; set; } = 1.0;
    public string Text { get; set; } = string.Empty;
    public bool Invert { get; set; }
}

public sealed record ScreensaverInfo(
    string Name,
    string Effect,
    int Fps,
    bool IsBuiltIn,
    string? FilePath,
    string Text,
    double Speed,
    bool Invert);

/// <summary>
/// Data-driven screensaver catalog. Custom .spbsaver presets and GIF/PNG/BMP files
/// placed in %LOCALAPPDATA%\SpacePilotBridge\Screensavers are discovered without a rebuild.
/// </summary>
public static class ScreensaverLibrary
{
    private static readonly string[] BuiltIns =
    {
        "Starfield",
        "Matrix",
        "Bounce",
        "Warp Tunnel",
        "Pong",
        "Orbit",
        "Wave",
        "Digital Rain",
        "Scanner",
        "Clock"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    private static readonly object CacheGate = new();
    private static IReadOnlyList<ScreensaverInfo>? _cache;
    private static DateTime _cacheUntilUtc;

    public static string FolderPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SpacePilotBridge",
        "Screensavers");

    public static IReadOnlyList<string> BuiltInNames => BuiltIns;

    public static IReadOnlyList<ScreensaverInfo> GetAll(bool force = false)
    {
        lock (CacheGate)
        {
            if (!force && _cache is not null && DateTime.UtcNow < _cacheUntilUtc) return _cache;
            _cache = Scan();
            _cacheUntilUtc = DateTime.UtcNow.AddSeconds(1);
            return _cache;
        }
    }

    public static void Invalidate()
    {
        lock (CacheGate)
        {
            _cache = null;
            _cacheUntilUtc = DateTime.MinValue;
        }
    }

    private static IReadOnlyList<ScreensaverInfo> Scan()
    {
        Directory.CreateDirectory(FolderPath);
        var list = new List<ScreensaverInfo>(BuiltIns.Length + 8);
        foreach (string name in BuiltIns)
            list.Add(new ScreensaverInfo(name, name, DefaultFps(name), true, null, string.Empty, 1.0, false));

        foreach (string file in Directory.EnumerateFiles(FolderPath).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            try
            {
                if (ext == ".spbsaver")
                {
                    var def = JsonSerializer.Deserialize<ScreensaverDefinition>(File.ReadAllText(file), JsonOptions);
                    if (def is null) continue;
                    string name = string.IsNullOrWhiteSpace(def.Name) ? Path.GetFileNameWithoutExtension(file) : def.Name.Trim();
                    list.RemoveAll(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    list.Add(new ScreensaverInfo(
                        name,
                        string.IsNullOrWhiteSpace(def.Effect) ? "Warp Tunnel" : def.Effect.Trim(),
                        Math.Clamp(def.Fps, 1, 6),
                        false,
                        file,
                        def.Text ?? string.Empty,
                        Math.Clamp(def.Speed, 0.2, 4.0),
                        def.Invert));
                }
                else if (ext is ".gif" or ".png" or ".bmp" or ".jpg" or ".jpeg")
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    list.RemoveAll(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    list.Add(new ScreensaverInfo(name, "Image", ext == ".gif" ? 2 : 1, false, file, string.Empty, 1.0, false));
                }
            }
            catch
            {
                // One malformed custom saver must never break the bridge.
            }
        }

        return list.OrderByDescending(x => x.IsBuiltIn).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static ScreensaverInfo Resolve(string? name)
    {
        string requested = string.IsNullOrWhiteSpace(name) ? "Starfield" : name.Trim();
        return GetAll().FirstOrDefault(x => x.Name.Equals(requested, StringComparison.OrdinalIgnoreCase))
            ?? new ScreensaverInfo("Starfield", "Starfield", 5, true, null, string.Empty, 1.0, false);
    }

    public static int GetIntervalMilliseconds(string? name)
    {
        int fps = Math.Clamp(Resolve(name).Fps, 1, 6);
        return Math.Max(100, 1000 / fps);
    }

    public static string SavePreset(string name, string effect, int fps = 5, double speed = 1.0, string text = "")
    {
        Directory.CreateDirectory(FolderPath);
        string safeName = SanitizeFileName(string.IsNullOrWhiteSpace(name) ? effect : name);
        string path = Path.Combine(FolderPath, safeName + ".spbsaver");
        var def = new ScreensaverDefinition
        {
            Name = string.IsNullOrWhiteSpace(name) ? safeName : name.Trim(),
            Effect = string.IsNullOrWhiteSpace(effect) ? "Warp Tunnel" : effect.Trim(),
            Fps = Math.Clamp(fps, 1, 6),
            Speed = Math.Clamp(speed, 0.2, 4.0),
            Text = text?.Trim() ?? string.Empty
        };
        File.WriteAllText(path, JsonSerializer.Serialize(def, JsonOptions));
        Invalidate();
        return path;
    }

    public static string ImportMedia(string sourcePath)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Screensaver media not found.", sourcePath);
        string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (ext is not (".gif" or ".png" or ".bmp" or ".jpg" or ".jpeg" or ".spbsaver"))
            throw new InvalidOperationException("Supported files: GIF, PNG, BMP, JPG and .spbsaver.");

        Directory.CreateDirectory(FolderPath);
        string destination = Path.Combine(FolderPath, Path.GetFileName(sourcePath));
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            return destination;

        string stem = Path.GetFileNameWithoutExtension(destination);
        int i = 2;
        while (File.Exists(destination))
            destination = Path.Combine(FolderPath, $"{stem}-{i++}{ext}");
        File.Copy(sourcePath, destination, false);
        Invalidate();
        return destination;
    }

    public static bool DeleteCustom(string name)
    {
        var info = Resolve(name);
        if (info.IsBuiltIn || string.IsNullOrWhiteSpace(info.FilePath)) return false;
        try
        {
            File.Delete(info.FilePath);
            Invalidate();
            return true;
        }
        catch { return false; }
    }

    private static int DefaultFps(string name) => name switch
    {
        "Clock" => 1,
        "Matrix" => 5,
        "Digital Rain" => 5,
        "Pong" => 6,
        "Warp Tunnel" => 6,
        _ => 5
    };

    private static string SanitizeFileName(string value)
    {
        string result = value;
        foreach (char invalid in Path.GetInvalidFileNameChars()) result = result.Replace(invalid, '-');
        result = result.Trim().Trim('.');
        return string.IsNullOrWhiteSpace(result) ? "custom-saver" : result;
    }
}
