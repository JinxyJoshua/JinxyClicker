using System.Collections.Generic;

namespace JinxyClicker;

/// <summary>
/// Turns a won cosmetic into the real thing it unlocks: a cursor cosmetic into a
/// drawable <see cref="CrosshairStyle"/> (written over Roblox's own cursor, the
/// same way the Crosshair page does it) and a wallpaper cosmetic into a
/// <see cref="WallpaperDesign"/> (installed as the app background).
/// </summary>
/// <remarks>
/// Kept apart from the wheel and the window so the mapping can be tested without
/// a screen, and so "equip" can never resolve to nothing. The ids line up with
/// the collectible slices in <c>MainWindow.Rewards</c>'s wheel catalog; every
/// cosmetic slice there has an entry here.
/// </remarks>
public static class RewardCosmetics
{
    // Each cursor cosmetic drawn as a crosshair in its signature colour, bright
    // enough to read in a fight and outlined so it shows on any background.
    private static readonly Dictionary<string, CrosshairStyle> Cursors = new()
    {
        ["mintp"] = new CrosshairStyle(CrosshairShape.CrossDot, Size: 9, Thickness: 3, Gap: 4,
            DotSize: 3, ColorHex: "#4BD47B", DotColorHex: "#FFFFFF"),                    // Mint Pop
        ["frost"] = new CrosshairStyle(CrosshairShape.CircleDot, Size: 8, Thickness: 3, Gap: 4,
            DotSize: 2, ColorHex: "#8FD4FF", DotColorHex: "#FFFFFF"),                    // Frostbite
        ["sakura"] = new CrosshairStyle(CrosshairShape.XDot, Size: 9, Thickness: 3, Gap: 3,
            DotSize: 3, ColorHex: "#FF5BC8", DotColorHex: "#FFFFFF"),                    // Sakura
        ["sm14"] = new CrosshairStyle(CrosshairShape.Cross, Size: 14, Thickness: 2, Gap: 5,
            ColorHex: "#2FD4A8"),                                                       // Smooth 14
        ["galaxy"] = new CrosshairStyle(CrosshairShape.CrossDot, Size: 9, Thickness: 3, Gap: 4,
            DotSize: 3, ColorHex: "#9B5BFA", DotColorHex: "#FFFFFF", GlowPercent: 70),  // Galaxy (Legendary halo)
    };

    // Each wallpaper cosmetic as a dark, drawn background in its signature glow.
    private static readonly Dictionary<string, WallpaperDesign> Wallpapers = new()
    {
        ["neon"] = new WallpaperDesign("Neon Horizon", WallpaperStyle.Wash, "#04070F", "#0A1A2E", "#45B7FF"),
        ["midn"] = new WallpaperDesign("Midnight Contours", WallpaperStyle.Grid, "#04060F", "#0C1230", "#5B7CFA"),
    };

    /// <summary>The crosshair a cursor cosmetic unlocks, or null if the id is not one.</summary>
    public static CrosshairStyle? Cursor(string id) =>
        Cursors.TryGetValue(id, out CrosshairStyle? s) ? s : null;

    /// <summary>The background a wallpaper cosmetic unlocks, or null if the id is not one.</summary>
    public static WallpaperDesign? Wallpaper(string id) =>
        Wallpapers.TryGetValue(id, out WallpaperDesign? d) ? d : null;

    public static bool IsCursor(string id) => Cursors.ContainsKey(id);
    public static bool IsWallpaper(string id) => Wallpapers.ContainsKey(id);

    public static IReadOnlyCollection<string> CursorIds => Cursors.Keys;
    public static IReadOnlyCollection<string> WallpaperIds => Wallpapers.Keys;
}
