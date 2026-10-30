using System.Linq;
using Xunit;

namespace JinxyClicker;

/// <summary>
/// The Quality ↔ Max FPS slider: one dial from full Roblox quality (level 10)
/// to lowest-quality/highest-FPS (level 1), mapped to Roblox's own allowlisted
/// graphics flags. These pin the mapping so a reviewer can see exactly what each
/// step writes.
/// </summary>
public class GraphicsQualityTests
{
    [Fact]
    public void MaxQuality_WritesNothing_SoRobloxRendersFully()
    {
        // Level 10 is "leave Roblox alone" — no overrides, full quality.
        Assert.Empty(GraphicsQuality.FlagsFor(GraphicsQuality.MaxLevel));
    }

    [Fact]
    public void MaxFps_PinsTheLowestOfEverything()
    {
        GraphicsPreset p = GraphicsQuality.PresetFor(GraphicsQuality.MinLevel);

        Assert.Equal(1, p.FrmLevel);       // lowest render quality
        Assert.Equal(0, p.TextureQuality); // lowest textures
        Assert.Equal(0, p.Msaa);           // no anti-aliasing
        Assert.True(p.GraySky);            // no sky rendering
        Assert.True(p.KillGrass);          // no grass
    }

    [Fact]
    public void MaxQualityPreset_IsTheTopOfEveryScale()
    {
        GraphicsPreset p = GraphicsQuality.PresetFor(GraphicsQuality.MaxLevel);

        Assert.Equal(21, p.FrmLevel);      // Roblox's top FRM quality level
        Assert.Equal(3, p.TextureQuality);
        Assert.Equal(4, p.Msaa);
        Assert.False(p.GraySky);
        Assert.False(p.KillGrass);
    }

    [Fact]
    public void RaisingTheLevel_NeverLowersQuality()
    {
        for (int level = GraphicsQuality.MinLevel; level < GraphicsQuality.MaxLevel; level++)
        {
            GraphicsPreset lo = GraphicsQuality.PresetFor(level);
            GraphicsPreset hi = GraphicsQuality.PresetFor(level + 1);

            Assert.True(hi.FrmLevel >= lo.FrmLevel);
            Assert.True(hi.TextureQuality >= lo.TextureQuality);
            Assert.True(hi.Msaa >= lo.Msaa);
        }
    }

    [Fact]
    public void LowEnd_WritesTheRealRobloxFlagsForEachSetting()
    {
        var flags = GraphicsQuality.FlagsFor(GraphicsQuality.MinLevel);
        var names = flags.Select(f => f.Name).ToList();

        Assert.Contains("DFIntDebugFRMQualityLevelOverride", names);
        Assert.Contains("DFIntTextureQualityOverride", names);
        Assert.Contains("FIntDebugForceMSAASamples", names);
        Assert.Contains("FFlagDebugSkyGray", names);

        // Every flag it writes is one it also knows how to clear, so Reset is total.
        Assert.All(names, n => Assert.Contains(n, GraphicsQuality.AllManagedFlags.Select(f => f.Name)));
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(99)]
    public void LevelIsClampedToItsRange(int outOfRange)
    {
        GraphicsPreset p = GraphicsQuality.PresetFor(outOfRange);
        Assert.InRange(p.FrmLevel, 1, 21);
    }
}
