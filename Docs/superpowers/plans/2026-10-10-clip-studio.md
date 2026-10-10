# Clip Studio v1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Pro "Clip Studio" page: list the clips the user saved with the clip hotkey, and stitch them into one music-backed montage (landscape or vertical) with one button.

**Architecture:** The capture side already exists — `ReplayBuffer` saves the last N seconds to an MP4 in the clip folder on a hotkey. Clip Studio adds three things: a `ClipLibrary` (list/delete MP4s in the clip folder), a `MontageMaker` (build + run an ffmpeg command that normalizes clips, concatenates them, lays music over the top, and exports landscape or vertical), and a Pro-gated CLIP STUDIO page that drives them. Music is a bundled `music/` folder plus an own-file picker.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WPF, xUnit. ffmpeg is already shipped beside the app (`ScreenRecorder.FindFfmpeg()`). No new packages.

**Spec:** `docs/superpowers/specs/2026-10-10-clip-studio-design.md`

## Global Constraints

- Target framework `net10.0-windows`; match existing projects.
- Commits: imperative, no Claude attribution / `Co-Authored-By` (repo rule).
- Pro-gated: the CLIP STUDIO page reads the existing `IsPro` switch; when not Pro, controls are disabled and a "Unlock with Pro" prompt shows.
- ffmpeg is found via `ScreenRecorder.FindFfmpeg()` (returns null if missing — handle it).
- Bundled music must be license-clean (original or CC0) — never ripped songs.
- Clip length stays the existing `ReplaySeconds` setting (20s default). No new clip-length setting.
- Montage output and clips go in the existing clip folder (`ClipFolder` / `DefaultClipFolder`).

## Review Focus

- **No clips in the library** → "Make montage" does nothing destructive and shows "No clips to montage yet." Covered in Task 1 (empty list) + Task 7 (guard).
- **A clip file is deleted/locked mid-montage** → ffmpeg fails; the app reports "Montage failed" rather than crashing. Covered in Task 3 (null/failure return) + Task 7.
- **ffmpeg missing** → every entry point degrades gracefully with a clear message, no crash. Covered in Task 3 + Task 7.
- **Clips of different sizes/framerates** → the montage still renders (each input is scaled/padded to the target before concat). Covered in Task 2 (filter builds per-input scale+pad).
- **Music shorter than the montage** → music loops to cover it, no silent tail. Covered in Task 2 (`-stream_loop -1` on the music input).

---

## File Structure

- `ClipLibrary.cs` (new) — list/delete clip MP4s in a folder. Pure, testable.
- `ClipLibrary.Tests.cs` (new).
- `MontageMaker.cs` (new) — build the ffmpeg args (testable) and run it (integration).
- `MontageMaker.Tests.cs` (new) — tests the argument building.
- `MusicLibrary.cs` (new) — list bundled tracks + resolve a chosen/own file. Small, testable.
- `MusicLibrary.Tests.cs` (new).
- `music/` (new folder) — bundled default tracks; wired into the build like `kits`.
- `JinxyClicker.csproj` (modify) — copy `music/*` to output + publish.
- `AppSettings.cs` (modify) — `MontageVertical` (bool), `MontageMusic` (string? — bundled name or full path).
- `Testing/JinxyClicker.Tests.csproj` (modify) — link the new source/test files.
- `MainWindow.xaml` (modify) — the CLIP STUDIO page + nav button in the CAPTURE group.
- `MainWindow.ClipStudio.cs` (new partial) — page handlers: list clips, play/delete/open, make montage with progress, Pro-gating.
- `MainWindow.xaml.cs` (modify) — register NavClipStudio/PageClipStudio in the arrays; load on startup.

---

### Task 1: ClipLibrary

**Files:**
- Create: `ClipLibrary.cs`, `ClipLibrary.Tests.cs`
- Modify: `Testing/JinxyClicker.Tests.csproj`

**Interfaces:**
- Produces:
  - `readonly record struct ClipInfo(string Path, string Name, DateTime SavedUtc, long Bytes)`
  - `static IReadOnlyList<ClipInfo> ClipLibrary.List(string folder)` — newest first; MP4s only; empty list if folder missing.
  - `static bool ClipLibrary.Delete(string path)` — true if the file is gone after the call.

- [ ] **Step 1: Link the test files**

In `Testing/JinxyClicker.Tests.csproj`, after the `License.Tests.cs` line, add:

```xml
    <Compile Include="../ClipLibrary.cs" />
    <Compile Include="../ClipLibrary.Tests.cs" />
```

