using System;
using System.Collections.Generic;

namespace JinxyClicker;

/// <summary>One slider step, as the graphics knobs it resolves to.</summary>
public sealed record GraphicsPreset(int FrmLevel, int TextureQuality, int Msaa, bool GraySky, bool KillGrass);

/// <summary>
/// The Quality ↔ Max FPS slider. One dial that bundles Roblox's own allowlisted
/// graphics flags — render level, texture quality, anti-aliasing, grass and sky —
/// so a player does not have to set six flags by hand to trade looks for frames.
/// </summary>
/// <remarks>
/// Level 10 is "leave Roblox alone": no overrides, the client renders at full
/// quality. Every lower level pins progressively lower settings, down to level 1
/// which is the lowest the allowlist permits. All of it is the same handful of
/// flags already used by the FPS-boost bundle (see <see cref="FastFlagStore"/>),
/// so it is reversible the same way — remove the flags and Roblox is itself again.
/// </remarks>
public static class GraphicsQuality
{
    public const int MinLevel = 1;    // max FPS, lowest quality
    public const int MaxLevel = 10;   // full Roblox quality, no overrides

    public static GraphicsPreset PresetFor(int level)
    {
        level = Math.Clamp(level, MinLevel, MaxLevel);
        double t = (level - MinLevel) / (double)(MaxLevel - MinLevel);   // 0..1

        int frm = (int)Math.Round(t * 20) + 1;                 // 1..21 (Roblox FRM quality)
        int texture = (int)Math.Round(t * 3);                  // 0..3
        int msaa = level <= 3 ? 0 : level <= 6 ? 1 : level <= 8 ? 2 : 4;
        bool graySky = level <= 2;
        bool killGrass = level <= 3;

        return new GraphicsPreset(frm, texture, msaa, graySky, killGrass);
    }

    /// <summary>
    /// The flags to write for a level. Empty at <see cref="MaxLevel"/>, where the
    /// right thing is to remove every override and let Roblox render fully.
    /// </summary>
    public static IReadOnlyList<FastFlag> FlagsFor(int level)
    {
        if (Math.Clamp(level, MinLevel, MaxLevel) >= MaxLevel)
            return Array.Empty<FastFlag>();

        GraphicsPreset p = PresetFor(level);

        var flags = new List<FastFlag>
        {
            new("DFIntDebugFRMQualityLevelOverride", p.FrmLevel.ToString(), "Overall render quality level."),
            new("DFFlagTextureQualityOverrideEnabled", "True", "Lets the texture quality below apply."),
            new("DFIntTextureQualityOverride", p.TextureQuality.ToString(), "Texture detail, 0 (lowest) to 3."),
            new("FIntDebugForceMSAASamples", p.Msaa.ToString(), "Anti-aliasing samples.")
        };

        if (p.GraySky)
            flags.Add(new("FFlagDebugSkyGray", "True", "Flat grey sky — no sky rendering."));

        if (p.KillGrass)
        {
            flags.Add(new("FIntFRMMaxGrassDistance", "0", "No grass at distance."));
            flags.Add(new("FIntFRMMinGrassDistance", "0", "No grass up close."));
        }

        return flags;
    }

    /// <summary>
    /// Every flag this slider might set, so applying a new level (or restoring)
    /// can clear them all first and never strand one from a previous level.
    /// </summary>
    public static IReadOnlyList<FastFlag> AllManagedFlags { get; } = new[]
    {
        new FastFlag("DFIntDebugFRMQualityLevelOverride", "", ""),
        new FastFlag("DFFlagTextureQualityOverrideEnabled", "", ""),
        new FastFlag("DFIntTextureQualityOverride", "", ""),
        new FastFlag("FIntDebugForceMSAASamples", "", ""),
        new FastFlag("FFlagDebugSkyGray", "", ""),
        new FastFlag("FIntFRMMaxGrassDistance", "", ""),
        new FastFlag("FIntFRMMinGrassDistance", "", "")
    };

    /// <summary>
    /// Applies a level: clears any flags a previous level set, then writes this
    /// one's (nothing at the top level).
    /// </summary>
    public static void Apply(int level)
    {
        FastFlagStore.Backup();
        FastFlagStore.Reset(AllManagedFlags);

        IReadOnlyList<FastFlag> flags = FlagsFor(level);
        if (flags.Count > 0) FastFlagStore.Apply(flags);
    }

    /// <summary>Removes every flag this slider manages, back to Roblox's own settings.</summary>
    public static void Restore() => FastFlagStore.Reset(AllManagedFlags);

    /// <summary>Whether any of this slider's flags are currently written.</summary>
    public static bool IsApplied()
    {
        Dictionary<string, string> current = FastFlagStore.Read();
        foreach (FastFlag f in AllManagedFlags)
            if (current.ContainsKey(f.Name)) return true;
        return false;
    }
}
