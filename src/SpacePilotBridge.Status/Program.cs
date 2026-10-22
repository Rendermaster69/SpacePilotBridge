using System.Diagnostics;
using SpacePilotBridge.Core;
using SpacePilotBridge.Lcd;
using SpacePilotBridge.Virtual;

Console.WriteLine("SpacePilotBridge v0.9 diagnostics");
Console.WriteLine("================================");
Console.WriteLine("Physical source : SpacePilot 046D:C625");

using var virtualControl = HidFeatureDevice.TryOpen(VirtualSpaceMouseWriter.VendorId, VirtualSpaceMouseWriter.ProductId, 18);
Console.WriteLine($"Virtual HID     : {(virtualControl is not null ? "FOUND 256F:C631" : "NOT FOUND")}");

string[] threeDxProcesses = { "3DxService", "3DxWinCore", "3DxNLServer" };
var running = threeDxProcesses.Where(n => Process.GetProcessesByName(n).Length > 0).ToArray();
Console.WriteLine($"3DxWare process : {(running.Length > 0 ? string.Join(", ", running) : "not detected")}");

using var lcd = new SpacePilotLcd();
bool lcdOk = lcd.ShowStatus("DIAGNOSTICS", true, virtualControl is not null, SpacePilotState.Zero, 1.0, false, false, "Diagnostics", 0.0, "On", false, "SPOTIFY -", new[] { "Speed", "Profile", "Latency", "Buttons" });
Console.WriteLine($"SpacePilot LCD  : {(lcdOk ? "WRITE OK" : "not writable")}");
Console.WriteLine();
Console.WriteLine("Move the puck; live values should change. Press Ctrl+C to quit.\n");

using var done = new ManualResetEventSlim(false);
Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.Set(); };

var hub = SpacePilotHub.Instance;
hub.ConnectionChanged += (_, _) => Console.WriteLine($"\nPhysical connected: {hub.IsConnected}");
hub.StateChanged += (_, s) =>
{
    Console.Write($"\rT [{s.Tx,7:F3} {s.Ty,7:F3} {s.Tz,7:F3}]  R [{s.Rx,7:F3} {s.Ry,7:F3} {s.Rz,7:F3}]  Buttons: {string.Join(',', s.Buttons),-20}");
};
hub.Acquire();
Console.WriteLine($"Physical connected: {hub.IsConnected}");
done.Wait();
hub.Release();
Console.WriteLine();
