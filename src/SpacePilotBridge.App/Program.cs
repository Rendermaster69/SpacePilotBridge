namespace SpacePilotBridge.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length >= 2 && args[0].Equals("--lcd-host", StringComparison.OrdinalIgnoreCase))
        {
            Environment.ExitCode = LcdHostServer.Run(args[1]);
            return;
        }

        using var mutex = new Mutex(initiallyOwned: true, "Local\\SpacePilotBridge.SingleInstance", out bool firstInstance);
        if (!firstInstance) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new BridgeApplicationContext());
    }
}
