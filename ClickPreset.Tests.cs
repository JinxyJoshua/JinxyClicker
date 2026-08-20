using System.Linq;
using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// Preset defaults, which are quietly load-bearing: applying a preset writes its
/// shake values over the sliders, so a stale default here silently resets them.
/// </summary>
public class ClickPresetTests
{
    /// <summary>
    /// The bug this pins: AppSettings moved to 7/9/6/5 and ClickPreset did not,
    /// so every built-in preset still carried 8/20/40/8 and applying one reset
    /// the user's shake to values they had never chosen.
    /// </summary>
    [Fact]
    public void PresetShakeDefaults_MatchTheAppSettingsDefaults()
    {
        var fresh = new AppSettings();
        var preset = new ClickPreset("test", 20, 50);

        Assert.Equal(fresh.ShakeLeft, preset.ShakeLeft);
        Assert.Equal(fresh.ShakeRight, preset.ShakeRight);
        Assert.Equal(fresh.ShakeUp, preset.ShakeUp);
        Assert.Equal(fresh.ShakeDown, preset.ShakeDown);
    }

    [Fact]
    public void EveryBuiltInPreset_CarriesTheDefaultShake()
    {
        var fresh = new AppSettings();

        foreach (ClickPreset preset in PresetStore.Defaults())
        {
            Assert.Equal(fresh.ShakeLeft, preset.ShakeLeft);
            Assert.Equal(fresh.ShakeRight, preset.ShakeRight);
            Assert.Equal(fresh.ShakeUp, preset.ShakeUp);
            Assert.Equal(fresh.ShakeDown, preset.ShakeDown);
        }
    }

    [Fact]
    public void TheBuiltInPresets_AreTheElevenNamedOnes()
    {
        string[] expected =
        {
            "Ish", "Snoopy", "Stunned", "Sky", "Ara", "Spooky",
            "Milo", "Lee", "Sharkiffy", "AraStxr", "YoNoobLike"
        };

        Assert.Equal(expected, PresetStore.Defaults().Select(p => p.Name).ToArray());
    }

    /// <summary>
    /// Names are shown as written, so a lowercase one is a typo rather than a
    /// style. All of these were corrected by hand once already.
    /// </summary>
    [Fact]
    public void EveryPresetNameStartsCapitalised()
    {
        foreach (ClickPreset preset in PresetStore.Defaults())
            Assert.True(char.IsUpper(preset.Name[0]), $"'{preset.Name}' starts lowercase");
    }

    /// <summary>
    /// Ish sits at 193.62, which the old 150 ceiling could not reach.
    /// </summary>
    [Fact]
    public void NoPresetExceedsTheSliderCeiling()
    {
        foreach (ClickPreset preset in PresetStore.Defaults())
        {
            Assert.InRange(preset.Cps, 0, 1000);
            Assert.InRange(preset.Cdc, 0, 100);
        }
    }
}
