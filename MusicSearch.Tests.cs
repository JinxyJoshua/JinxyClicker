using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// The only logic behind the Music page: turning what the user typed into a
/// YouTube Music URL for the embedded view. Everything else is the web view
/// doing its own thing, so this is the one part worth pinning.
/// </summary>
public class MusicSearchTests
{
    [Fact]
    public void SearchUrl_BuildsAYouTubeMusicSearchForTheQuery()
    {
        Assert.Equal("https://music.youtube.com/search?q=daft%20punk",
                     MusicSearch.SearchUrl("daft punk"));
    }

    [Fact]
    public void SearchUrl_EncodesCharactersThatWouldBreakTheUrl()
    {
        // An ampersand in a song title must not start a second query parameter.
        Assert.Equal("https://music.youtube.com/search?q=AC%2FDC%20%26%20friends",
                     MusicSearch.SearchUrl("AC/DC & friends"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SearchUrl_FallsBackToHomeForAnEmptyQuery(string? query)
    {
        Assert.Equal(MusicSearch.Home, MusicSearch.SearchUrl(query));
    }

    [Fact]
    public void SearchUrl_TrimsSurroundingWhitespace()
    {
        Assert.Equal("https://music.youtube.com/search?q=lofi",
                     MusicSearch.SearchUrl("  lofi  "));
    }

    [Fact]
    public void Home_IsTheYouTubeMusicHome()
    {
        Assert.Equal("https://music.youtube.com/", MusicSearch.Home);
    }
}
