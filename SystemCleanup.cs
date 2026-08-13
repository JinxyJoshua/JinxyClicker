using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace MyBlinkStyleClicker;

public readonly record struct MemoryStatus(ulong TotalBytes, ulong AvailableBytes, uint LoadPercent)
{
    public double TotalGb => TotalBytes / 1024.0 / 1024.0 / 1024.0;
    public double AvailableGb => AvailableBytes / 1024.0 / 1024.0 / 1024.0;
    public double UsedGb => TotalGb - AvailableGb;
}

public static class MemoryTools
{
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
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    public static MemoryStatus Read()
    {
        MEMORYSTATUSEX status = default;
        status.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();

        if (!GlobalMemoryStatusEx(ref status)) return default;

        return new MemoryStatus(status.ullTotalPhys, status.ullAvailPhys, status.dwMemoryLoad);
    }

    /// <summary>
    /// Trims every process working set this account is allowed to touch.
    /// </summary>
    /// <remarks>
    /// This does not create memory. It pushes pages out of physical RAM onto the
    /// standby list, so the "available" figure rises and then falls again as the
    /// affected processes fault their pages straight back in. It is included
    /// because it was asked for, not because it makes anything faster.
    /// </remarks>
    public static (int Trimmed, int Skipped) TrimWorkingSets()
    {
        int trimmed = 0, skipped = 0;

        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                if (EmptyWorkingSet(process.Handle)) trimmed++;
                else skipped++;
            }
            catch
            {
                // Protected or already-exited processes are simply not ours.
                skipped++;
            }
            finally
            {
                process.Dispose();
            }
        }

        return (trimmed, skipped);
    }
}

public readonly record struct TempScan(int Files, long Bytes)
{
    public double Megabytes => Bytes / 1024.0 / 1024.0;
}

public readonly record struct CleanResult(int Deleted, long Bytes, int Skipped)
{
    public double Megabytes => Bytes / 1024.0 / 1024.0;
}

/// <summary>
/// Deletes stale files from the temp directories, and nowhere else.
/// </summary>
public static class TempCleaner
{
    /// <summary>
    /// Files touched more recently than this are left alone — an installer or a
    /// running app may still be using them.
    /// </summary>
    public static readonly TimeSpan MinimumAge = TimeSpan.FromHours(24);

    private static IEnumerable<string> Roots()
    {
        string user = Path.GetTempPath();
        if (Directory.Exists(user)) yield return user;

        string windows = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");

        if (Directory.Exists(windows) &&
            !string.Equals(windows, user.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            yield return windows;
        }
    }

    public static TempScan Scan(DateTime cutoff)
    {
        int files = 0;
        long bytes = 0;

        foreach (string root in Roots())
        {
            foreach (FileInfo file in Walk(new DirectoryInfo(root)))
            {
                try
                {
                    if (file.LastWriteTimeUtc >= cutoff) continue;
                    files++;
                    bytes += file.Length;
                }
                catch
                {
                    // Vanished between enumeration and inspection.
                }
            }
        }

        return new TempScan(files, bytes);
    }

    public static CleanResult Clean(DateTime cutoff)
    {
        int deleted = 0, skipped = 0;
        long bytes = 0;

        foreach (string root in Roots())
        {
            var directory = new DirectoryInfo(root);

            foreach (FileInfo file in Walk(directory))
            {
                try
                {
                    if (file.LastWriteTimeUtc >= cutoff)
                    {
                        skipped++;
                        continue;
                    }

                    long size = file.Length;

                    // Read-only temp files still belong to the user.
                    if (file.IsReadOnly) file.IsReadOnly = false;

                    file.Delete();

                    deleted++;
                    bytes += size;
                }
                catch
                {
                    // Locked by a running process, or permission denied.
                    skipped++;
                }
            }

            RemoveEmptyDirectories(directory, isRoot: true);
        }

        return new CleanResult(deleted, bytes, skipped);
    }

    /// <summary>
    /// Depth-first file walk that refuses to cross a reparse point. A junction
    /// inside temp can point anywhere — following one would take a temp cleaner
    /// somewhere it has no business deleting.
    /// </summary>
    private static IEnumerable<FileInfo> Walk(DirectoryInfo directory)
    {
        if (IsReparsePoint(directory)) yield break;

        FileInfo[] files;
        try { files = directory.GetFiles(); }
        catch { yield break; }

        foreach (FileInfo file in files) yield return file;

        DirectoryInfo[] children;
        try { children = directory.GetDirectories(); }
        catch { yield break; }

        foreach (DirectoryInfo child in children)
            foreach (FileInfo file in Walk(child))
                yield return file;
    }

    private static void RemoveEmptyDirectories(DirectoryInfo directory, bool isRoot)
    {
        if (IsReparsePoint(directory)) return;

        DirectoryInfo[] children;
        try { children = directory.GetDirectories(); }
        catch { return; }

        foreach (DirectoryInfo child in children) RemoveEmptyDirectories(child, isRoot: false);

        if (isRoot) return;

        try
        {
            if (directory.GetFiles().Length == 0 && directory.GetDirectories().Length == 0)
                directory.Delete();
        }
        catch
        {
            // In use, or not ours to remove.
        }
    }

    private static bool IsReparsePoint(DirectoryInfo directory)
    {
        try
        {
            return directory.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return true;
        }
    }
}
