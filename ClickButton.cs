using System;

namespace JinxyClicker;

/// <summary>Which physical button the click engine presses.</summary>
public enum ClickButton
{
    Left,
    Right,
    Middle
}

/// <summary>
/// The Windows flags behind each button, and what to call it on screen.
/// </summary>
/// <remarks>
/// Split out because a press and its release are sent separately — the duty
/// cycle puts real time between them — and pairing the wrong two flags leaves a
/// button held down across the whole desktop with nothing to release it. Kept
/// here so the pairing is one table that can be tested rather than constants
/// picked out by hand at three call sites.
/// </remarks>
public static class ClickButtons
{
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;
    private const uint RightDown = 0x0008;
    private const uint RightUp = 0x0010;
    private const uint MiddleDown = 0x0020;
    private const uint MiddleUp = 0x0040;

    public static uint DownFlag(ClickButton button) => button switch
    {
        ClickButton.Right => RightDown,
        ClickButton.Middle => MiddleDown,
        _ => LeftDown
    };

    public static uint UpFlag(ClickButton button) => button switch
    {
        ClickButton.Right => RightUp,
        ClickButton.Middle => MiddleUp,
        _ => LeftUp
    };

    /// <summary>What the button is called where it is shown.</summary>
    public static string Label(ClickButton button) => button switch
    {
        ClickButton.Right => "Right",
        ClickButton.Middle => "Wheel",
        _ => "Left"
    };

    /// <summary>
    /// Reads a stored or tag value back into a button.
    /// </summary>
    /// <remarks>
    /// Anything unrecognised is the left button rather than a failure. A
    /// hand-edited settings file naming a button that does not exist should
    /// leave a working clicker, not one that presses nothing.
    /// </remarks>
    public static ClickButton Parse(string? name) =>
        Enum.TryParse(name, ignoreCase: true, out ClickButton button)
        && Enum.IsDefined(button)
            ? button
            : ClickButton.Left;
}
