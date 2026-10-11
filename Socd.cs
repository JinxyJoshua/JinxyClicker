namespace JinxyClicker;

/// <summary>What happens to an opposing key pair when both are held.</summary>
public enum SocdMode
{
    /// <summary>Alternate rapidly between the two — auto-strafe side to side.</summary>
    Strafe,

    /// <summary>The most recently pressed direction wins — instant counter-strafe.</summary>
    LastInputWins,

    /// <summary>Both cancel to nothing while held together.</summary>
    Neutral
}

/// <summary>
/// SOCD (Simultaneous Opposing Cardinal Directions) cleaning for one key pair,
/// e.g. A (negative) vs D (positive). Given what is physically held and which was
/// pressed most recently, it says which direction the game should actually get.
/// </summary>
/// <remarks>
/// Pure and side-effect-free: the keyboard hook feeds it the real key state and
/// injects whatever it returns. Keeping the decision here means the tricky part —
/// what should happen when both are down — is testable without a keyboard.
/// </remarks>
public static class Socd
{
    /// <summary>
    /// The directions the game should receive. When only one key is held it passes
    /// straight through; the cleaning only changes the both-held case.
    /// </summary>
    public static (bool Neg, bool Pos) Resolve(bool negDown, bool posDown, bool lastWasPos, SocdMode mode)
    {
        if (!(negDown && posDown)) return (negDown, posDown);

        return mode == SocdMode.Neutral
            ? (false, false)
            : lastWasPos ? (false, true) : (true, false);
    }
}
