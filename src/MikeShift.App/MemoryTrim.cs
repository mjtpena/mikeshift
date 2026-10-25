using Windows.Win32;

namespace MikeShift.App;

/// <summary>
/// Gives memory back after the app did something heavy (a wheel, an editor, a conversion). MikeShift sits in the tray all day, so what it
/// holds while idle matters more than how fast the next gesture could be if everything stayed resident: after a collection the working set
/// is trimmed, and the few pages the next gesture needs fault back in from memory (measured: the wheel still appears in 30-46 ms).
/// </summary>
internal static class MemoryTrim
{
    private static int s_pending;

    /// <summary>Collects (compacting the large object heap) and trims the working set. Safe on any thread.</summary>
    public static void CompactNow()
    {
        try
        {
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            PInvoke.SetProcessWorkingSetSize(PInvoke.GetCurrentProcess(), nuint.MaxValue, nuint.MaxValue);
        }
        catch (Exception)
        {
            // Housekeeping only.
        }
    }

    /// <summary>Runs <see cref="CompactNow"/> once, <paramref name="delayMs"/> from now, however often this is called meanwhile.</summary>
    public static void CompactSoon(int delayMs = 3000)
    {
        if (Interlocked.Exchange(ref s_pending, 1) == 1) return;
        _ = Task.Delay(delayMs).ContinueWith(_ =>
        {
            Volatile.Write(ref s_pending, 0);
            CompactNow();
        }, TaskScheduler.Default);
    }
}