- [ ] **Step 2: Write the failing tests**

Create `ClipLibrary.Tests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using JinxyClicker;
using Xunit;

public class ClipLibraryTests
{
    private static string TempDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "jinxyclip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void Missing_folder_lists_nothing()
    {
        Assert.Empty(ClipLibrary.List(Path.Combine(Path.GetTempPath(), "nope_" + Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void Lists_only_mp4_newest_first()
    {
        string d = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(d, "a.mp4"), "x");
            System.Threading.Thread.Sleep(20);
            File.WriteAllText(Path.Combine(d, "b.mp4"), "x");
            File.WriteAllText(Path.Combine(d, "notes.txt"), "x");

            var list = ClipLibrary.List(d);
            Assert.Equal(2, list.Count);
            Assert.Equal("b.mp4", list[0].Name); // newest first
            Assert.Equal("a.mp4", list[1].Name);
        }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void Delete_removes_the_file()
    {
        string d = TempDir();
        try
        {
            string p = Path.Combine(d, "c.mp4");
            File.WriteAllText(p, "x");
            Assert.True(ClipLibrary.Delete(p));
            Assert.False(File.Exists(p));
            Assert.True(ClipLibrary.Delete(p)); // already gone -> still "gone"
        }
        finally { Directory.Delete(d, true); }
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test Testing/JinxyClicker.Tests.csproj --filter ClipLibraryTests`
Expected: FAIL — `ClipLibrary` does not exist.

- [ ] **Step 4: Write the implementation**

Create `ClipLibrary.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JinxyClicker;

/// <summary>One saved clip on disk.</summary>
public readonly record struct ClipInfo(string Path, string Name, DateTime SavedUtc, long Bytes);

/// <summary>Lists and deletes the clip MP4s in the clip folder.</summary>
public static class ClipLibrary
{
    public static IReadOnlyList<ClipInfo> List(string folder)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return Array.Empty<ClipInfo>();

            return new DirectoryInfo(folder)
                .GetFiles("*.mp4")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => new ClipInfo(f.FullName, f.Name, f.LastWriteTimeUtc, f.Length))
                .ToList();
        }
        catch
        {
            return Array.Empty<ClipInfo>();
        }
    }

    public static bool Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); return !File.Exists(path); }
        catch { return false; }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test Testing/JinxyClicker.Tests.csproj --filter ClipLibraryTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add ClipLibrary.cs ClipLibrary.Tests.cs Testing/JinxyClicker.Tests.csproj
git commit -m "Add ClipLibrary to list and delete saved clips"
```

---

### Task 2: MontageMaker — ffmpeg argument builder

**Files:**
- Create: `MontageMaker.cs`, `MontageMaker.Tests.cs`
- Modify: `Testing/JinxyClicker.Tests.csproj`

**Interfaces:**
- Consumes: nothing from other tasks.
- Produces:
  - `enum MontageShape { Landscape, Vertical }`
  - `static string MontageMaker.BuildArgs(IReadOnlyList<string> clipPaths, string? musicPath, MontageShape shape, string outputPath)` — the full ffmpeg argument string.

Design notes the implementation must satisfy (and the tests pin):
- Each clip is input `-i`. The music, if any, is the LAST input and uses `-stream_loop -1` before its `-i` so it loops to cover the montage.
- `filter_complex` scales+pads each clip to the target (`1920x1080` landscape, `1080x1920` vertical) with `force_original_aspect_ratio` and `setsar=1,fps=30`, labels them `[v0]..[vN-1]`, then `concat=n=N:v=1:a=0[outv]`.
  - Landscape uses `decrease` + `pad` (letterbox). Vertical uses `increase` + `crop` (fill).
- `-map "[outv]"`; if music present also `-map <musicIndex>:a -shortest`.
- Output encode: `-c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p` and, with music, `-c:a aac`.
- `-y` to overwrite.

- [ ] **Step 1: Link the test files**

In `Testing/JinxyClicker.Tests.csproj` add:

```xml
    <Compile Include="../MontageMaker.cs" />
    <Compile Include="../MontageMaker.Tests.cs" />
```

- [ ] **Step 2: Write the failing tests**

Create `MontageMaker.Tests.cs`:

