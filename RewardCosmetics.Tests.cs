using System.Linq;
using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// The won cosmetics are real: each cursor id resolves to a drawable
/// <see cref="CrosshairStyle"/> (written into Roblox's cursors) and each
/// wallpaper id to a <see cref="WallpaperDesign"/> (installed as the app
/// background). These tests pin the mapping so a reward can never equip to
/// "nothing".
/// </summary>
public class RewardCosmeticsTests
{
    [Theory]
    [InlineData("mintp")]
    [InlineData("frost")]
    [InlineData("sakura")]
    [InlineData("sm14")]
    [InlineData("galaxy")]
    public void Every_cursor_cosmetic_resolves_to_a_style(string id)
    {
        CrosshairStyle? style = RewardCosmetics.Cursor(id);
        Assert.NotNull(style);
    }

    [Theory]
    [InlineData("neon", "Neon Horizon")]
    [InlineData("midn", "Midnight Contours")]
    public void Every_wallpaper_cosmetic_resolves_to_a_design(string id, string name)
    {
        WallpaperDesign? design = RewardCosmetics.Wallpaper(id);
        Assert.NotNull(design);
        Assert.Equal(name, design!.Name);
    }

    [Fact]
    public void Cursor_styles_carry_their_signature_colour()
    {
        // The arm colour each cosmetic is known by, so the picture matches the name.
        Assert.Equal("#4BD47B", RewardCosmetics.Cursor("mintp")!.ColorHex);   // mint
        Assert.Equal("#8FD4FF", RewardCosmetics.Cursor("frost")!.ColorHex);   // ice
        Assert.Equal("#FF5BC8", RewardCosmetics.Cursor("sakura")!.ColorHex);  // pink
        Assert.Equal("#2FD4A8", RewardCosmetics.Cursor("sm14")!.ColorHex);    // teal
        Assert.Equal("#9B5BFA", RewardCosmetics.Cursor("galaxy")!.ColorHex);  // violet
    }

    [Fact]
    public void Legendary_galaxy_cursor_glows()
    {
        // The one Legendary cursor earns a halo the common ones do not have.
        Assert.True(RewardCosmetics.Cursor("galaxy")!.HasGlow);
        Assert.False(RewardCosmetics.Cursor("mintp")!.HasGlow);
    }

    [Fact]
    public void Cursor_styles_draw_an_outline_so_they_read_on_any_background()
    {
        foreach (string id in RewardCosmetics.CursorIds)
            Assert.True(RewardCosmetics.Cursor(id)!.Outline);
    }

    [Fact]
    public void Wallpaper_designs_are_dark_enough_to_sit_behind_the_ui()
    {
        // Every ground is a near-black deep colour, like the built-in gallery,
        // so text over it stays readable.
        foreach (string id in RewardCosmetics.WallpaperIds)
        {
            WallpaperDesign d = RewardCosmetics.Wallpaper(id)!;
            Assert.StartsWith("#0", d.Deep);   // #0x xx xx — a dark ground
        }
    }

    [Fact]
    public void An_unknown_id_is_neither_a_cursor_nor_a_wallpaper()
    {
        Assert.Null(RewardCosmetics.Cursor("sd15"));     // a Stardust slice
        Assert.Null(RewardCosmetics.Wallpaper("prem60")); // a Premium slice
        Assert.Null(RewardCosmetics.Cursor("nope"));
        Assert.Null(RewardCosmetics.Wallpaper("nope"));
    }

    [Fact]
    public void No_id_is_both_a_cursor_and_a_wallpaper()
    {
        Assert.Empty(RewardCosmetics.CursorIds.Intersect(RewardCosmetics.WallpaperIds));
    }

    [Fact]
    public void The_id_lists_match_what_resolves()
    {
        foreach (string id in RewardCosmetics.CursorIds)
        {
            Assert.NotNull(RewardCosmetics.Cursor(id));
            Assert.True(RewardCosmetics.IsCursor(id));
        }
        foreach (string id in RewardCosmetics.WallpaperIds)
        {
            Assert.NotNull(RewardCosmetics.Wallpaper(id));
            Assert.True(RewardCosmetics.IsWallpaper(id));
        }
    }
}
