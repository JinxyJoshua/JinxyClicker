using System.Collections.Generic;
using System.Windows.Input;

namespace JinxyClicker;

/// <summary>
/// What to call a key on screen.
/// </summary>
/// <remarks>
/// WPF's own name for a key is the enum member, which is fine for letters and
/// function keys and unreadable for everything else. Someone who bound the key
/// left of their 1 saw "Oem3" — on the button, in the settings file, and in the
/// notice that says another action took it. None of those are places to show an
/// internal name.
///
/// The table is US-layout, because the punctuation keys are the only ones whose
/// enum name is unreadable and their US legends are what the enum is named
/// after. A different layout gets a slightly wrong symbol rather than "Oem3",
/// which is still the better of the two.
/// </remarks>
public static class KeyNames
{
    private static readonly Dictionary<Key, string> Named = new()
    {
        [Key.Oem3] = "`",
        [Key.OemMinus] = "-",
        [Key.OemPlus] = "=",
        [Key.OemOpenBrackets] = "[",
        [Key.Oem6] = "]",
        [Key.Oem5] = "\\",
        [Key.Oem1] = ";",
        [Key.OemQuotes] = "'",
        [Key.OemComma] = ",",
        [Key.OemPeriod] = ".",
        [Key.OemQuestion] = "/",

        [Key.Space] = "Space",
        [Key.Return] = "Enter",
        [Key.Escape] = "Esc",
        [Key.Back] = "Backspace",
        [Key.Tab] = "Tab",
        [Key.Capital] = "Caps Lock",
        [Key.PageUp] = "Page Up",
        [Key.PageDown] = "Page Down",
        [Key.Snapshot] = "Print Screen",

        [Key.Left] = "Left Arrow",
        [Key.Right] = "Right Arrow",
        [Key.Up] = "Up Arrow",
        [Key.Down] = "Down Arrow",

        [Key.LeftShift] = "Left Shift",
        [Key.RightShift] = "Right Shift",
        [Key.LeftCtrl] = "Left Ctrl",
        [Key.RightCtrl] = "Right Ctrl",
        [Key.LeftAlt] = "Left Alt",
        [Key.RightAlt] = "Right Alt",

        [Key.Add] = "Numpad +",
        [Key.Subtract] = "Numpad -",
        [Key.Multiply] = "Numpad *",
        [Key.Divide] = "Numpad /",
        [Key.Decimal] = "Numpad .",
    };

    /// <summary>The name to show for a key.</summary>
    public static string For(Key key)
    {
        if (Named.TryGetValue(key, out string? named)) return named;

        string raw = key.ToString();

        // D0 to D9 are the number row. The enum has to prefix them because an
        // identifier cannot start with a digit; a button does not.
        if (raw.Length == 2 && raw[0] == 'D' && char.IsDigit(raw[1])) return raw[1].ToString();

        // NumPad0 to NumPad9.
        if (raw.StartsWith("NumPad") && raw.Length == 7 && char.IsDigit(raw[6]))
            return "Numpad " + raw[6];

        return raw;
    }
}
