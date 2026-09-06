using System.Linq;
using System.Windows.Input;
using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// Binding a mouse button to a hotkey.
/// </summary>
/// <remarks>
/// A user reported the app "would not let them bind their mouse", meaning the
/// side buttons. Those were always accepted and measurably work, so what these
/// pin is the boundary either side of that: the two buttons that must bind, and
/// the three that must not.
/// </remarks>
public class HotkeyBindingMouseTests
{
    [Theory]
    [InlineData(MouseButton.XButton1, 0x05, "Mouse 4")]
    [InlineData(MouseButton.XButton2, 0x06, "Mouse 5")]
    public void TheSideButtonsBind(MouseButton button, int virtualKey, string name)
    {
        HotkeyBinding? binding = HotkeyBinding.FromMouse(button);

        Assert.NotNull(binding);
        Assert.Equal(virtualKey, binding!.VirtualKey);
        Assert.Equal(name, binding.Name);
        Assert.True(binding.IsValid);
    }

    /// <summary>
    /// All three, not just left.
    /// </summary>
    /// <remarks>
    /// The clicker sends one of left, right and middle, and which one is a
    /// setting on the clicker page. GetAsyncKeyState cannot tell a synthesised
    /// press from a real one, so binding any of the three risks an action that
    /// fires itself for as long as the clicker runs.
    ///
    /// Right and middle were briefly allowed, on the belief that the app only
    /// ever sends left. It does not — ClickButtons has the flags for all three
    /// and the BUTTON selector chooses between them. This is that regression.
    /// </remarks>
    [Theory]
    [InlineData(MouseButton.Left)]
    [InlineData(MouseButton.Right)]
    [InlineData(MouseButton.Middle)]
    public void TheButtonsTheClickerCanSendAreRefused(MouseButton button)
    {
        Assert.Null(HotkeyBinding.FromMouse(button));
    }

    /// <summary>
    /// Every button the clicker can be set to send must be one that cannot be
    /// bound. Written against the ClickButton enum rather than a list, so
    /// teaching the clicker a fourth button fails here instead of shipping a
    /// binding that fires itself.
    /// </summary>
    [Fact]
    public void NoButtonTheClickerCanSendIsBindable()
    {
        foreach (ClickButton sendable in System.Enum.GetValues<ClickButton>())
        {
            MouseButton asMouse = sendable switch
            {
                ClickButton.Right => MouseButton.Right,
                ClickButton.Middle => MouseButton.Middle,
                _ => MouseButton.Left
            };

            Assert.Null(HotkeyBinding.FromMouse(asMouse));
        }
    }

    [Fact]
    public void TheTwoSideButtonsDoNotShareAVirtualKey()
    {
        Assert.NotEqual(
            HotkeyBinding.FromMouse(MouseButton.XButton1)!.VirtualKey,
            HotkeyBinding.FromMouse(MouseButton.XButton2)!.VirtualKey);
    }

    /// <summary>
    /// A mouse binding has to survive a round trip through the settings file,
    /// which stores the virtual key rather than the button.
    /// </summary>
    [Fact]
    public void AMouseBindingIsJustAVirtualKey()
    {
        HotkeyBinding mouse = HotkeyBinding.FromMouse(MouseButton.XButton1)!;
        HotkeyBinding rebuilt = new(mouse.VirtualKey, mouse.Name);

        Assert.Equal(mouse, rebuilt);
    }

    // ---- capture from the poll thread ----

    /// <summary>
    /// The poll thread captures the side buttons too, and it sees virtual keys
    /// rather than buttons. Both routes have to produce the same binding, or a
    /// button bound one way would read differently from the same button bound
    /// the other.
    /// </summary>
    [Theory]
    [InlineData(MouseButton.XButton1, 0x05)]
    [InlineData(MouseButton.XButton2, 0x06)]
    public void BothRoutesToASideButtonAgree(MouseButton button, int virtualKey)
    {
        Assert.Equal(HotkeyBinding.FromMouse(button), HotkeyBinding.FromSideButtonKey(virtualKey));
    }

    /// <summary>
    /// Only the side pair is polled for. Left is what arms the rebind in the
    /// first place, so polling for it would capture the click on the button
    /// rather than the button the user then pressed — and right and middle are
    /// refused for the same reason the window refuses them.
    /// </summary>
    [Theory]
    [InlineData(0x01)]  // left
    [InlineData(0x02)]  // right
    [InlineData(0x04)]  // middle
    [InlineData(0x00)]
    [InlineData(0x70)]  // F1 — a keyboard key, which the window already handles
    public void ThePollOnlyEverCapturesTheSideButtons(int virtualKey)
    {
        Assert.Null(HotkeyBinding.FromSideButtonKey(virtualKey));
    }
}
