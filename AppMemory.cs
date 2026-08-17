using System;
using System.Runtime.InteropServices;

namespace JinxyClicker;

/// <summary>
/// How much memory this process is using, without spending memory to find out.
/// </summary>
/// <remarks>
/// Three attempts, and the middle one is the interesting failure.
///
/// WorkingSet64 was first and reads high: it counts pages shared with every
/// other managed process — the .NET and WPF runtime images — so it reported
/// 210 MB against Task Manager's 120 MB for the same process. Most of that is
/// not memory this app is responsible for.
///
/// The private working set is exactly what Task Manager shows, but the only
/// managed route to it is a PerformanceCounter, and resolving the right instance
/// means opening a counter against every process on the machine. Measured: the
/// app went from 99 MB to 205 MB and 1800 handles the moment that ran. Reporting
/// memory accurately is not worth doubling the memory being reported.
///
/// So: PSAPI directly. PrivateUsage is the private commit charge, which excludes
/// every shared runtime page and costs one syscall against a struct on the
/// stack. It reads a little above the private working set, because commit counts
/// pages that have been paged out, but it moves with the app rather than with
/// the runtime and it is honest about what this process alone is holding.
/// </remarks>
public static class AppMemory
{
    /// <summary>Megabytes this process alone is using, or null if unavailable.</summary>
    public static double? MegabytesInUse()
    {
        try
        {
            var counters = new PROCESS_MEMORY_COUNTERS_EX
            {
                cb = (uint)Marshal.SizeOf<PROCESS_MEMORY_COUNTERS_EX>()
            };

            if (GetProcessMemoryInfo(GetCurrentProcess(), ref counters, counters.cb))
                return counters.PrivateUsage / 1024.0 / 1024.0;
        }
        catch
        {
            // Nothing here is worth a crash over a status tile.
        }

        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_MEMORY_COUNTERS_EX
    {
        public uint cb;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSize;
        public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage;
        public UIntPtr PeakPagefileUsage;
        public UIntPtr PrivateUsage;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetProcessMemoryInfo(
        IntPtr process, ref PROCESS_MEMORY_COUNTERS_EX counters, uint size);
}
