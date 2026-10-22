using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SpacePilotBridge.App;

internal static class MemoryTrimmer
{
    [DllImport("psapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    public static void TrimSoon()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350).ConfigureAwait(false);
                GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: false);
                using Process p = Process.GetCurrentProcess();
                EmptyWorkingSet(p.Handle);
            }
            catch
            {
                // Memory trimming is best-effort only.
            }
        });
    }
}