```csharp
using System.Collections.Generic;
using JinxyClicker;
using Xunit;

public class MontageMakerTests
{
    private static readonly List<string> TwoClips = new() { @"C:\c\a.mp4", @"C:\c\b.mp4" };

    [Fact]
    public void Builds_concat_for_each_clip()
    {
        string args = MontageMaker.BuildArgs(TwoClips, null, MontageShape.Landscape, @"C:\out.mp4");
        Assert.Contains("-i \"C:\\c\\a.mp4\"", args);
        Assert.Contains("-i \"C:\\c\\b.mp4\"", args);
        Assert.Contains("concat=n=2:v=1:a=0[outv]", args);
        Assert.Contains("-map \"[outv]\"", args);
        Assert.Contains("\"C:\\out.mp4\"", args);
    }

    [Fact]
    public void Landscape_targets_1920x1080_and_letterboxes()
    {
        string args = MontageMaker.BuildArgs(TwoClips, null, MontageShape.Landscape, @"C:\out.mp4");
        Assert.Contains("1920:1080", args);
        Assert.Contains("force_original_aspect_ratio=decrease", args);
        Assert.Contains("pad=1920:1080", args);
    }

    [Fact]
    public void Vertical_targets_1080x1920_and_crops()
    {
        string args = MontageMaker.BuildArgs(TwoClips, null, MontageShape.Vertical, @"C:\out.mp4");
        Assert.Contains("1080:1920", args);
        Assert.Contains("force_original_aspect_ratio=increase", args);
        Assert.Contains("crop=1080:1920", args);
    }

    [Fact]
    public void Music_loops_and_is_mapped()
    {
        string args = MontageMaker.BuildArgs(TwoClips, @"C:\m\song.mp3", MontageShape.Landscape, @"C:\out.mp4");
        Assert.Contains("-stream_loop -1 -i \"C:\\m\\song.mp3\"", args);
        Assert.Contains("-map \"2:a\"", args);   // music is input index 2 (after 2 clips)
        Assert.Contains("-shortest", args);
        Assert.Contains("-c:a aac", args);
    }

    [Fact]
    public void No_music_has_no_audio_map()
    {
        string args = MontageMaker.BuildArgs(TwoClips, null, MontageShape.Landscape, @"C:\out.mp4");
        Assert.DoesNotContain(":a\"", args);
        Assert.DoesNotContain("-shortest", args);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test Testing/JinxyClicker.Tests.csproj --filter MontageMakerTests`
Expected: FAIL — `MontageMaker` does not exist.

- [ ] **Step 4: Write the implementation**

Create `MontageMaker.cs` (argument builder only — execution is Task 3):

