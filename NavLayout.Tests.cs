using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace JinxyClicker;

public class NavLayoutTests
{
    private static (string, IReadOnlyList<string>) G(string group, params string[] items) => (group, items);

    private static readonly List<(string Group, IReadOnlyList<string> Items)> Actual = new()
    {
        G("GroupClicking", "NavClicker", "NavPresets"),
        G("GroupGame", "NavKitWheel", "NavCrosshair"),
        G("GroupPro", "NavSocd", "NavKeystrokes"),
    };

    [Fact]
    public void Reconcile_without_saved_keeps_the_default_order()
    {
        var r = NavLayout.Reconcile(null, Actual);
        Assert.Equal(new[] { "GroupClicking", "GroupGame", "GroupPro" }, r.Select(x => x.Group));
        Assert.Equal(new[] { "NavClicker", "NavPresets" }, r[0].Items);
    }

    [Fact]
    public void Reconcile_applies_saved_group_and_item_order()
    {
        var saved = new NavLayout
        {
            Groups = new()
            {
                new NavGroupOrder { Group = "GroupPro", Items = new() { "NavKeystrokes", "NavSocd" } },
                new NavGroupOrder { Group = "GroupClicking", Items = new() { "NavPresets", "NavClicker" } },
                new NavGroupOrder { Group = "GroupGame", Items = new() { "NavKitWheel", "NavCrosshair" } },
            }
        };

        var r = NavLayout.Reconcile(saved, Actual);

        Assert.Equal(new[] { "GroupPro", "GroupClicking", "GroupGame" }, r.Select(x => x.Group));
        Assert.Equal(new[] { "NavKeystrokes", "NavSocd" }, r.First(x => x.Group == "GroupPro").Items);
        Assert.Equal(new[] { "NavPresets", "NavClicker" }, r.First(x => x.Group == "GroupClicking").Items);
    }

    [Fact]
    public void Reconcile_appends_new_groups_and_items_not_in_the_saved_order()
    {
        // Saved knows only one group with one of its two items.
        var saved = new NavLayout
        {
            Groups = new() { new NavGroupOrder { Group = "GroupGame", Items = new() { "NavCrosshair" } } }
        };

        var r = NavLayout.Reconcile(saved, Actual);

        // The known group comes first; the rest follow in default order.
        Assert.Equal("GroupGame", r[0].Group);
        // Within it, the saved item first, then the new one appended.
        Assert.Equal(new[] { "NavCrosshair", "NavKitWheel" }, r[0].Items);
        // The unseen groups are still present.
        Assert.Contains(r, x => x.Group == "GroupClicking");
        Assert.Contains(r, x => x.Group == "GroupPro");
    }

    [Fact]
    public void Reconcile_lets_an_item_move_to_another_group()
    {
        // NavPresets (default group GroupClicking) is saved under GroupGame.
        var saved = new NavLayout
        {
            Groups = new()
            {
                new NavGroupOrder { Group = "GroupClicking", Items = new() { "NavClicker" } },
                new NavGroupOrder { Group = "GroupGame", Items = new() { "NavPresets", "NavKitWheel", "NavCrosshair" } },
                new NavGroupOrder { Group = "GroupPro", Items = new() { "NavSocd", "NavKeystrokes" } },
            }
        };

        var r = NavLayout.Reconcile(saved, Actual);

        Assert.Equal(new[] { "NavClicker" }, r.First(x => x.Group == "GroupClicking").Items);
        Assert.Equal(new[] { "NavPresets", "NavKitWheel", "NavCrosshair" }, r.First(x => x.Group == "GroupGame").Items);
    }

    [Fact]
    public void Reconcile_drops_saved_entries_that_no_longer_exist()
    {
        var saved = new NavLayout
        {
            Groups = new()
            {
                new NavGroupOrder { Group = "GroupGone", Items = new() { "NavOld" } },
                new NavGroupOrder { Group = "GroupClicking", Items = new() { "NavClicker", "NavRemoved", "NavPresets" } },
            }
        };

        var r = NavLayout.Reconcile(saved, Actual);

        Assert.DoesNotContain(r, x => x.Group == "GroupGone");
        Assert.DoesNotContain(r.First(x => x.Group == "GroupClicking").Items, i => i == "NavRemoved");
    }

    [Fact]
    public void Serialize_round_trips()
    {
        var layout = new NavLayout
        {
            Groups = new() { new NavGroupOrder { Group = "GroupPro", Items = new() { "NavSocd" } } }
        };

        NavLayout? back = NavLayout.Deserialize(NavLayout.Serialize(layout));

        Assert.NotNull(back);
        Assert.Equal("GroupPro", back!.Groups.Single().Group);
        Assert.Equal("NavSocd", back.Groups.Single().Items.Single());
    }

    [Fact]
    public void Deserialize_is_null_for_junk_or_empty()
    {
        Assert.Null(NavLayout.Deserialize(null));
        Assert.Null(NavLayout.Deserialize(""));
        Assert.Null(NavLayout.Deserialize("not json"));
    }
}
