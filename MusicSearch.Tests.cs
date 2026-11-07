using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// The Music page just hosts YouTube Music in a web view and searching happens
/// inside that page, so the only thing worth pinning is the home URL it opens to.
/// </summary>
public class MusicSearchTests
{
    [Fact]
    public void Home_IsTheYouTubeMusicHome()
    {
        Assert.Equal("https://music.youtube.com/", MusicSearch.Home);
    }
}
