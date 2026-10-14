using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace JinxyClicker;

public class RobloxFontsTests
{
    // A throwaway folder that mimics Roblox's content\fonts: a families\ dir with
    // JSON descriptors (some faces local, some cloud assets), plus a custom font
    // kept OUTSIDE the folder, exactly as it is in the app (it lives in app data).
    private static string MakeFontsDir(out string customFont)
    {
        string dir = Path.Combine(Path.GetTempPath(), "jinxyfonttest-" + Path.GetRandomFileName());
        string families = Path.Combine(dir, "families");
        Directory.CreateDirectory(families);

        File.WriteAllText(Path.Combine(families, "BuilderSans.json"),
            "{\"name\":\"Builder Sans\",\"faces\":[" +
            "{\"name\":\"Regular\",\"weight\":400,\"assetId\":\"rbxasset://fonts/BuilderSans-Regular.otf\"}," +
            "{\"name\":\"Thin\",\"weight\":100,\"assetId\":\"rbxassetid://16480735997\"}]}");
        File.WriteAllText(Path.Combine(families, "Gotham.json"),
            "{\"name\":\"Gotham\",\"faces\":[{\"name\":\"Regular\",\"weight\":400,\"assetId\":\"rbxassetid://12345\"}]}");

        // Physical font files beside the families dir, as in a real install: a UI
        // font the new chat references by file path, plus an emoji/CJK fallback that
        // must NOT be clobbered (a latin custom font would turn those into boxes).
        File.WriteAllText(Path.Combine(dir, "BuilderSans-Regular.otf"), "ORIGINAL-BUILDERSANS");
        File.WriteAllText(Path.Combine(dir, "RobloxEmoji.ttf"), "ORIGINAL-EMOJI");
        File.WriteAllText(Path.Combine(dir, "NotoSansThaiUI-Regular.ttf"), "ORIGINAL-NOTO");

        customFont = Path.Combine(Path.GetTempPath(), "jinxycustom-" + Path.GetRandomFileName() + ".ttf");
        File.WriteAllText(customFont, "CUSTOM-FONT-BYTES");
        return dir;
    }

    [Theory]
    [InlineData("BuilderSans-Regular.otf", true)]
    [InlineData("BuilderSans-Medium.otf", true)]
    [InlineData("SourceSansPro-Regular.ttf", true)]
    [InlineData("Bangers-Regular.ttf", true)]
    [InlineData("NotoSansThaiUI-Regular.ttf", false)]   // non-latin fallback
    [InlineData("NotoSansCJKjp-Regular.ttf", false)]    // CJK fallback
    [InlineData("RobloxEmoji.ttf", false)]
    [InlineData("TwemojiMozilla.ttf", false)]
    [InlineData("JinxyCustom.ttf", false)]              // our own drop-in
    [InlineData("gamecontrollerdb.txt", false)]         // not a font
    public void IsReplaceableFontFile_takes_latin_ui_fonts_not_fallbacks(string name, bool expected)
        => Assert.Equal(expected, RobloxFonts.IsReplaceableFontFile(name));

    [Fact]
    public void PointFamilyAtFont_repoints_every_assetId_local_and_cloud()
    {
        string json = "{\"faces\":[{\"name\":\"Regular\",\"assetId\":\"rbxasset://fonts/X.otf\"}," +
                      "{\"name\":\"Thin\",\"assetId\":\"rbxassetid://999\"}]}";

        string r = RobloxFonts.PointFamilyAtFont(json, "rbxasset://fonts/JinxyCustom.ttf");

        Assert.DoesNotContain("rbxassetid://999", r);
        Assert.DoesNotContain("X.otf", r);
        Assert.Equal(2, Regex.Matches(r, Regex.Escape("rbxasset://fonts/JinxyCustom.ttf")).Count);
        // Names are left intact.
        Assert.Contains("\"name\":\"Regular\"", r);
        Assert.Contains("\"name\":\"Thin\"", r);
    }

