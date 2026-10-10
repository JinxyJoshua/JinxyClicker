using System;
using System.Collections.Generic;

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
}
