using System.Diagnostics;
using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// The process has to outrank Roblox, or the game takes the CPU the clicks
/// need.
/// </summary>
/// <remarks>
/// Measured with a stand-in for Roblox at uncapped FPS — every core busy, at
/// the AboveNormal priority this app's own "Roblox priority" setting gives it.
/// With this process left at Normal, a 10-second run delivered 202 to 224 of
/// its 334 clicks under Ultra Accuracy, with single freezes up to 1.9 seconds.
/// At AboveNormal it delivered 334 of 334 every time. Fewer clicks in a timed
/// test is fewer hits.
/// </remarks>
public class ProcessTimingTests
{
    [Fact]
    public void KeepingResponsiveRaisesThisProcessAboveNormal()
    {
        using Process self = Process.GetCurrentProcess();
        ProcessPriorityClass before = self.PriorityClass;

        try
        {
            self.PriorityClass = ProcessPriorityClass.Normal;

            ProcessTiming.KeepResponsiveInBackground();

            self.Refresh();
            Assert.Equal(ProcessPriorityClass.AboveNormal, self.PriorityClass);
        }
        finally
        {
            self.PriorityClass = before;
        }
    }

    /// <summary>
    /// Never High. It can starve input handling on a weak machine, which would
    /// cost more than it gained — the same reason Roblox is only ever raised to
    /// AboveNormal. And never lower something the user raised on purpose.
    /// </summary>
    [Fact]
    public void LeavesAHigherPriorityAlone()
    {
        using Process self = Process.GetCurrentProcess();
        ProcessPriorityClass before = self.PriorityClass;

        try
        {
            self.PriorityClass = ProcessPriorityClass.High;

            ProcessTiming.KeepResponsiveInBackground();

            self.Refresh();
            Assert.Equal(ProcessPriorityClass.High, self.PriorityClass);
        }
        finally
        {
            self.PriorityClass = before;
        }
    }
}
