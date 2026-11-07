namespace JinxyClicker;

/// <summary>
/// The YouTube Music home the Music page's embedded web view opens to. Searching
/// happens inside YouTube Music's own page, so there is no URL to build.
/// </summary>
/// <remarks>
/// YouTube Music rather than Spotify on purpose: it plays for everyone with no
/// sign-in and no Premium, which is the whole point of the feature being free.
/// </remarks>
public static class MusicSearch
{
    /// <summary>The YouTube Music home the web view opens to.</summary>
    public const string Home = "https://music.youtube.com/";
}
