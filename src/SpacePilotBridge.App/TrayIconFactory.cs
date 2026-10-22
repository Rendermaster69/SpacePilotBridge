using System.Drawing;
namespace SpacePilotBridge.App;

internal static class TrayIconFactory
{
    public static Icon CreateIcon()
    {
        try
        {
            string? path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path))
            {
                using Icon? extracted = Icon.ExtractAssociatedIcon(path);
                if (extracted is not null) return (Icon)extracted.Clone();
            }
        }
        catch { }
        return (Icon)SystemIcons.Application.Clone();
    }
}
