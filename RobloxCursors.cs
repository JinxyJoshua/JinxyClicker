using System;
using System.Collections.Generic;
using System.IO;

namespace JinxyClicker;

/// <summary>One cursor image a crosshair can be written over.</summary>
/// <param name="Path">The PNG on disk.</param>
/// <param name="Pixels">Its side in pixels — 64 for the arrows, 32 for the locked cursor.</param>
/// <param name="StrapCreated">
/// True for a bootstrapper's Modifications folder, where the file is one we create
/// as an override rather than an original we replace. It has no backup; removing
/// it means deleting it.
/// </param>
public readonly record struct CursorTarget(string Path, int Pixels, bool StrapCreated);

/// <summary>The tally from applying a crosshair.</summary>
public readonly record struct CursorApplyResult(int Written, int Failed)
{
    public bool AnyWritten => Written > 0;
}

/// <summary>
/// Writes a crosshair into Roblox's own cursor pictures, and puts the originals
/// back.
/// </summary>
/// <remarks>
/// Roblox in first person shows only a faint dot, so replacing the cursor image
/// is how a real crosshair gets on screen — it becomes the actual cursor, in
/// every installed version and in Bloxstrap / Fishstrap / Froststrap mods if they
/// are present. It is the approach every Roblox crosshair tool uses because it is
/// the only one that shows the crosshair exactly where the game reads the mouse.
///
/// <para>Reversible by keeping a <c>.jinxybak</c> copy of each original the first
/// time it is replaced, so <see cref="Remove"/> restores the untouched file even
/// after the crosshair has been changed several times. Bootstrapper overrides,
/// which have no original, are marked with a <c>.jinxymod</c> sentinel so only the
/// files this app created are ever deleted — a user's own cursor mod is left
/// alone.</para>
///
/// <para>Roblox reads the cursor at launch, so a change lands on the next
/// relaunch. A Roblox update installs a fresh version folder with the default
/// cursor; re-applying covers it.</para>
///
/// <para>The drawing is injected rather than referenced, so the file logic can be
/// tested without a screen.</para>
/// </remarks>
public sealed class RobloxCursors
{
    private const string BackupSuffix = ".jinxybak";
    private const string ModMarker = ".jinxymod";

    // The three cursors worth replacing. IBeamCursor is the text caret and is
    // left alone so typing in chat still looks normal.
    private const string ArrowCursor = "ArrowCursor.png";
    private const string ArrowFarCursor = "ArrowFarCursor.png";
    private const string MouseLockedCursor = "MouseLockedCursor.png";

    private readonly string _localAppData;

    public RobloxCursors(string? localAppData = null)
    {
        _localAppData = localAppData
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    /// <summary>Every cursor file a crosshair would be written over right now.</summary>
    public IReadOnlyList<CursorTarget> Discover()
    {
        var targets = new List<CursorTarget>();

        // Installed Roblox versions: replace the originals that are there.
        string versions = Path.Combine(_localAppData, "Roblox", "Versions");
        if (Directory.Exists(versions))
        {
            foreach (string version in Directory.EnumerateDirectories(versions))
            {
                string km = Path.Combine(version, "content", "textures", "Cursors", "KeyboardMouse");
                AddIfFile(targets, Path.Combine(km, ArrowCursor), 64, strap: false);
                AddIfFile(targets, Path.Combine(km, ArrowFarCursor), 64, strap: false);

                string locked = Path.Combine(version, "content", "textures", MouseLockedCursor);
                AddIfFile(targets, locked, 32, strap: false);
            }
        }

        // Bootstrapper mods: create overrides so the crosshair survives a Roblox
        // update, which the straps re-apply from their Modifications folder.
        foreach (string strap in new[] { "Bloxstrap", "Fishstrap", "Froststrap" })
        {
            string root = Path.Combine(_localAppData, strap);
            if (!Directory.Exists(root)) continue;

            string mods = Path.Combine(root, "Modifications");
            string km = Path.Combine(mods, "content", "textures", "Cursors", "KeyboardMouse");
            targets.Add(new CursorTarget(Path.Combine(km, ArrowCursor), 64, StrapCreated: true));
            targets.Add(new CursorTarget(Path.Combine(km, ArrowFarCursor), 64, StrapCreated: true));
            targets.Add(new CursorTarget(Path.Combine(mods, "content", "textures", MouseLockedCursor), 32, StrapCreated: true));
        }

        return targets;
    }

    private static void AddIfFile(List<CursorTarget> targets, string path, int pixels, bool strap)
    {
        if (File.Exists(path)) targets.Add(new CursorTarget(path, pixels, strap));
    }

    /// <summary>Whether a crosshair is currently written anywhere.</summary>
    public bool IsApplied()
    {
        foreach (CursorTarget t in Discover())
        {
            if (t.StrapCreated)
            {
                if (File.Exists(t.Path + ModMarker)) return true;
            }
            else if (File.Exists(t.Path + BackupSuffix)) return true;
        }
        return false;
    }

    /// <summary>
    /// Writes <paramref name="pngForPixels"/>'s image over every cursor, backing
    /// up each original the first time.
    /// </summary>
    /// <param name="pngForPixels">
    /// Gives the PNG bytes for a required pixel size (64 or 32). Called once per
    /// distinct size and cached, so the drawing happens twice, not once per file.
    /// </param>
    public CursorApplyResult Apply(Func<int, byte[]> pngForPixels)
    {
        var cache = new Dictionary<int, byte[]>();
        byte[] Bytes(int px) => cache.TryGetValue(px, out byte[]? b) ? b : cache[px] = pngForPixels(px);

        int written = 0, failed = 0;

        foreach (CursorTarget t in Discover())
        {
            try
            {
                string? dir = Path.GetDirectoryName(t.Path);
                if (dir != null) Directory.CreateDirectory(dir);

                if (t.StrapCreated)
                {
                    File.WriteAllBytes(t.Path, Bytes(t.Pixels));
                    File.WriteAllText(t.Path + ModMarker, string.Empty);
                }
                else
                {
                    string backup = t.Path + BackupSuffix;
                    if (!File.Exists(backup) && File.Exists(t.Path))
                        File.Copy(t.Path, backup);

                    File.WriteAllBytes(t.Path, Bytes(t.Pixels));
                }

                written++;
            }
            catch
            {
                // A single locked or unwritable file should not sink the rest —
                // count it and move on. Roblox running is the usual cause, and it
                // is why applying asks for a relaunch.
                failed++;
            }
        }

        return new CursorApplyResult(written, failed);
    }

    /// <summary>Puts every original back and removes the overrides this app made.</summary>
    public int Remove()
    {
        int restored = 0;

        foreach (CursorTarget t in Discover())
        {
            try
            {
                if (t.StrapCreated)
                {
                    string marker = t.Path + ModMarker;
                    if (File.Exists(marker))
                    {
                        if (File.Exists(t.Path)) File.Delete(t.Path);
                        File.Delete(marker);
                        restored++;
                    }
                }
                else
                {
                    string backup = t.Path + BackupSuffix;
                    if (File.Exists(backup))
                    {
                        File.Copy(backup, t.Path, overwrite: true);
                        File.Delete(backup);
                        restored++;
                    }
                }
            }
            catch
            {
                // Leave it for the next Remove rather than throwing half way.
            }
        }

        return restored;
    }
}
