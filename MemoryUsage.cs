using System;
using System.Runtime.InteropServices;

namespace JinxyClicker;

/// <summary>
/// Physical memory in use across the whole machine.
/// </summary>
/// <remarks>
/// This tile sits beside the CPU one, which has always reported the whole
/// machine, and two tiles side by side are read as a pair whether or not they
/// are one. So this reports the same scope rather than this process alone.
///
/// It took three attempts to get here. WorkingSet64 read high because it counts
/// the .NET and WPF runtime pages shared with every managed process — 210 MB
/// against Task Manager's 120 MB for the same app. Matching Task Manager exactly
/// needs a performance counter, and resolving the right instance meant opening
/// one against every process on the machine, which pulled the whole counter
/// infrastructure in and took the app from 99 MB to 205 MB with 1800 handles.
/// Measuring memory should not cost this much memory.
///
/// GlobalMemoryStatusEx is one syscall against a struct on the stack, and it
/// answers the question the tile is actually asking.
/// </remarks>
public static class MemoryUsage
{
    /// <summary>Gigabytes of physical memory in use machine-wide, or null.</summary>
    public static double? GigabytesInUse()
    {
        if (!TryRead(out MEMORYSTATUSEX status)) return null;

        ulong used = status.ullTotalPhys - status.ullAvailPhys;
        return used / 1024.0 / 1024.0 / 1024.0;
    }

    /// <summary>Total physical memory in gigabytes, or null.</summary>
    public static double? TotalGigabytes() =>
        TryRead(out MEMORYSTATUSEX status)
            ? status.ullTotalPhys / 1024.0 / 1024.0 / 1024.0
            : null;

    private static bool TryRead(out MEMORYSTATUSEX status)
    {
        status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };

        try
        {
            return GlobalMemoryStatusEx(ref status);
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
}