    [Fact]
    public void Apply_repoints_every_family_including_cloud_faces_and_drops_the_font()
    {
        string dir = MakeFontsDir(out string custom);
        try
        {
            int n = RobloxFonts.Apply(dir, custom);

            Assert.True(n >= 2);
            Assert.True(File.Exists(Path.Combine(dir, "JinxyCustom.ttf")));

            string builder = File.ReadAllText(Path.Combine(dir, "families", "BuilderSans.json"));
            Assert.Contains("rbxasset://fonts/JinxyCustom.ttf", builder);
            Assert.DoesNotContain("rbxassetid://", builder);          // cloud face repointed too
            Assert.DoesNotContain("BuilderSans-Regular.otf", builder); // local face repointed
            Assert.True(File.Exists(Path.Combine(dir, "families", "BuilderSans.json.jinxybak")));

            // The physical UI font file is overwritten with the custom font (so the
            // new chat, which references the file directly, picks it up), backed up,
            // while the emoji/CJK fallbacks are left alone.
            Assert.Equal("CUSTOM-FONT-BYTES", File.ReadAllText(Path.Combine(dir, "BuilderSans-Regular.otf")));
            Assert.True(File.Exists(Path.Combine(dir, "BuilderSans-Regular.otf.jinxybak")));
            Assert.Equal("ORIGINAL-EMOJI", File.ReadAllText(Path.Combine(dir, "RobloxEmoji.ttf")));
            Assert.Equal("ORIGINAL-NOTO", File.ReadAllText(Path.Combine(dir, "NotoSansThaiUI-Regular.ttf")));
            Assert.False(File.Exists(Path.Combine(dir, "RobloxEmoji.ttf.jinxybak")));
        }
        finally { Cleanup(dir, custom); }
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
        finally { Cleanup(dir, custom); }
    }

    [Fact]
    public void Reset_restores_originals_and_removes_the_custom_font()
    {
        string dir = MakeFontsDir(out string custom);
        try
        {
            RobloxFonts.Apply(dir, custom);
            int restored = RobloxFonts.Reset(dir);

            Assert.True(restored >= 2);
            string builder = File.ReadAllText(Path.Combine(dir, "families", "BuilderSans.json"));
            Assert.Contains("rbxasset://fonts/BuilderSans-Regular.otf", builder);
            Assert.DoesNotContain("JinxyCustom", builder);
            Assert.False(File.Exists(Path.Combine(dir, "JinxyCustom.ttf")));
            Assert.False(File.Exists(Path.Combine(dir, "families", "BuilderSans.json.jinxybak")));

            // The physical font file is restored to its original and its backup removed.
            Assert.Equal("ORIGINAL-BUILDERSANS", File.ReadAllText(Path.Combine(dir, "BuilderSans-Regular.otf")));
            Assert.False(File.Exists(Path.Combine(dir, "BuilderSans-Regular.otf.jinxybak")));
        }
        finally { Cleanup(dir, custom); }
    }

    [Fact]
    public void Apply_cleans_up_its_backup_when_a_family_cannot_be_written()
    {
        // One family only, held open so the rewrite fails after the backup is made.
        string dir = Path.Combine(Path.GetTempPath(), "jinxyfonttest-" + Path.GetRandomFileName());
        string families = Path.Combine(dir, "families");
        Directory.CreateDirectory(families);
        string only = Path.Combine(families, "BuilderSans.json");
        File.WriteAllText(only, "{\"faces\":[{\"assetId\":\"rbxassetid://1\"}]}");
        string custom = Path.Combine(Path.GetTempPath(), "jinxycustom-" + Path.GetRandomFileName() + ".ttf");
        File.WriteAllText(custom, "CUSTOM");
        try
        {
            using (new FileStream(only, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.ThrowsAny<System.Exception>(() => RobloxFonts.Apply(dir, custom));
            }
            // No stray backup, so IsApplied stays false and re-apply isn't blocked.
            Assert.False(File.Exists(only + ".jinxybak"));
            Assert.False(RobloxFonts.IsApplied(dir));
        }
        finally { Cleanup(dir, custom); }
    }

    private static void Cleanup(string dir, string customFont)
    {
        try { Directory.Delete(dir, true); } catch { }
        try { File.Delete(customFont); } catch { }
    }
}
