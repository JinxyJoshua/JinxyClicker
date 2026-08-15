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
        // These two together are as close to "no textures" as the allowlist
        // allows. Nothing on it removes texturing outright — the flags the
        // community calls "no textures" are these, at quality zero.
        new FastFlag("DFFlagTextureQualityOverrideEnabled", "True",
            "Lets the texture quality below be applied at all."),
        new FastFlag("DFIntTextureQualityOverride", "0",
            "Lowest texture detail. The largest single saving on a weak GPU."),
        new FastFlag("FFlagDebugSkyGray", "True",
            "Replaces the skybox with flat grey. Removes sky rendering entirely."),
        new FastFlag("DFIntDebugFRMQualityLevelOverride", "1",
            "Pins the overall render quality to its lowest level."),
        new FastFlag("FIntDebugForceMSAASamples", "0",
            "Turns off anti-aliasing."),
        new FastFlag("FIntFRMMaxGrassDistance", "0",
            "Stops grass rendering at distance."),
        new FastFlag("FIntFRMMinGrassDistance", "0",
            "Stops grass rendering up close."),
        new FastFlag("FIntGrassMovementReducedMotionFactor", "0",
            "Removes grass animation."),
        new FastFlag("DFFlagDisableDPIScale", "True",
            "Stops the client scaling for display DPI. A real saving on a high-DPI screen."),
        new FastFlag("DFFlagDebugPauseVoxelizer", "True",
            "Freezes voxel lighting. Lighting stops updating, which is the cost.")
    };

    /// <summary>
    /// The graphics backend, as a choice rather than a toggle.
    /// </summary>
    /// <remarks>
    /// Which one is fastest is a property of the GPU and driver, not something
    /// that can be decided here — Vulkan is a large gain on some machines and a
    /// loss on others. So this offers the choice and does not pick.
    ///
    /// Mutually exclusive by nature: the three flags are separate preferences
    /// and setting more than one at a time means nothing, which is why applying
    /// one clears the others rather than merging with them.
    /// </remarks>
    public static IReadOnlyList<FastFlag> AllGraphicsApis { get; } = new[]
    {
        new FastFlag("FFlagDebugGraphicsPreferD3D11", "True", "Direct3D 11."),
        new FastFlag("FFlagDebugGraphicsPreferVulkan", "True", "Vulkan."),
        new FastFlag("FFlagDebugGraphicsPreferOpenGL", "True", "OpenGL.")
    };

    /// <summary>The flag for one API name, or null for "let Roblox decide".</summary>
    public static FastFlag? GraphicsApi(string? name) => name switch
    {
        "D3D11" => AllGraphicsApis[0],
        "Vulkan" => AllGraphicsApis[1],
        "OpenGL" => AllGraphicsApis[2],
        _ => null
    };

    /// <summary>
    /// Clears every API preference, then sets the chosen one. Passing null
    /// leaves none set, which returns the client to its own default.
    /// </summary>
    public static void ApplyGraphicsApi(string? name)
    {
        Reset(AllGraphicsApis);

        FastFlag? chosen = GraphicsApi(name);
        if (chosen != null) Apply(new[] { chosen });
    }

    /// <summary>Which API preference the file currently holds, if any.</summary>
    public static string? CurrentGraphicsApi()
    {
        Dictionary<string, string> current = Read();

        foreach (string name in new[] { "D3D11", "Vulkan", "OpenGL" })
        {
            FastFlag? flag = GraphicsApi(name);

            if (flag != null && current.TryGetValue(flag.Name, out string? value) && value == flag.Value)
                return name;
        }

        return null;
    }

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

    private const string BackupFileName = "ClientAppSettings.jinxy-backup.json";

    /// <summary>
    /// Copies the client's settings aside, once.
    /// </summary>
    /// <remarks>
    /// Written only if no backup exists, so it captures the file as it was
    /// before this app first touched it. Overwriting on every apply would
    /// quickly leave a "backup" of this app's own output.
    /// </remarks>
    public static void Backup()
    {
        try
        {
            string? path = SettingsPath();
            if (path == null || !File.Exists(path)) return;

            string backup = Path.Combine(Path.GetDirectoryName(path)!, BackupFileName);
            if (!File.Exists(backup)) File.Copy(path, backup);
        }
        catch
        {
            // A missing backup must not stop the flags being applied.
        }
    }

    public static bool HasBackup()
    {
        string? folder = FindClientSettingsFolder();
        return folder != null && File.Exists(Path.Combine(folder, BackupFileName));
    }

    /// <summary>Puts the pre-Jinxy file back, if one was captured.</summary>
    public static bool RestoreBackup()
    {
        try
        {
            string? path = SettingsPath();
            if (path == null) return false;

            string backup = Path.Combine(Path.GetDirectoryName(path)!, BackupFileName);
            if (!File.Exists(backup)) return false;

            File.Copy(backup, path, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
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
