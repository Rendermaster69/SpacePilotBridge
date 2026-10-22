using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SpacePilotBridge.App;

internal sealed record ForegroundAppInfo(
    string ProcessName,
    string DisplayName,
    string WindowTitle,
    bool IsBridge)
{
    public string ExecutableName => string.IsNullOrWhiteSpace(ProcessName) ? string.Empty : ProcessName + ".exe";
    public string ProcessKey => ProfileResolver.NormalizeProcessKey(ProcessName);
}

internal static class ForegroundApp
{
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    public static ForegroundAppInfo Get()
    {
        try
        {
            nint hwnd = GetForegroundWindow();
            if (hwnd == 0) return Empty;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return Empty;
            using var p = Process.GetProcessById((int)pid);
            string raw = p.ProcessName ?? string.Empty;
            string title = string.Empty;
            try { title = p.MainWindowTitle ?? string.Empty; } catch { }
            return new ForegroundAppInfo(raw, Friendly(raw), title, p.Id == Environment.ProcessId);
        }
        catch { return Empty; }
    }

    public static string GetName() => Get().DisplayName;

    private static ForegroundAppInfo Empty { get; } = new(string.Empty, string.Empty, string.Empty, false);

    private static string Friendly(string processName)
    {
        string n = processName.ToLowerInvariant();
        if (n.Contains("fusion")) return "FUSION 360";
        if (n.Contains("maya")) return "MAYA";
        if (n.Contains("zbrush")) return "ZBRUSH";
        if (n.Contains("resolve")) return "DAVINCI RESOLVE";
        if (n.Contains("ableton") || n == "live") return "ABLETON LIVE";
        if (n.Contains("illustrator")) return "ADOBE ILLUSTRATOR";
        if (n.Contains("photoshop")) return "ADOBE PHOTOSHOP";
        return processName;
    }
}
