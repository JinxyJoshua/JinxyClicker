namespace JinxyClicker;

/// <summary>A rectangle in screen pixels, inclusive of its edges.</summary>
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public bool Contains(int x, int y) => x >= Left && x <= Right && y >= Top && y <= Bottom;
}

/// <summary>
/// Where the pointer has to be for the clicker to stop on its own.
/// </summary>
/// <remarks>
/// This is the way out of a clicker that will not stop, so it is worth being
/// able to test rather than reason about. The arithmetic used to live inside a
/// method that called GetCursorPos itself, which meant the only way to check it
/// was to move a real mouse — and a failsafe nobody can test is a failsafe
/// nobody knows the state of.
///
/// Two zones, deliberately different in kind:
///
/// Corners take a determined throw. They are always on, because the escape
/// hatch must not be behind a setting that can be switched off.
///
/// The taskbar and the top edge are what a competing clicker uses, and they are
/// optional because they fire by accident: on a desktop two monitors wide the
/// top edge is where tabs, menus and title bars live, and crossing it during
/// ordinary play is not a request to stop clicking.
/// </remarks>
public static class StopZones
{
    /// <summary>
    /// How close to a corner counts as being in it.
    /// </summary>
    /// <remarks>
    /// Was 2. All four corners were measured reachable at zero offset on a
    /// 3840x1080 desktop, so 2 was not provably wrong — but it asks the pointer
    /// to settle inside a 3x3 box, and 6 costs nothing. Nobody's cursor rests
    /// six pixels into a corner by accident, and a throw that lands slightly
    /// short still stops the clicker.
    /// </remarks>
    public const int CornerMarginPx = 6;

    /// <summary>How close to the top edge counts as touching it.</summary>
    /// <remarks>
    /// Tighter than the corner margin on purpose. This zone is crossed in
    /// normal use, so it should take actually reaching the edge.
    /// </remarks>
    public const int EdgeMarginPx = 2;

    /// <summary>
    /// Whether the pointer is in a corner of the whole desktop.
    /// </summary>
    /// <param name="desktop">The virtual desktop, spanning every monitor.</param>
    /// <remarks>
    /// Corners rather than edges. With two monitors side by side an edge gets
    /// crossed constantly in normal play, so only the four extreme points of
    /// the arrangement count — reaching one takes deliberately throwing the
    /// mouse, which is exactly the gesture wanted.
    /// </remarks>
    public static bool InCorner(ScreenRect desktop, int x, int y, int margin = CornerMarginPx)
    {
        bool nearLeftOrRight = x - desktop.Left <= margin || desktop.Right - x <= margin;
        bool nearTopOrBottom = y - desktop.Top <= margin || desktop.Bottom - y <= margin;

        // Both, not either — either one of them on its own is an edge.
        return nearLeftOrRight && nearTopOrBottom;
    }

    /// <summary>
    /// Whether the pointer is over the taskbar, or against the top of a screen.
    /// </summary>
    /// <param name="monitor">The full bounds of the screen the pointer is on.</param>
    /// <param name="workArea">
    /// The usable part of that screen. Whatever the monitor has and this does
    /// not is the taskbar, wherever the taskbar has been put — so a taskbar
    /// moved to the left or the top is still found, without asking where it is.
    /// </param>
    public static bool OnTaskbarOrTop(
        ScreenRect monitor, ScreenRect workArea, int x, int y, int margin = EdgeMarginPx)
    {
        if (!monitor.Contains(x, y)) return false;

        if (!workArea.Contains(x, y)) return true;

        return y - monitor.Top <= margin;
    }
}
