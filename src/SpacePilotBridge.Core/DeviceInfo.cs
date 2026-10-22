namespace SpacePilotBridge.Core;

public sealed record DeviceInfo(
    string Path,
    ushort VendorId,
    ushort ProductId,
    ushort Version,
    ushort InputReportLength,
    ushort OutputReportLength,
    ushort FeatureReportLength);
