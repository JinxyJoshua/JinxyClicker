using System;

namespace JinxyClicker;

/// <summary>
/// Turns the Music page's web-view document title into a now-playing line for the
/// music overlay. YouTube Music writes "Song - Artist - YouTube Music" while a
/// track plays and just "YouTube Music" when idle, so this strips the suffix and
/// reports null when nothing is playing.
/// </summary>
public static class MusicNowPlaying
{
    private const string Suffix = " - YouTube Music";
    private const string Idle = "YouTube Music";

    public static string? Clean(string? rawTitle)
    {
        if (rawTitle is null) return null;

        string t = rawTitle.Trim();
        if (t.EndsWith(Suffix, StringComparison.Ordinal))
            t = t[..^Suffix.Length].Trim();

        if (t.Length == 0 || t.Equals(Idle, StringComparison.OrdinalIgnoreCase))
            return null;

        return t;
    }
}
