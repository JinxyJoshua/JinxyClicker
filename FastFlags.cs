using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace JinxyClicker;

/// <summary>A single client setting, with the value this app would write.</summary>
public sealed record FastFlag(string Name, string Value, string Description);

/// <summary>
/// Writes Roblox's local client configuration file.
/// </summary>
/// <remarks>
/// Every flag here is on Roblox's published allowlist, announced on the Developer
/// Forum in September 2025. That distinction matters: since the allowlist landed,
/// a flag not on it is simply ignored by the client rather than applied, so
/// offering non-allowlisted flags would be both against the rules and useless.
///
/// The list is Roblox's to change. If they remove one, it stops taking effect —
/// it does not become a violation.
/// </remarks>
public static class FastFlagStore
{
    private const string SettingsFileName = "ClientAppSettings.json";

    /// <summary>
    /// FPS-oriented subset of the allowlist. Deliberately excludes anything
    /// touching character physics — that was the category Roblox treated as
    /// bannable, and it is precisely what the allowlist exists to fence off.
    /// </summary>
    public static IReadOnlyList<FastFlag> FpsBoost { get; } = new[]
    {
        new FastFlag("DFFlagTextureQualityOverrideEnabled", "True",
            "Lets the texture quality below be applied at all."),
        new FastFlag("DFIntTextureQualityOverride", "0",
            "Lowest texture detail. The largest single saving on a weak GPU."),
        new FastFlag("DFIntDebugFRMQualityLevelOverride", "1",
            "Pins the overall render quality to its lowest level."),
        new FastFlag("FIntDebugForceMSAASamples", "0",
            "Turns off anti-aliasing."),
        new FastFlag("FIntFRMMaxGrassDistance", "0",
            "Stops grass rendering at distance."),
        new FastFlag("FIntFRMMinGrassDistance", "0",
            "Stops grass rendering up close."),
        new FastFlag("FIntGrassMovementReducedMotionFactor", "0",
            "Removes grass animation.")
    };

    /// <summary>
    /// The ClientSettings folder of the newest installed client, or null when
    /// Roblox is not installed.
    /// </summary>
    public static string? FindClientSettingsFolder()
    {
        try
        {
            string versions = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox", "Versions");

            if (!Directory.Exists(versions)) return null;

            // A Versions folder holds every client ever installed. The one with
            // the player executable, most recently written, is the live one.
            DirectoryInfo? newest = new DirectoryInfo(versions)
                .GetDirectories()
                .Where(d => File.Exists(Path.Combine(d.FullName, "RobloxPlayerBeta.exe")))
                .OrderByDescending(d => d.LastWriteTimeUtc)
                .FirstOrDefault();

            return newest == null ? null : Path.Combine(newest.FullName, "ClientSettings");
        }
        catch
        {
            return null;
        }
    }

    public static string? SettingsPath()
    {
        string? folder = FindClientSettingsFolder();
        return folder == null ? null : Path.Combine(folder, SettingsFileName);
    }

    public static Dictionary<string, string> Read()
    {
        try
        {
            string? path = SettingsPath();
            if (path == null || !File.Exists(path)) return new Dictionary<string, string>();

            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    public static bool IsApplied(IReadOnlyList<FastFlag> flags)
    {
        Dictionary<string, string> current = Read();

        return flags.Count > 0
               && flags.All(f => current.TryGetValue(f.Name, out string? value) && value == f.Value);
    }

    /// <summary>
    /// Merges the given flags into the existing file rather than replacing it,
    /// so anything the user set by hand survives.
    /// </summary>
    public static void Apply(IReadOnlyList<FastFlag> flags)
    {
        string folder = FindClientSettingsFolder()
            ?? throw new DirectoryNotFoundException("Roblox does not appear to be installed.");

        Directory.CreateDirectory(folder);

        Dictionary<string, string> settings = Read();
        foreach (FastFlag flag in flags) settings[flag.Name] = flag.Value;

        File.WriteAllText(Path.Combine(folder, SettingsFileName),
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Removes only the flags this app added, leaving the rest alone.</summary>
    public static void Reset(IReadOnlyList<FastFlag> flags)
    {
        string? path = SettingsPath();
        if (path == null || !File.Exists(path)) return;

        Dictionary<string, string> settings = Read();
        foreach (FastFlag flag in flags) settings.Remove(flag.Name);

        if (settings.Count == 0)
        {
            File.Delete(path);
            return;
        }

        File.WriteAllText(path,
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
