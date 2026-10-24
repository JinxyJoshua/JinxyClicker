using System;

namespace JinxyClicker;

/// <summary>
/// Turns what the user types into the Music page's search box into a YouTube
/// Music URL for the embedded web view.
/// </summary>
/// <remarks>
/// YouTube Music rather than Spotify on purpose: it plays for everyone with no
/// sign-in and no Premium, which is the whole point of the feature being free.
/// The page just hosts the real site in a web view, so this is the only logic
/// there is — build the search URL, and fall back to the home page when the box
/// is empty.
/// </remarks>
public static class MusicSearch
{
    /// <summary>The YouTube Music home, used on first open and for an empty search.</summary>
    public const string Home = "https://music.youtube.com/";

    /// <summary>
    /// The URL to navigate the web view to for a search, or <see cref="Home"/>
    /// when nothing was typed.
    /// </summary>
    public static string SearchUrl(string? query)
    {
        string q = query?.Trim() ?? "";
        if (q.Length == 0) return Home;

        // EscapeDataString, not EscapeUriString: the query is a value, so every
        // reserved character (&, /, ?, =) has to be percent-encoded or it would
        // change the URL's structure rather than search for the character.
        return "https://music.youtube.com/search?q=" + Uri.EscapeDataString(q);
    }
}
