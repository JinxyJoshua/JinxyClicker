using System.Windows.Input;
using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// What a key is called on screen.
/// </summary>
/// <remarks>
/// Someone who bound the key left of their 1 saw "Oem3" on the button, in the
/// settings file, and in the notice saying another action had taken it.
/// </remarks>
public class KeyNamesTests
{
    /// <summary>The one that was actually reported.</summary>
    [Fact]
    public void TheKeyLeftOfOneIsNotCalledOem3()
    {
        Assert.Equal("`", KeyNames.For(Key.Oem3));
    }

    [Theory]
    [InlineData(Key.OemMinus, "-")]
    [InlineData(Key.OemPlus, "=")]
    [InlineData(Key.OemComma, ",")]
    [InlineData(Key.OemPeriod, ".")]
    [InlineData(Key.OemQuestion, "/")]
    [InlineData(Key.OemQuotes, "'")]
    public void PunctuationReadsAsThePunctuation(Key key, string expected)
    {
        Assert.Equal(expected, KeyNames.For(key));
    }

    /// <summary>
    /// The enum prefixes the number row with D because an identifier cannot
    /// start with a digit. A button has no such problem.
    /// </summary>
    [Theory]
    [InlineData(Key.D0, "0")]
    [InlineData(Key.D5, "5")]
    [InlineData(Key.D9, "9")]
    public void TheNumberRowIsJustTheNumber(Key key, string expected)
    {
        Assert.Equal(expected, KeyNames.For(key));
    }

    [Theory]
    [InlineData(Key.NumPad0, "Numpad 0")]
    [InlineData(Key.NumPad7, "Numpad 7")]
    public void TheNumpadSaysSo(Key key, string expected)
    {
        Assert.Equal(expected, KeyNames.For(key));
    }

    [Theory]
    [InlineData(Key.Return, "Enter")]
    [InlineData(Key.Escape, "Esc")]
    [InlineData(Key.Back, "Backspace")]
    [InlineData(Key.PageUp, "Page Up")]
    public void TheLongOnesReadAsTheirLegend(Key key, string expected)
    {
        Assert.Equal(expected, KeyNames.For(key));
    }

    /// <summary>Letters and function keys were already fine and must stay so.</summary>
    [Theory]
    [InlineData(Key.A, "A")]
    [InlineData(Key.Z, "Z")]
    [InlineData(Key.F6, "F6")]
    [InlineData(Key.F12, "F12")]
    public void WhatWasAlreadyReadableIsLeftAlone(Key key, string expected)
    {
        Assert.Equal(expected, KeyNames.For(key));
    }

    /// <summary>
    /// Nothing may come back empty. A blank button reads as no binding at all,
    /// which is the one thing worse than an ugly name.
    /// </summary>
    [Fact]
    public void EveryKeyGetsSomeName()
    {
        foreach (Key key in System.Enum.GetValues<Key>())
        {
            if (key == Key.None) continue;

            Assert.False(string.IsNullOrWhiteSpace(KeyNames.For(key)), key.ToString());
        }
    }

    /// <summary>
    /// A binding restored from a file keeps its key but not its stored label,
    /// so a macro bound before the names were readable stops reading "Oem3".
    /// </summary>
    [Theory]
    [InlineData(0xC0, "`")]
    [InlineData(0x5A, "Z")]
    [InlineData(0x75, "F6")]
    [InlineData(0x05, "Mouse 4")]
    [InlineData(0x06, "Mouse 5")]
    [InlineData(0x02, "Mouse 2")]
    [InlineData(0x04, "Mouse 3")]
    public void AStoredKeyIsNamedFromTheKey(int virtualKey, string expected)
    {
        Assert.Equal(expected, HotkeyBinding.Describe(virtualKey));
    }

    [Fact]
    public void AKeyWindowsCannotMapStillGetsSomething()
    {
        Assert.False(string.IsNullOrWhiteSpace(HotkeyBinding.Describe(0xFE)));
    }

    /// <summary>
    /// A binding made now has to match one restored from a settings file
    /// written before, or a rebind would look like it had not taken.
    /// </summary>
    [Fact]
    public void TheNameIsWhatABindingCarries()
    {
        Assert.Equal(KeyNames.For(Key.Oem3), HotkeyBinding.FromKey(Key.Oem3).Name);
    }
}
