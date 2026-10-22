namespace SpacePilotBridge.Core;

public sealed record SpacePilotState(
    double Tx, double Ty, double Tz,
    double Rx, double Ry, double Rz,
    IReadOnlyCollection<int> Buttons,
    DateTime TimestampUtc)
{
    public static readonly SpacePilotState Zero = new(0,0,0,0,0,0, Array.Empty<int>(), DateTime.UtcNow);
}
