using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace JinxyClicker;

/// <summary>The saved sidebar order: groups in order, each with its items in order.</summary>
public sealed class NavLayout
{
    public List<NavGroupOrder> Groups { get; set; } = new();

    public static string Serialize(NavLayout layout) => JsonSerializer.Serialize(layout);

    public static NavLayout? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<NavLayout>(json); }
        catch { return null; }
    }

    /// <summary>
    /// Merges a saved order with the sidebar as it is now. Items are free to live in
    /// any group — the saved layout decides which group each item sits in and in what
    /// order, and groups keep their saved order. Anything new (a tab or group added in
    /// a later build) is appended to its default group; anything saved that no longer
    /// exists is dropped. So a customised layout survives updates without hiding new
    /// features or resurrecting removed ones. Pure, testable.
    /// </summary>
    public static List<(string Group, List<string> Items)> Reconcile(
        NavLayout? saved,
        IReadOnlyList<(string Group, IReadOnlyList<string> Items)> actual)
    {
        List<string> validGroups = actual.Select(a => a.Group).ToList();
        var validGroupSet = new HashSet<string>(validGroups);

        // Each item's default group, for appending ones the saved layout never placed.
        var defaultGroupOf = new Dictionary<string, string>();
        foreach (var a in actual)
            foreach (var it in a.Items)
                defaultGroupOf[it] = a.Group;
        var validItems = new HashSet<string>(defaultGroupOf.Keys);

        // Group order: saved groups that still exist, then any new ones in default order.
        var orderedGroups = new List<string>();
        if (saved is not null)
            foreach (var g in saved.Groups)
                if (validGroupSet.Contains(g.Group) && !orderedGroups.Contains(g.Group))
                    orderedGroups.Add(g.Group);
        foreach (var g in validGroups)
            if (!orderedGroups.Contains(g)) orderedGroups.Add(g);

        var placement = orderedGroups.ToDictionary(g => g, _ => new List<string>());
        var placed = new HashSet<string>();

        // Place items where the saved layout put them (any group), in saved order.
        if (saved is not null)
            foreach (var g in saved.Groups)
            {
                if (!placement.TryGetValue(g.Group, out List<string>? into)) continue;
                foreach (var it in g.Items)
                    if (validItems.Contains(it) && placed.Add(it)) into.Add(it);
            }

        // Append anything never placed to its default group (default order).
        foreach (var a in actual)
            foreach (var it in a.Items)
                if (placed.Add(it))
                {
                    string target = placement.ContainsKey(defaultGroupOf[it]) ? defaultGroupOf[it] : orderedGroups[0];
                    placement[target].Add(it);
                }

        return orderedGroups.Select(g => (g, placement[g])).ToList();
    }
}

public sealed class NavGroupOrder
{
    public string Group { get; set; } = "";
    public List<string> Items { get; set; } = new();
}
