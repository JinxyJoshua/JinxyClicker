using System.Collections.Generic;
using Xunit;

namespace JinxyClicker;

/// <summary>
/// The per-click app gate: decides whether the clicker may click over the app
/// currently under the pointer. Off clicks everywhere; a Whitelist clicks only
/// over listed apps; a Blacklist clicks everywhere except listed apps. Apps are
/// matched by executable file name, case-insensitively.
/// </summary>
public class AppTargetingTests
{
    private static IReadOnlySet<string> Apps(params string[] e) => AppTargeting.Set(e);

    [Fact]
    public void Off_AllowsEverything()
    {
        Assert.True(AppTargeting.Allows(ClickTargetMode.Off, Apps("RobloxPlayerBeta.exe"), "notepad.exe"));
        Assert.True(AppTargeting.Allows(ClickTargetMode.Off, Apps(), null));
    }

    [Fact]
    public void Whitelist_ClicksOnlyOverListedApps()
    {
        var set = Apps("RobloxPlayerBeta.exe");
        Assert.True(AppTargeting.Allows(ClickTargetMode.Whitelist, set, "RobloxPlayerBeta.exe"));
        Assert.False(AppTargeting.Allows(ClickTargetMode.Whitelist, set, "chrome.exe"));
    }

    [Fact]
    public void Whitelist_DeniesWhenAppIsUnknown()
    {
        // Can't tell what's under the pointer → not on the whitelist → no click.
        Assert.False(AppTargeting.Allows(ClickTargetMode.Whitelist, Apps("RobloxPlayerBeta.exe"), null));
    }

    [Fact]
    public void Blacklist_ClicksEverywhereExceptListedApps()
    {
        var set = Apps("chrome.exe");
        Assert.False(AppTargeting.Allows(ClickTargetMode.Blacklist, set, "chrome.exe"));
        Assert.True(AppTargeting.Allows(ClickTargetMode.Blacklist, set, "RobloxPlayerBeta.exe"));
    }

    [Fact]
    public void Blacklist_AllowsWhenAppIsUnknown()
    {
        Assert.True(AppTargeting.Allows(ClickTargetMode.Blacklist, Apps("chrome.exe"), null));
    }

    [Fact]
    public void Matching_IgnoresCaseAndAnyPastedPath()
    {
        var set = Apps("RobloxPlayerBeta.exe");
        Assert.True(AppTargeting.Allows(ClickTargetMode.Whitelist, set, "robloxplayerbeta.exe"));
        Assert.True(AppTargeting.Allows(ClickTargetMode.Whitelist, set, @"C:\Program Files\Roblox\RobloxPlayerBeta.exe"));
    }

    [Fact]
    public void Set_NormalizesAndDropsBlankEntries()
    {
        var set = AppTargeting.Set(new[] { "  Chrome.EXE ", "", "   ", @"D:\games\RobloxPlayerBeta.exe" });
        Assert.Equal(2, set.Count);
        Assert.Contains("chrome.exe", set);
        Assert.Contains("robloxplayerbeta.exe", set);
    }

    [Fact]
    public void Normalize_ReturnsNullForBlank()
    {
        Assert.Null(AppTargeting.Normalize(null));
        Assert.Null(AppTargeting.Normalize("   "));
        Assert.Equal("notepad.exe", AppTargeting.Normalize(@" C:\Windows\Notepad.exe "));
    }
}
