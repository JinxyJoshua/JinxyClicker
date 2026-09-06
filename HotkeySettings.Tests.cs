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

    // ---- what the clicker is sending decides what can be bound ----

    /// <summary>
    /// Reported on 1.4.5: "when i try to put m2 it says 'side buttons only'".
    /// In this game's vocabulary m2 is the second side button, and a mouse that
    /// sends it as a right click had it refused — by a rule that only ever
    /// needed to protect against the button the clicker is itself sending.
    /// </summary>
    [Theory]
    [InlineData(MouseButton.Right, ClickButton.Left)]
    [InlineData(MouseButton.Right, ClickButton.Middle)]
    [InlineData(MouseButton.Middle, ClickButton.Left)]
    [InlineData(MouseButton.Middle, ClickButton.Right)]
    public void AButtonTheClickerIsNotSendingCanBeBound(MouseButton pressed, ClickButton sending)
    {
        Assert.NotNull(HotkeyBinding.FromMouse(pressed, sending));
    }

    /// <summary>
    /// The one real hazard, and the whole reason for a rule here.
    /// GetAsyncKeyState cannot tell the clicker's own synthesised press from a
    /// real one, so a hotkey on the button being sent re-triggers itself for as
    /// long as the clicker runs.
    /// </summary>
    [Theory]
    [InlineData(MouseButton.Right, ClickButton.Right)]
    [InlineData(MouseButton.Middle, ClickButton.Middle)]
    public void TheButtonTheClickerIsSendingCannotBeBound(MouseButton pressed, ClickButton sending)
    {
        Assert.Null(HotkeyBinding.FromMouse(pressed, sending));
    }

    /// <summary>
    /// Left is refused whatever the clicker is sending, and not for the same
    /// reason. It is how the window is operated — the rebind is armed with it —
    /// so there is no press of it that could mean "bind this".
    /// </summary>
    [Theory]
    [InlineData(ClickButton.Left)]
    [InlineData(ClickButton.Right)]
    [InlineData(ClickButton.Middle)]
    public void LeftIsNeverBindable(ClickButton sending)
    {
        Assert.Null(HotkeyBinding.FromMouse(MouseButton.Left, sending));
    }

    /// <summary>The side buttons are never what the clicker sends, so they always bind.</summary>
    [Theory]
    [InlineData(MouseButton.XButton1, ClickButton.Left)]
    [InlineData(MouseButton.XButton1, ClickButton.Right)]
    [InlineData(MouseButton.XButton2, ClickButton.Middle)]
    public void TheSideButtonsAlwaysBind(MouseButton pressed, ClickButton sending)
    {
        Assert.NotNull(HotkeyBinding.FromMouse(pressed, sending));
    }

    /// <summary>
    /// Whatever the clicker is set to send must be unbindable, and this is
    /// written against the ClickButton enum rather than a list so that teaching
    /// the engine a fourth button fails here instead of shipping a hotkey that
    /// fires itself.
    /// </summary>
    [Fact]
    public void WhicheverButtonTheClickerSendsIsUnbindable()
    {
        foreach (ClickButton sending in System.Enum.GetValues<ClickButton>())
        {
            MouseButton asMouse = sending switch
            {
                ClickButton.Right => MouseButton.Right,
                ClickButton.Middle => MouseButton.Middle,
                _ => MouseButton.Left
            };

            Assert.Null(HotkeyBinding.FromMouse(asMouse, sending));
        }
    }

    /// <summary>
    /// The virtual key a binding carries has to be the same one the engine's own
    /// presses show up as, or the comparison guarding against self-triggering
    /// would be comparing two different things and never match.
    /// </summary>
    [Theory]
    [InlineData(ClickButton.Right, MouseButton.Right)]
    [InlineData(ClickButton.Middle, MouseButton.Middle)]
    public void TheEnginesButtonAndTheBoundButtonAgreeOnTheirKey(ClickButton sending, MouseButton pressed)
    {
        // Bindable while the engine sends something else, so there is a binding
        // to read the key from.
        HotkeyBinding binding = HotkeyBinding.FromMouse(pressed, ClickButton.Left)!;

        Assert.Equal(HotkeyBinding.VirtualKeyOf(sending), binding.VirtualKey);
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
