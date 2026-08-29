using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// The mapping from a chosen button to the flags that press and release it.
/// </summary>
/// <remarks>
/// A press and its release are sent as two separate calls, because the duty
/// cycle puts real time between them. Pairing the wrong two flags does not
/// produce a wrong click — it produces a button held down across the entire
/// desktop with nothing left to release it, which needs a reboot or a real
/// click to clear.
/// </remarks>
public class ClickButtonTests
{
    /// <summary>The documented Win32 values, written out rather than derived.</summary>
    [Theory]
    [InlineData(ClickButton.Left, 0x0002u, 0x0004u)]
    [InlineData(ClickButton.Right, 0x0008u, 0x0010u)]
    [InlineData(ClickButton.Middle, 0x0020u, 0x0040u)]
    public void SendsTheFlagsWindowsDocuments(ClickButton button, uint down, uint up)
    {
        Assert.Equal(down, ClickButtons.DownFlag(button));
        Assert.Equal(up, ClickButtons.UpFlag(button));
    }

    /// <summary>
    /// The failure that matters: a release belonging to a different button than
    /// the press. Every pair must be distinct from every other pair.
    /// </summary>
    [Fact]
    public void NoButtonSharesAFlagWithAnother()
    {
        ClickButton[] all = { ClickButton.Left, ClickButton.Right, ClickButton.Middle };

        foreach (ClickButton a in all)
        {
            foreach (ClickButton b in all)
            {
                if (a == b) continue;

                Assert.NotEqual(ClickButtons.DownFlag(a), ClickButtons.DownFlag(b));
                Assert.NotEqual(ClickButtons.UpFlag(a), ClickButtons.UpFlag(b));

                // A down flag colliding with some other button's up flag would
                // release that button on every press.
                Assert.NotEqual(ClickButtons.DownFlag(a), ClickButtons.UpFlag(b));
            }
        }
    }

    [Fact]
    public void APressIsNeverAlsoItsOwnRelease()
    {
        foreach (ClickButton button in new[]
                 { ClickButton.Left, ClickButton.Right, ClickButton.Middle })
        {
            Assert.NotEqual(ClickButtons.DownFlag(button), ClickButtons.UpFlag(button));
        }
    }

    /// <summary>The wheel is called what it is called on a mouse, not in Win32.</summary>
    [Theory]
    [InlineData(ClickButton.Left, "Left")]
    [InlineData(ClickButton.Right, "Right")]
    [InlineData(ClickButton.Middle, "Wheel")]
    public void NamesTheButtonThePersonSees(ClickButton button, string expected)
    {
        Assert.Equal(expected, ClickButtons.Label(button));
    }

    [Theory]
    [InlineData("Left", ClickButton.Left)]
    [InlineData("right", ClickButton.Right)]
    [InlineData("MIDDLE", ClickButton.Middle)]
    public void ReadsBackWhatWasStored(string stored, ClickButton expected)
    {
        Assert.Equal(expected, ClickButtons.Parse(stored));
    }

    /// <summary>
    /// A hand-edited settings file naming a button that does not exist must
    /// leave a working clicker, not one that presses nothing.
    /// </summary>
    [Theory]
    [InlineData("Wheel")]
    [InlineData("XButton1")]
    [InlineData("7")]
    [InlineData("")]
    [InlineData(null)]
    public void FallsBackToLeftRatherThanToNothing(string? stored)
    {
        Assert.Equal(ClickButton.Left, ClickButtons.Parse(stored));
    }

    /// <summary>
    /// Parse accepts numbers as enum values, so an out-of-range one has to be
    /// refused explicitly or it becomes a button with no flags at all.
    /// </summary>
    [Fact]
    public void RefusesANumberOutsideTheEnum()
    {
        Assert.Equal(ClickButton.Left, ClickButtons.Parse("99"));
    }

    [Fact]
    public void ShipsClickingTheLeftButton()
    {
        Assert.Equal(ClickButton.Left, ClickButtons.Parse(new AppSettings().ClickButton));
    }
}