```csharp
using System;
using System.Collections.Generic;
using System.Text;

namespace JinxyClicker;

public enum MontageShape { Landscape, Vertical }

/// <summary>
/// Builds (and, in Task 3, runs) the ffmpeg command that turns a list of clips
/// into one montage: every clip scaled to a common frame, concatenated, with
/// music looped over the top.
/// </summary>
public static partial class MontageMaker
{
    public static string BuildArgs(IReadOnlyList<string> clipPaths, string? musicPath,
        MontageShape shape, string outputPath)
    {
        bool hasMusic = !string.IsNullOrWhiteSpace(musicPath);
        int w = shape == MontageShape.Vertical ? 1080 : 1920;
        int h = shape == MontageShape.Vertical ? 1920 : 1080;

        var sb = new StringBuilder("-y ");
        foreach (string clip in clipPaths) sb.Append($"-i \"{clip}\" ");
        if (hasMusic) sb.Append($"-stream_loop -1 -i \"{musicPath}\" ");

        // Per-clip normalize, then concat.
        var filter = new StringBuilder();
        string fit = shape == MontageShape.Vertical
            ? $"scale={w}:{h}:force_original_aspect_ratio=increase,crop={w}:{h}"
            : $"scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2";

        for (int i = 0; i < clipPaths.Count; i++)
            filter.Append($"[{i}:v]{fit},setsar=1,fps=30[v{i}];");
        for (int i = 0; i < clipPaths.Count; i++)
            filter.Append($"[v{i}]");
        filter.Append($"concat=n={clipPaths.Count}:v=1:a=0[outv]");

        sb.Append($"-filter_complex \"{filter}\" -map \"[outv]\" ");

        if (hasMusic)
            sb.Append($"-map \"{clipPaths.Count}:a\" -shortest -c:a aac ");

        sb.Append($"-c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -movflags +faststart \"{outputPath}\"");
        return sb.ToString();
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test Testing/JinxyClicker.Tests.csproj --filter MontageMakerTests`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add MontageMaker.cs MontageMaker.Tests.cs Testing/JinxyClicker.Tests.csproj
git commit -m "Add MontageMaker ffmpeg argument builder"
```

---

### Task 3: MontageMaker — run ffmpeg

**Files:**
- Modify: `MontageMaker.cs`

**Interfaces:**
- Consumes: `BuildArgs` (Task 2), `ScreenRecorder.FindFfmpeg()` (existing).
- Produces: `static Task<string?> MontageMaker.RenderAsync(IReadOnlyList<string> clipPaths, string? musicPath, MontageShape shape, string outputFolder)` — returns the output path, or null on any failure (no ffmpeg, no clips, ffmpeg error, empty output).

- [ ] **Step 1: Write the implementation (no unit test — it shells out to ffmpeg; verified end-to-end in Task 7)**

Add to `MontageMaker.cs` (and `using System.Diagnostics; using System.IO; using System.Threading.Tasks;`):

```csharp
    public static async Task<string?> RenderAsync(IReadOnlyList<string> clipPaths, string? musicPath,
        MontageShape shape, string outputFolder)
    {
        if (clipPaths is null || clipPaths.Count == 0) return null;

        string? ffmpeg = ScreenRecorder.FindFfmpeg();
        if (ffmpeg == null) return null;

        try
        {
            Directory.CreateDirectory(outputFolder);
            string output = Path.Combine(outputFolder, $"montage-{DateTime.Now:yyyy-MM-dd-HHmmss}.mp4");
            string args = BuildArgs(clipPaths, musicPath, shape, output);

            var info = new ProcessStartInfo(ffmpeg, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using Process? p = Process.Start(info);
            if (p == null) return null;
            p.ErrorDataReceived += (_, _) => { };
            p.OutputDataReceived += (_, _) => { };
            p.BeginErrorReadLine();
            p.BeginOutputReadLine();
            await p.WaitForExitAsync().ConfigureAwait(false);

            return File.Exists(output) && new FileInfo(output).Length > 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build JinxyClicker.csproj -c Debug`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add MontageMaker.cs
git commit -m "Run the montage ffmpeg command"
```

---

### Task 4: Music — bundled tracks + MusicLibrary

**Files:**
- Create: `music/` folder with default tracks (generated, license-clean)
- Create: `MusicLibrary.cs`, `MusicLibrary.Tests.cs`
- Modify: `JinxyClicker.csproj` (copy `music/*` to output + publish)
- Modify: `Testing/JinxyClicker.Tests.csproj`

**Interfaces:**
- Produces:
  - `static IReadOnlyList<string> MusicLibrary.Bundled(string appFolder)` — full paths of tracks in `<app>/music`, or empty.
  - `static string? MusicLibrary.Resolve(string? chosen, string appFolder)` — if `chosen` is a full path that exists, return it; if it's a bare bundled file name that exists in `<app>/music`, return that full path; else the first bundled track; else null.

- [ ] **Step 1: Generate two license-clean default tracks**

These are simple original loops made with ffmpeg (no copyright). Run from the repo root:

```bash
FF="ffmpeg/ffmpeg.exe"
mkdir -p music
"$FF" -y -f lavfi -i "sine=frequency=220:duration=30,aecho=0.8:0.9:1000:0.3" -filter:a "tremolo=f=4:d=0.5,volume=0.5" -c:a libmp3lame -q:a 5 music/chill-loop.mp3
"$FF" -y -f lavfi -i "sine=frequency=330:duration=30,aecho=0.8:0.88:600:0.4" -filter:a "tremolo=f=6:d=0.6,volume=0.5" -c:a libmp3lame -q:a 5 music/upbeat-loop.mp3
ls -la music/
```

(These are plain placeholder loops so the montage has music out of the box. They can be replaced any time with better CC0 tracks from e.g. Pixabay Music — just drop .mp3 files into the `music` folder. Document this in the page's tooltip.)

- [ ] **Step 2: Bundle the music folder into the build**

In `JinxyClicker.csproj`, after the `Content Include="Assets\Kits\*.png"` item group, add:

```xml
  <ItemGroup>
    <!-- Royalty-free montage music, copied beside the app like the kit art.
         Users can drop their own .mp3s into this folder too. -->
    <Content Include="music\*.mp3"
             Link="music\%(Filename)%(Extension)"
             CopyToOutputDirectory="PreserveNewest"
             CopyToPublishDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 3: Link the test files**

In `Testing/JinxyClicker.Tests.csproj` add:

```xml
    <Compile Include="../MusicLibrary.cs" />
    <Compile Include="../MusicLibrary.Tests.cs" />
```

- [ ] **Step 4: Write the failing tests**

Create `MusicLibrary.Tests.cs`:

```csharp
using System;
using System.IO;
using JinxyClicker;
using Xunit;

public class MusicLibraryTests
{
    private static string AppWithMusic(params string[] names)
    {
        string app = Path.Combine(Path.GetTempPath(), "jinxymusic_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(app, "music"));
        foreach (var n in names) File.WriteAllText(Path.Combine(app, "music", n), "x");
        return app;
    }

    [Fact]
    public void Bundled_lists_mp3s()
    {
        string app = AppWithMusic("a.mp3", "b.mp3");
        try { Assert.Equal(2, MusicLibrary.Bundled(app).Count); }
        finally { Directory.Delete(app, true); }
    }

    [Fact]
    public void Resolve_prefers_an_existing_full_path()
    {
        string app = AppWithMusic("a.mp3");
        string own = Path.Combine(Path.GetTempPath(), "own_" + Guid.NewGuid().ToString("N") + ".mp3");
        File.WriteAllText(own, "x");
        try { Assert.Equal(own, MusicLibrary.Resolve(own, app)); }
        finally { Directory.Delete(app, true); File.Delete(own); }
    }

    [Fact]
    public void Resolve_finds_a_bundled_name()
    {
        string app = AppWithMusic("a.mp3");
        try { Assert.Equal(Path.Combine(app, "music", "a.mp3"), MusicLibrary.Resolve("a.mp3", app)); }
        finally { Directory.Delete(app, true); }
    }

    [Fact]
    public void Resolve_falls_back_to_first_bundled_then_null()
    {
        string app = AppWithMusic("a.mp3");
        try { Assert.Equal(Path.Combine(app, "music", "a.mp3"), MusicLibrary.Resolve(null, app)); }
        finally { Directory.Delete(app, true); }

        string empty = Path.Combine(Path.GetTempPath(), "nomusic_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try { Assert.Null(MusicLibrary.Resolve(null, empty)); }
        finally { Directory.Delete(empty, true); }
    }
}
```

- [ ] **Step 5: Run the tests to verify they fail**

Run: `dotnet test Testing/JinxyClicker.Tests.csproj --filter MusicLibraryTests`
Expected: FAIL — `MusicLibrary` does not exist.

- [ ] **Step 6: Write the implementation**

Create `MusicLibrary.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JinxyClicker;

/// <summary>The montage music: the tracks shipped in &lt;app&gt;/music, plus the
/// user's own file if they pick one.</summary>
public static class MusicLibrary
{
    public static IReadOnlyList<string> Bundled(string appFolder)
    {
        try
        {
            string dir = Path.Combine(appFolder, "music");
            if (!Directory.Exists(dir)) return Array.Empty<string>();
            return Directory.GetFiles(dir, "*.mp3").OrderBy(f => f).ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    public static string? Resolve(string? chosen, string appFolder)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(chosen))
            {
                if (File.Exists(chosen)) return chosen;
                string inMusic = Path.Combine(appFolder, "music", chosen);
                if (File.Exists(inMusic)) return inMusic;
            }
            return Bundled(appFolder).FirstOrDefault();
        }
        catch { return null; }
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test Testing/JinxyClicker.Tests.csproj --filter MusicLibraryTests`
Expected: PASS (4 tests).

- [ ] **Step 8: Commit**

```bash
git add music MusicLibrary.cs MusicLibrary.Tests.cs JinxyClicker.csproj Testing/JinxyClicker.Tests.csproj
git commit -m "Bundle montage music and add MusicLibrary"
```

---

### Task 5: AppSettings fields

**Files:**
- Modify: `AppSettings.cs`, `AppSettings.Tests.cs`

**Interfaces:**
- Produces: `AppSettings.MontageVertical` (bool), `AppSettings.MontageMusic` (string?).

- [ ] **Step 1: Write the failing test**

Add to `AppSettings.Tests.cs` (near the license round-trip test):

```csharp
[Fact]
public void Montage_settings_round_trip()
{
    var s = new AppSettings { MontageVertical = true, MontageMusic = "upbeat-loop.mp3" };
    var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s))!;
    Assert.True(back.MontageVertical);
    Assert.Equal("upbeat-loop.mp3", back.MontageMusic);
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Testing/JinxyClicker.Tests.csproj --filter Montage_settings_round_trip`
Expected: FAIL — properties missing.

- [ ] **Step 3: Add the properties**

In `AppSettings.cs`, after the `LicenseFirstUseUtc` property, add:

```csharp
    /// <summary>Clip Studio: export montages vertically (9:16) for TikTok/Shorts.</summary>
    public bool MontageVertical { get; set; }

    /// <summary>Clip Studio: the chosen music — a bundled track's file name, or a
    /// full path to the user's own file. Null uses the first bundled track.</summary>
    public string? MontageMusic { get; set; }
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test Testing/JinxyClicker.Tests.csproj --filter Montage_settings_round_trip`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AppSettings.cs AppSettings.Tests.cs
git commit -m "Store Clip Studio montage settings"
```

---

### Task 6: CLIP STUDIO page (XAML + nav)

**Files:**
- Modify: `MainWindow.xaml` (nav button in CAPTURE group + the page)
- Modify: `MainWindow.xaml.cs` (register in NavButtons/Pages arrays)

**Interfaces:**
- Consumes: the collapsible-group nav pattern and `ShowPage` (existing).
- Produces: named controls the handlers (Task 7) use: `NavClipStudio`, `PageClipStudio`, `ClipList` (ItemsControl or ListBox), `MontageButton`, `MontageVerticalCheck`, `MusicCombo` (ComboBox), `ClipStudioStatus` (TextBlock), `ClipStudioLockNote` (TextBlock).

- [ ] **Step 1: Add the nav button to the CAPTURE group**

In `MainWindow.xaml`, inside `<StackPanel x:Name="GroupCapture">`, after the HISTORY button, add:

```xml
                    <Button x:Name="NavClipStudio" app:NavIcon.Glyph="&#xE8B2;" Content="CLIP STUDIO" Style="{StaticResource NavButton}"
                            Click="NavClipStudio_Click"/>
```

- [ ] **Step 2: Add the page**

In `MainWindow.xaml`, add a new page StackPanel next to the other pages (e.g. right before `<StackPanel x:Name="PageHistory"`). Match the existing page markup style (Panel/Outline/TextMuted brushes). It needs: a lock note (shown when not Pro), a music picker + vertical toggle + "Make montage" button + status line, and a clip list with play/delete/open per item.

```xml
                <StackPanel x:Name="PageClipStudio" Visibility="Collapsed">

                    <TextBlock x:Name="ClipStudioLockNote" Visibility="Collapsed"
                               Text="Clip Studio is a Pro feature. Unlock it on the PRO page to make montages."
                               Foreground="{StaticResource TextSoft}" FontSize="13" Margin="0,0,0,14"/>

                    <Border Background="{StaticResource Panel}" CornerRadius="2" BorderThickness="1" BorderBrush="{StaticResource Outline}" Padding="18" Margin="0,0,0,14">
                        <StackPanel>
                            <TextBlock Text="MAKE A MONTAGE" Foreground="{StaticResource TextMuted}" FontSize="10" FontWeight="Bold"/>
                            <TextBlock Text="Stitches all your clips below into one video with music. Clip your moments in-game with your clip hotkey; they show up here."
                                       Foreground="{StaticResource TextMuted}" FontSize="11" TextWrapping="Wrap" Margin="0,8,0,0"/>
                            <DockPanel LastChildFill="False" Margin="0,12,0,0">
                                <TextBlock Text="Music" Foreground="{StaticResource TextSoft}" FontSize="12" VerticalAlignment="Center" Margin="0,0,10,0"/>
                                <ComboBox x:Name="MusicCombo" Width="220" VerticalAlignment="Center"/>
                                <Button Content="Use my file…" Margin="10,0,0,0" Padding="10,4" Click="MontagePickMusic_Click"/>
                            </DockPanel>
                            <CheckBox x:Name="MontageVerticalCheck" Content="Vertical (TikTok / Shorts)"
                                      Foreground="{StaticResource TextSoft}" Margin="0,12,0,0"
                                      Checked="MontageVertical_Changed" Unchecked="MontageVertical_Changed"/>
                            <Button x:Name="MontageButton" Content="Make montage" Padding="16,8" Margin="0,14,0,0"
                                    HorizontalAlignment="Left" Click="MakeMontage_Click"/>
                            <TextBlock x:Name="ClipStudioStatus" Text="" Foreground="{StaticResource TextMuted}"
                                       FontSize="12" TextWrapping="Wrap" Margin="0,10,0,0"/>
                        </StackPanel>
                    </Border>

                    <Border Background="{StaticResource Panel}" CornerRadius="2" BorderThickness="1" BorderBrush="{StaticResource Outline}" Padding="18" Margin="0,0,0,14">
                        <StackPanel>
                            <TextBlock Text="YOUR CLIPS" Foreground="{StaticResource TextMuted}" FontSize="10" FontWeight="Bold" Margin="0,0,0,8"/>
                            <ItemsControl x:Name="ClipList"/>
                        </StackPanel>
                    </Border>
                </StackPanel>
```

- [ ] **Step 3: Register the page in the arrays**

In `MainWindow.xaml.cs`, add `NavClipStudio` to the `NavButtons` array (after `NavHistory`) and `PageClipStudio` to the `Pages` array (after `PageHistory`).

- [ ] **Step 4: Build to confirm the XAML compiles**

Run: `dotnet build JinxyClicker.csproj -c Debug`
Expected: Build succeeded. (Handlers are added in Task 7; this step may fail to build until Task 7's handlers exist — if so, do Task 7 Step 1 first, then build. They are one deliverable; commit them together.)

- [ ] **Step 5: Commit (with Task 7)**

Commit the page and handlers together at the end of Task 7.

---

### Task 7: CLIP STUDIO handlers + Pro gating

**Files:**
- Create: `MainWindow.ClipStudio.cs`
- Modify: `MainWindow.xaml.cs` (call `LoadClipStudio()` where other pages load their state; expose `AppFolder`)

**Interfaces:**
- Consumes: `ClipLibrary`, `MontageMaker`, `MusicLibrary`, `IsPro`, `ClipFolder`, `_settings`-style fields (`MontageVertical`/`MontageMusic` via the save-builder), `ShowPage`.
- Produces: `NavClipStudio_Click`, `MakeMontage_Click`, `MontagePickMusic_Click`, `MontageVertical_Changed`, and `LoadClipStudio()`.

- [ ] **Step 1: Write the partial**

Create `MainWindow.ClipStudio.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace JinxyClicker;

public partial class MainWindow
{
    private bool _montageVertical;
    private string? _montageMusic;

    private static string AppFolder => AppDomain.CurrentDomain.BaseDirectory;

    private void LoadClipStudio(AppSettings s)
    {
        _montageVertical = s.MontageVertical;
        _montageMusic = s.MontageMusic;
        if (MontageVerticalCheck is not null) MontageVerticalCheck.IsChecked = _montageVertical;
        PopulateMusic();
    }

    private void NavClipStudio_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(NavClipStudio, PageClipStudio, "Clip Studio", "Turn your clips into a montage");
        ApplyClipStudioPro();
        RefreshClipList();
    }

    // Locks the montage controls when not Pro; the clip list stays visible either way.
    private void ApplyClipStudioPro()
    {
        bool pro = IsPro;
        if (ClipStudioLockNote is not null)
            ClipStudioLockNote.Visibility = pro ? Visibility.Collapsed : Visibility.Visible;
        if (MontageButton is not null) MontageButton.IsEnabled = pro;
        if (MusicCombo is not null) MusicCombo.IsEnabled = pro;
        if (MontageVerticalCheck is not null) MontageVerticalCheck.IsEnabled = pro;
    }

    private void PopulateMusic()
    {
        if (MusicCombo is null) return;
        MusicCombo.Items.Clear();
        foreach (string track in MusicLibrary.Bundled(AppFolder))
            MusicCombo.Items.Add(Path.GetFileName(track));
        // Reflect a saved own-file choice as its file name too.
        if (!string.IsNullOrEmpty(_montageMusic) && !MusicCombo.Items.Contains(Path.GetFileName(_montageMusic)))
            MusicCombo.Items.Add(Path.GetFileName(_montageMusic));
        if (_montageMusic != null) MusicCombo.SelectedItem = Path.GetFileName(_montageMusic);
        else if (MusicCombo.Items.Count > 0) MusicCombo.SelectedIndex = 0;
    }

    private void RefreshClipList()
    {
        if (ClipList is null) return;
        var clips = ClipLibrary.List(ClipFolder);
        ClipList.Items.Clear();
        foreach (ClipInfo clip in clips)
            ClipList.Items.Add(BuildClipRow(clip));
        if (clips.Count == 0)
            ClipStudioStatus.Text = "No clips yet. Press your clip hotkey in-game to save one.";
    }

    private FrameworkElement BuildClipRow(ClipInfo clip)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = false };
        var name = new TextBlock
        {
            Text = $"{clip.Name}   ({clip.SavedUtc.ToLocalTime():g})",
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(name);

        var del = new Button { Content = "Delete", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(del, Dock.Right);
        del.Click += (_, _) => { ClipLibrary.Delete(clip.Path); RefreshClipList(); };
        row.Children.Add(del);

        var play = new Button { Content = "Play", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(play, Dock.Right);
        play.Click += (_, _) => OpenWithShell(clip.Path);
        row.Children.Add(play);

        return row;
    }

    private static void OpenWithShell(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }

    private void MontageVertical_Changed(object sender, RoutedEventArgs e)
    {
        _montageVertical = MontageVerticalCheck.IsChecked == true;
        _settingsDirty = true;
    }

    private void MontagePickMusic_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Audio (*.mp3;*.wav;*.m4a)|*.mp3;*.wav;*.m4a" };
        if (dlg.ShowDialog() == true)
        {
            _montageMusic = dlg.FileName;
            _settingsDirty = true;
            PopulateMusic();
        }
    }

    private async void MakeMontage_Click(object sender, RoutedEventArgs e)
    {
        if (!IsPro) return;

        var clips = ClipLibrary.List(ClipFolder).Select(c => c.Path).Reverse().ToList(); // oldest first
        if (clips.Count == 0) { ClipStudioStatus.Text = "No clips to montage yet."; return; }

        // Resolve the chosen music: a selected bundled name, or the saved own-file path.
        string? chosen = _montageMusic;
        if (MusicCombo?.SelectedItem is string picked &&
            (string.IsNullOrEmpty(_montageMusic) || Path.GetFileName(_montageMusic) != picked))
            chosen = picked;
        string? music = MusicLibrary.Resolve(chosen, AppFolder);

        MontageButton.IsEnabled = false;
        ClipStudioStatus.Text = "Making your montage… this can take a minute.";
        try
        {
            MontageShape shape = _montageVertical ? MontageShape.Vertical : MontageShape.Landscape;
            string? outPath = await MontageMaker.RenderAsync(clips, music, shape, ClipFolder);
            if (outPath != null)
            {
                ClipStudioStatus.Text = "Montage ready: " + Path.GetFileName(outPath);
                OpenWithShell(outPath);
                RefreshClipList();
            }
            else
            {
                ClipStudioStatus.Text = "Montage failed. Make sure your clips still exist and try again.";
            }
        }
        finally
        {
            MontageButton.IsEnabled = IsPro;
        }
    }
}
```

- [ ] **Step 2: Load it at startup and in the save-builder**

In `MainWindow.xaml.cs`:
- In `ApplyAppSettings`, next to `LoadLicense(s);`, add `LoadClipStudio(s);`.
- In `SaveAppSettings`'s `new AppSettings { ... }`, add `MontageVertical = _montageVertical, MontageMusic = _montageMusic,`.

- [ ] **Step 3: Build**

Run: `dotnet build JinxyClicker.csproj -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test Testing/JinxyClicker.Tests.csproj`
Expected: PASS (all).

- [ ] **Step 5: Manual smoke test**

Run the app (as Pro):
- Open CLIP STUDIO (CAPTURE group). Montage controls enabled; music dropdown has the two bundled tracks.
- Turn on Instant Replay (Recorder page), play/record something, press the clip hotkey twice to make 2 clips.
- Back on CLIP STUDIO, the 2 clips list. Click "Make montage" → after a short render, a `montage-*.mp4` opens and plays with music.
- Tick "Vertical", make montage again → output is 9:16.
- Set the app to free (clear the license) → the lock note shows and the montage button is disabled; the clip list still shows.

Expected: all behaviors hold.

- [ ] **Step 6: Commit**

```bash
git add MainWindow.xaml MainWindow.xaml.cs MainWindow.ClipStudio.cs
git commit -m "Add the Clip Studio page: clip library and one-button montage"
```

---

## Self-review notes

- Spec coverage: hotkey clipping (existing `ReplayBuffer`, unchanged), clip library (Task 1 + Task 7 UI), montage stitch+music (Tasks 2–4, 7), vertical export (Task 2 + toggle in 6/7), music bundled+own-file (Task 4), Pro-gating (Task 7 `ApplyClipStudioPro`), lives in CAPTURE group (Task 6).
- Review Focus inputs each have a test or an explicit guard: no clips (Task 1 empty + Task 7 guard), deleted/locked clip mid-montage (Task 3 null return + Task 7 "failed" message), ffmpeg missing (Task 3 null + Task 7), mixed sizes (Task 2 per-input scale+pad), short music (Task 2 `-stream_loop -1`).
- Type consistency: `ClipInfo`, `MontageShape`, `MontageMaker.BuildArgs/RenderAsync`, `ClipLibrary.List/Delete`, `MusicLibrary.Bundled/Resolve` used identically across tasks.
- The bundled music is generated, license-clean placeholder loops; the design permits swapping in better CC0 tracks by dropping files in `music/`.
