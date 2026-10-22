namespace SpacePilotBridge.Core;

public sealed record SpacePilotButtonDefinition(
    int RawId,
    string Name,
    int? DefaultVirtualBit,
    string? DefaultVirtualName);

/// <summary>
/// Physical button numbering for the original Logitech-era SpacePilot (046D:C625).
/// The bridge intentionally exposes itself to 3DxWare as a C631 SpaceMouse Pro
/// Wireless because that identity is proven to work for motion. C631 does NOT
/// expose every legacy SpacePilot view/function key in 3DxWare, so unsupported
/// physical keys stay available to the bridge's own software mapper.
/// </summary>
public static class SpacePilotButtons
{
    public static readonly IReadOnlyList<SpacePilotButtonDefinition> All = new SpacePilotButtonDefinition[]
    {
        new(0,  "1",        12, "1"),
        new(1,  "2",        13, "2"),
        new(2,  "3",        14, "3"),
        new(3,  "4",        15, "4"),
        new(4,  "5",        null, null),
        new(5,  "6",        null, null),
        new(6,  "Top",       2, "Top"),
        // C631 has no driver-visible Left/Back/Bottom quick-view button.
        // Left is handled through a configurable carrier slot by the app.
        new(7,  "Left",     null, null),
        new(8,  "Right",     4, "Right"),
        new(9,  "Front",     5, "Front"),
        new(10, "Esc",      22, "Esc"),
        new(11, "Alt",      23, "Alt"),
        new(12, "Shift",    24, "Shift"),
        new(13, "Ctrl",     25, "Ctrl"),
        new(14, "Fit",       1, "Fit"),
        new(15, "Panel",     0, "Menu"),
        new(16, "Speed +",  null, null),
        new(17, "Speed -",  null, null),
        new(18, "Dominant", null, null),
        new(19, "3D Lock",  26, "Rotation lock"),
        new(20, "Config",   null, null),
    };

    private static readonly Dictionary<int, SpacePilotButtonDefinition> ByRaw =
        All.ToDictionary(b => b.RawId);

    public static string GetName(int rawId) =>
        ByRaw.TryGetValue(rawId, out var b) ? b.Name : $"Button {rawId}";

    public static int? GetDefaultVirtualBit(int rawId) =>
        ByRaw.TryGetValue(rawId, out var b) ? b.DefaultVirtualBit : null;

    public static string? GetDefaultVirtualName(int rawId) =>
        ByRaw.TryGetValue(rawId, out var b) ? b.DefaultVirtualName : null;
}

/// <summary>
/// Only buttons actually exposed by the C631 SpaceMouse Pro Wireless profile in
/// current 3DxWare. Missing legacy SpacePilot keys cannot be made to appear in
/// the 3DxWare GUI merely by setting unused bits in report 3.
/// </summary>
public static class VirtualSpaceMouseButtons
{
    public static readonly IReadOnlyDictionary<string, int> ByName =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Menu"] = 0,
            ["Fit"] = 1,
            ["Top"] = 2,
            ["Right"] = 4,
            ["Front"] = 5,
            ["Roll CW"] = 8,
            ["1"] = 12,
            ["2"] = 13,
            ["3"] = 14,
            ["4"] = 15,
            ["Esc"] = 22,
            ["Alt"] = 23,
            ["Shift"] = 24,
            ["Ctrl"] = 25,
            ["Rotation lock"] = 26,
        };

    public static readonly string[] CarrierNames =
    {
        "Menu", "Fit", "Top", "Right", "Front", "Roll CW", "1", "2", "3", "4"
    };

    public static string GetName(int bit) =>
        ByName.FirstOrDefault(kvp => kvp.Value == bit).Key ?? $"Bit {bit}";
}
