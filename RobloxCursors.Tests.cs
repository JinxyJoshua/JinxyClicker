using System;
using System.IO;
using System.Linq;
using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// The cursor writer's file logic: what it replaces, that it backs up, and that
/// Remove puts everything back — all against a throwaway Roblox tree, no screen.
/// </summary>
public sealed class RobloxCursorsTests : IDisposable
{
    private readonly string _root;

    private static readonly byte[] ArrowBytes = { 0xA0 };
    private static readonly byte[] FarBytes = { 0xA1 };
    private static readonly byte[] LockedBytes = { 0xB0 };
    private static readonly byte[] Png64 = { 1, 2, 3, 4 };
    private static readonly byte[] Png32 = { 9, 9 };

    public RobloxCursorsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jinxy-cursors-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    // A renderer that gives different bytes per size, and counts its calls.
    private static Func<int, byte[]> Renderer(out Func<int> calls)
    {
        int n = 0;
        calls = () => n;
        return px => { n++; return px == 64 ? Png64 : Png32; };
    }

    private string MakeVersion(string name)
    {
        string version = Path.Combine(_root, "Roblox", "Versions", name);
        string km = Path.Combine(version, "content", "textures", "Cursors", "KeyboardMouse");
        Directory.CreateDirectory(km);

        File.WriteAllBytes(Path.Combine(km, "ArrowCursor.png"), ArrowBytes);
        File.WriteAllBytes(Path.Combine(km, "ArrowFarCursor.png"), FarBytes);
        File.WriteAllBytes(Path.Combine(km, "IBeamCursor.png"), new byte[] { 0xCC });
        File.WriteAllBytes(Path.Combine(version, "content", "textures", "MouseLockedCursor.png"), LockedBytes);

        return version;
    }

    [Fact]
    public void DiscoverFindsTheThreeCursorsAndSkipsTheIBeam()
    {
        MakeVersion("version-a");
        var targets = new RobloxCursors(_root).Discover();

        Assert.Equal(3, targets.Count);
        Assert.Contains(targets, t => t.Path.EndsWith("ArrowCursor.png") && t.Pixels == 64);
        Assert.Contains(targets, t => t.Path.EndsWith("ArrowFarCursor.png") && t.Pixels == 64);
        Assert.Contains(targets, t => t.Path.EndsWith("MouseLockedCursor.png") && t.Pixels == 32);
        Assert.DoesNotContain(targets, t => t.Path.EndsWith("IBeamCursor.png"));
    }

    [Fact]
    public void ApplyWritesEveryCursorAndBacksUpTheOriginal()
    {
        string version = MakeVersion("version-a");
        var cursors = new RobloxCursors(_root);

        var result = cursors.Apply(Renderer(out _));

        Assert.Equal(3, result.Written);
        Assert.Equal(0, result.Failed);

        string km = Path.Combine(version, "content", "textures", "Cursors", "KeyboardMouse");
        Assert.Equal(Png64, File.ReadAllBytes(Path.Combine(km, "ArrowCursor.png")));
        Assert.Equal(Png32, File.ReadAllBytes(Path.Combine(version, "content", "textures", "MouseLockedCursor.png")));

        // The original is kept beside it for a clean restore.
        Assert.Equal(ArrowBytes, File.ReadAllBytes(Path.Combine(km, "ArrowCursor.png.jinxybak")));
        Assert.True(cursors.IsApplied());
    }

    [Fact]
    public void ApplyDrawsOncePerSizeNotOncePerFile()
    {
        MakeVersion("version-a");
        MakeVersion("version-b");

        new RobloxCursors(_root).Apply(Renderer(out Func<int> calls));

        // Six files across two versions, but only two distinct sizes: 64 and 32.
        Assert.Equal(2, calls());
    }

    [Fact]
    public void RemovePutsEveryOriginalBack()
    {
        string version = MakeVersion("version-a");
        var cursors = new RobloxCursors(_root);

        cursors.Apply(Renderer(out _));
        int restored = cursors.Remove();

        Assert.Equal(3, restored);

        string km = Path.Combine(version, "content", "textures", "Cursors", "KeyboardMouse");
        Assert.Equal(ArrowBytes, File.ReadAllBytes(Path.Combine(km, "ArrowCursor.png")));
        Assert.Equal(FarBytes, File.ReadAllBytes(Path.Combine(km, "ArrowFarCursor.png")));
        Assert.Equal(LockedBytes, File.ReadAllBytes(Path.Combine(version, "content", "textures", "MouseLockedCursor.png")));

        Assert.False(File.Exists(Path.Combine(km, "ArrowCursor.png.jinxybak")));
        Assert.False(cursors.IsApplied());
    }

    [Fact]
    public void ChangingCrosshairKeepsTheFirstBackupSoRemoveStillRestoresTheTrueOriginal()
    {
        string version = MakeVersion("version-a");
        var cursors = new RobloxCursors(_root);

        cursors.Apply(_ => new byte[] { 7 });   // crosshair A
        cursors.Apply(_ => new byte[] { 8 });   // crosshair B
        cursors.Remove();

        string arrow = Path.Combine(version, "content", "textures", "Cursors", "KeyboardMouse", "ArrowCursor.png");
        Assert.Equal(ArrowBytes, File.ReadAllBytes(arrow));
    }

    [Fact]
    public void StrapModsAreCreatedThenRemoved()
    {
        MakeVersion("version-a");
        Directory.CreateDirectory(Path.Combine(_root, "Bloxstrap"));
        var cursors = new RobloxCursors(_root);

        var targets = cursors.Discover();
        Assert.Equal(3, targets.Count(t => t.StrapCreated));

        cursors.Apply(Renderer(out _));

        string strapArrow = Path.Combine(_root, "Bloxstrap", "Modifications",
            "content", "textures", "Cursors", "KeyboardMouse", "ArrowCursor.png");
        Assert.True(File.Exists(strapArrow));
        Assert.True(File.Exists(strapArrow + ".jinxymod"));

        cursors.Remove();
        Assert.False(File.Exists(strapArrow));
        Assert.False(File.Exists(strapArrow + ".jinxymod"));
    }

    [Fact]
    public void RemoveLeavesAUsersOwnStrapCursorAlone()
    {
        MakeVersion("version-a");
        string strapKm = Path.Combine(_root, "Bloxstrap", "Modifications",
            "content", "textures", "Cursors", "KeyboardMouse");
        Directory.CreateDirectory(strapKm);

        // A cursor the user put there themselves, with no marker of ours.
        string userArrow = Path.Combine(strapKm, "ArrowCursor.png");
        File.WriteAllBytes(userArrow, new byte[] { 0xEE });

        new RobloxCursors(_root).Remove();

        Assert.True(File.Exists(userArrow));
        Assert.Equal(new byte[] { 0xEE }, File.ReadAllBytes(userArrow));
    }

    [Fact]
    public void NoRobloxMeansNothingToApply()
    {
        var result = new RobloxCursors(_root).Apply(Renderer(out _));
        Assert.False(result.AnyWritten);
        Assert.Equal(0, result.Written);
    }
}
