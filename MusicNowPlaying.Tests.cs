using Xunit;

namespace JinxyClicker;

public class MusicNowPlayingTests
{
    [Fact]
    public void StripsTheYouTubeMusicSuffix()
    {
        Assert.Equal("Some Song", MusicNowPlaying.Clean("Some Song - YouTube Music"));
    }

    [Fact]
    public void TreatsTheIdleTitleAsNothingPlaying()
    {
        Assert.Null(MusicNowPlaying.Clean("YouTube Music"));
        Assert.Null(MusicNowPlaying.Clean("youtube music"));
    }

    [Fact]
    public void BlankOrNullIsNothingPlaying()
    {
        Assert.Null(MusicNowPlaying.Clean(null));
        Assert.Null(MusicNowPlaying.Clean(""));
        Assert.Null(MusicNowPlaying.Clean("   "));
    }

    [Fact]
    public void KeepsAPlainTitleUnchanged()
    {
        Assert.Equal("Artist — Track", MusicNowPlaying.Clean("Artist — Track"));
    }

    [Fact]
    public void TrimsSurroundingWhitespace()
    {
        Assert.Equal("Track", MusicNowPlaying.Clean("  Track - YouTube Music  "));
    }
}
