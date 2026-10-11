using System.IO;
using Xunit;

namespace JinxyClicker;

public class RobloxFontsTests
{
    // A throwaway folder that mimics Roblox's content\fonts: a few default faces
    // we replace, one unrelated file we must not touch, and a custom font to apply.
    private static string MakeFontsDir(out string customFont)
    {
        string dir = Path.Combine(Path.GetTempPath(), "jinxyfonttest-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "BuilderSans-Regular.otf"), "ORIGINAL-builder-regular");
        File.WriteAllText(Path.Combine(dir, "BuilderSans-Bold.otf"), "ORIGINAL-builder-bold");
        File.WriteAllText(Path.Combine(dir, "SourceSansPro-Regular.ttf"), "ORIGINAL-source-regular");
        File.WriteAllText(Path.Combine(dir, "RobloxEmoji.ttf"), "EMOJI-should-not-change");
        customFont = Path.Combine(dir, "my-custom.ttf");
        File.WriteAllText(customFont, "CUSTOM-FONT-BYTES");
        return dir;
    }

    [Fact]
    public void Apply_replaces_default_faces_and_backs_up_originals()
    {
        string dir = MakeFontsDir(out string custom);
        try
        {
            int n = RobloxFonts.Apply(dir, custom);

            Assert.True(n >= 3);
            Assert.Equal("CUSTOM-FONT-BYTES", File.ReadAllText(Path.Combine(dir, "BuilderSans-Regular.otf")));
            Assert.Equal("CUSTOM-FONT-BYTES", File.ReadAllText(Path.Combine(dir, "SourceSansPro-Regular.ttf")));
            Assert.Equal("ORIGINAL-builder-regular", File.ReadAllText(Path.Combine(dir, "BuilderSans-Regular.otf.jinxybak")));
            // An unrelated font (emoji) must be left alone.
            Assert.Equal("EMOJI-should-not-change", File.ReadAllText(Path.Combine(dir, "RobloxEmoji.ttf")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void IsApplied_is_true_after_apply_and_false_after_reset()
    {
        string dir = MakeFontsDir(out string custom);
        try
        {
            Assert.False(RobloxFonts.IsApplied(dir));
            RobloxFonts.Apply(dir, custom);
            Assert.True(RobloxFonts.IsApplied(dir));
            RobloxFonts.Reset(dir);
            Assert.False(RobloxFonts.IsApplied(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Reset_restores_the_original_fonts_and_clears_backups()
    {
        string dir = MakeFontsDir(out string custom);
        try
        {
            RobloxFonts.Apply(dir, custom);
            int restored = RobloxFonts.Reset(dir);

            Assert.True(restored >= 3);
            Assert.Equal("ORIGINAL-builder-regular", File.ReadAllText(Path.Combine(dir, "BuilderSans-Regular.otf")));
            Assert.Equal("ORIGINAL-source-regular", File.ReadAllText(Path.Combine(dir, "SourceSansPro-Regular.ttf")));
            Assert.False(File.Exists(Path.Combine(dir, "BuilderSans-Regular.otf.jinxybak")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Apply_cleans_up_its_backup_when_a_face_cannot_be_overwritten()
    {
        string dir = MakeFontsDir(out string custom);
        string target = Path.Combine(dir, "BuilderSans-Regular.otf");
        try
        {
            // Hold the face open for reading only: the backup copy can still read
            // it, but the overwrite needs write access and fails — the way a
            // running Roblox holds its font files.
            using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.ThrowsAny<Exception>(() => RobloxFonts.Apply(dir, custom));
            }

            // The backup it made just before the failed overwrite must be removed,
            // or IsApplied would lie forever and block every future re-apply.
            Assert.False(File.Exists(target + ".jinxybak"));
            Assert.False(RobloxFonts.IsApplied(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Applying_a_second_font_keeps_the_true_original_in_the_backup()
    {
        string dir = MakeFontsDir(out string custom);
        try
        {
            RobloxFonts.Apply(dir, custom);
            string custom2 = Path.Combine(dir, "custom2.ttf");
            File.WriteAllText(custom2, "CUSTOM-2");
            RobloxFonts.Apply(dir, custom2);

            // The backup must still hold the pristine original, not the first custom.
            Assert.Equal("ORIGINAL-builder-regular", File.ReadAllText(Path.Combine(dir, "BuilderSans-Regular.otf.jinxybak")));
            RobloxFonts.Reset(dir);
            Assert.Equal("ORIGINAL-builder-regular", File.ReadAllText(Path.Combine(dir, "BuilderSans-Regular.otf")));
        }
        finally { Directory.Delete(dir, true); }
    }
}
