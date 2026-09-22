using System;
using System.Runtime.InteropServices;

namespace JinxyClicker;

/// <summary>
/// Keeps Windows from quietly slowing this process down while it is in the
/// background — which is every moment that matters, because the game is in
/// front whenever any of this is being used.
/// </summary>
/// <remarks>
/// Two separate throttles, and only one of them was being turned off.
///
/// <b>Timer resolution.</b> Windows 11 ignores a background process's
/// timeBeginPeriod request. Measured against a competing clicker: matching
/// median timing, but a period standard deviation of 37.95 ms against its
/// 2.21 ms, with single gaps stretching to 617 ms.
///
/// <b>Execution speed (EcoQoS).</b> The one that was missing. Windows decides a
/// process that has sat in the background long enough is a background task, and
/// moves it to efficiency cores at a reduced clock. It does not happen at
/// launch — it builds up over a session, which is exactly the shape of "fine
/// for a couple of hours, then the hit registration gets worse". Nothing in the
/// app changes at that point; the CPU it is running on does.
///
/// Setting a control bit while leaving its state bit clear is the documented
/// way to say "this process manages that itself, do not throttle it". Setting
/// both would ask for the opposite.
///
/// Best effort throughout. On a build without these behaviours the call fails
/// and everything still works, just as it did before.
/// </remarks>
public static class ProcessTiming
{
    /// <summary>Opts out of both background throttles. Safe to call repeatedly.</summary>
    public static void KeepResponsiveInBackground()
    {
        try
        {
            var state = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = CurrentVersion,
                ControlMask = IgnoreTimerResolution | ExecutionSpeed,
                StateMask = 0
            };

            SetProcessInformation(
                GetCurrentProcess(),
                ProcessPowerThrottlingInformation,
                ref state,
                (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
        }
        catch
        {
            // Older Windows, or a policy that forbids it. Not worth a crash.
        }
    }

    /// <summary>
    /// What Windows is actually granting, as opposed to what was asked for.
    /// </summary>
    /// <remarks>
    /// Asking not to be throttled and being believed are different things, and
    /// the difference is invisible from inside a running app — which is exactly
    /// how "fine for two hours, then worse" went unexplained. This reads the
    /// state back so a report can say what was true at the time.
    ///
    /// Returns null when the call fails, which is the honest answer on a build
    /// that does not have the feature: not "we are fine", but "not known".
    /// </remarks>
    public static bool? IsExecutionThrottled()
    {
        try
        {
            var state = new PROCESS_POWER_THROTTLING_STATE();

            if (!GetProcessInformation(
                    GetCurrentProcess(),
                    ProcessPowerThrottlingInformation,
                    ref state,
                    (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>()))
            {
                return null;
            }

            // Managed by us, and the state bit clear, means "do not throttle".
            // Anything else means Windows is free to, or is already doing so.
            bool managed = (state.ControlMask & ExecutionSpeed) != 0;
            bool throttled = (state.StateMask & ExecutionSpeed) != 0;

            return managed ? throttled : true;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The system timer resolution in milliseconds, as it currently stands.
    /// </summary>
    /// <remarks>
    /// The number that decides whether a sleep can land within a millisecond or
    /// only within about fifteen. timeBeginPeriod asks for it; this reports what
    /// is actually in force, which is not always the same thing — Windows
    /// ignores the request from a process it considers background.
    /// </remarks>
    public static double? TimerResolutionMs()
    {
        try
        {
            if (NtQueryTimerResolution(out uint _, out uint _, out uint current) != 0) return null;

            // Reported in 100-nanosecond units.
            return current / 10000.0;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryTimerResolution(
        out uint minimum, out uint maximum, out uint current);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessInformation(
        IntPtr process, int informationClass,
        ref PROCESS_POWER_THROTTLING_STATE info, uint size);

    private const int ProcessPowerThrottlingInformation = 4;
    private const uint CurrentVersion = 1;

    /// <summary>Do not park this process on efficiency cores.</summary>
    private const uint ExecutionSpeed = 0x1;

    /// <summary>Honour this process's timer resolution request in the background.</summary>
    private const uint IgnoreTimerResolution = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(
        IntPtr process, int informationClass,
        ref PROCESS_POWER_THROTTLING_STATE info, uint size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
}
