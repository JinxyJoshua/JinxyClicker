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
