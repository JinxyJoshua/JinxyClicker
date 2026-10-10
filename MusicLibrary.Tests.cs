using System;
using System.IO;
using JinxyClicker;
using Xunit;

public class MusicLibraryTests
{
    private static string AppWithMusic(params string[] names)
    {
        string app = Path.Combine(Path.GetTempPath(), "jinxymusic_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(app, "music"));
        foreach (var n in names) File.WriteAllText(Path.Combine(app, "music", n), "x");
        return app;
    }

    [Fact]
    public void Bundled_lists_mp3s()
    {
        string app = AppWithMusic("a.mp3", "b.mp3");
        try { Assert.Equal(2, MusicLibrary.Bundled(app).Count); }
        finally { Directory.Delete(app, true); }
    }

    [Fact]
    public void Resolve_prefers_an_existing_full_path()
    {
        string app = AppWithMusic("a.mp3");
        string own = Path.Combine(Path.GetTempPath(), "own_" + Guid.NewGuid().ToString("N") + ".mp3");
        File.WriteAllText(own, "x");
        try { Assert.Equal(own, MusicLibrary.Resolve(own, app)); }
        finally { Directory.Delete(app, true); File.Delete(own); }
    }

    [Fact]
    public void Resolve_finds_a_bundled_name()
    {
        string app = AppWithMusic("a.mp3");
        try { Assert.Equal(Path.Combine(app, "music", "a.mp3"), MusicLibrary.Resolve("a.mp3", app)); }
        finally { Directory.Delete(app, true); }
    }

    [Fact]
    public void Resolve_falls_back_to_first_bundled_then_null()
    {
        string app = AppWithMusic("a.mp3");
        try { Assert.Equal(Path.Combine(app, "music", "a.mp3"), MusicLibrary.Resolve(null, app)); }
        finally { Directory.Delete(app, true); }

        string empty = Path.Combine(Path.GetTempPath(), "nomusic_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try { Assert.Null(MusicLibrary.Resolve(null, empty)); }
        finally { Directory.Delete(empty, true); }
    }
}
