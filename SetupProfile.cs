using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace JinxyClicker;

/// <summary>
/// A named bundle of the app's look-and-feel: the crosshair, the click preset,
/// whether the FPS FastFlags are on, and the theme. Applying one flips the whole
/// app to that setup in a single click.
/// </summary>
/// <remarks>
/// Each piece is stored by its own name/value rather than as a copy of the
/// underlying object, so a profile stays small and readable in the settings file
/// and an entry that no longer exists (a deleted crosshair, say) is simply
/// skipped when the setup is applied, not an error.
/// </remarks>
public sealed class SetupProfile
{
    public string Name { get; set; } = "";
    public string? CrosshairName { get; set; }
    public string? ClickPresetName { get; set; }
    public bool FastFlagsOn { get; set; }
    public string? ThemeMode { get; set; }    // "Dark" or "Light"
    public string? AccentColor { get; set; }  // hex, e.g. "#FF4B52"
}

/// <summary>The saved setups, and the small rules for keeping the list tidy.</summary>
public static class SetupProfiles
{
    public const int MaxNameLength = 24;

    /// <summary>Cleans up a typed name, or returns null if it cannot be used.</summary>
    public static string? CleanName(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return null;

        string cleaned = string.Join(" ", typed.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length == 0) return null;

        return cleaned.Length <= MaxNameLength ? cleaned : cleaned[..MaxNameLength].TrimEnd();
    }

    /// <summary>Adds the profile, or replaces an existing one of the same name in place.</summary>
    public static void Save(List<SetupProfile> list, SetupProfile profile)
    {
        int i = list.FindIndex(p => Same(p.Name, profile.Name));
        if (i >= 0) list[i] = profile;
        else list.Add(profile);
    }

    public static void Remove(List<SetupProfile> list, string? name)
        => list.RemoveAll(p => Same(p.Name, name));

    public static SetupProfile? Find(IEnumerable<SetupProfile> list, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (SetupProfile p in list)
            if (Same(p.Name, name)) return p;
        return null;
    }

    private static bool Same(string? a, string? b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Turns what the user typed in the Play "game link" box into something openable:
    /// a bare place id becomes the game's page, a scheme-less link gets https://, and
    /// a full URL or roblox:// deep link is kept as-is. Null when nothing was typed.
    /// </summary>
    public static string? ResolvePlayUrl(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        string t = input.Trim();

        if (t.All(char.IsDigit)) return "https://www.roblox.com/games/" + t;

        if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("roblox://", StringComparison.OrdinalIgnoreCase))
            return t;

        return "https://" + t;
    }

    /// <summary>
    /// Turns the Play "game link" into a launch target: a Roblox deep link that
    /// opens the app straight into the game. Pulls the place id from a bare number
    /// or a game URL; a roblox:// link is kept; anything with no id falls back to
    /// opening the link itself. Null when nothing was typed.
    /// </summary>
    public static string? ResolvePlayLaunch(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        string t = input.Trim();

        if (t.StartsWith("roblox://", StringComparison.OrdinalIgnoreCase)) return t;

        string? id = t.All(char.IsDigit) ? t : null;
        if (id is null)
        {
            Match m = Regex.Match(t, @"(?:games/|placeId=)(\d+)", RegexOptions.IgnoreCase);
            if (m.Success) id = m.Groups[1].Value;
        }

        return id is not null
            ? "roblox://experiences/start?placeId=" + id
            : ResolvePlayUrl(t);
    }
}
