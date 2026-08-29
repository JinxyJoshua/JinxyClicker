using System;
using System.IO;
using System.Text.Json;

namespace JinxyClicker;

/// <summary>
/// Everything on the Clicker page that the user tunes, so a session starts where
/// the last one left off. Hotkeys and presets have their own files; this covers
/// the sliders, the direction fields and the toggles.
/// </summary>
public sealed class AppSettings
{
    private static readonly string SETTINGS_FILE = SettingsPath.For("app_settings.json");

    public double Cps { get; set; } = 10;
    public double Cdc { get; set; } = 67;

    // Tuned by hand against a live client and kept. The old 8/20/40/8 was a
    // guess with a 40px vertical throw that read as a flick rather than a shake.
    public double ShakeLeft { get; set; } = 7;
    public double ShakeRight { get; set; } = 9;
    public double ShakeUp { get; set; } = 6;
    public double ShakeDown { get; set; } = 5;

    /// <summary>A snapshot the user can return to after experimenting.</summary>
    public bool HasSavedShake { get; set; }
    public double SavedShakeLeft { get; set; }
    public double SavedShakeRight { get; set; }
    public double SavedShakeUp { get; set; }
    public double SavedShakeDown { get; set; }

    /// <summary>Shake movements per second.</summary>
    public double ShakeSpeed { get; set; } = 50;

    public bool ShakyTracking { get; set; }
    public bool UltraAccuracy { get; set; }
    public bool PingSync { get; set; }
    public bool HitFix { get; set; } = true;
    public bool HoldMode { get; set; }

    /// <summary>Which button is clicked: Left, Right or Middle.</summary>
    /// <remarks>
    /// Stored by name rather than as a number, so the file stays readable and a
    /// reordered enum cannot silently change what an existing setting means.
    /// </remarks>
    public string ClickButton { get; set; } = "Left";

    public bool HideValues { get; set; }

    public bool ReplayEnabled { get; set; }
    public int ReplaySeconds { get; set; } = 30;

    /// <summary>The accent colour, as hex. Must match one of the Theme swatches
    /// to be restored; anything else falls back to the default red.</summary>
    public string AccentColor { get; set; } = "#FF4B52";

    /// <summary>Window opacity, 0.4 to 1. Clamped to the slider on load, so a
    /// hand-edited zero cannot produce a window nobody can see.</summary>
    public double WindowOpacity { get; set; } = 1.0;

    /// <summary>Light mode. Dark is the default and the designed appearance.</summary>
    public bool LightTheme { get; set; }

    /// <summary>Bare file name of the wallpaper copy, or empty for none.</summary>
    /// <remarks>
    /// A name rather than a path, because the picture is copied into the
    /// settings folder and the original is not depended on afterwards.
    /// </remarks>
    public string WallpaperFile { get; set; } = "";

    /// <summary>How far the wallpaper is darkened, as a percentage.</summary>
    public int WallpaperDimming { get; set; } = Wallpaper.DefaultDimming;

    /// <summary>Where clips are written. Null or empty means the Videos folder.</summary>
    public string? ClipFolder { get; set; }

    /// <summary>Capture framerate for both the recorder and the replay buffer.</summary>
    /// <summary>The two hotbar slots the auto switcher swaps between.</summary>
    public string SwitcherSlotA { get; set; } = "3";
    public string SwitcherSlotB { get; set; } = "1";
    public int SwitcherIntervalMs { get; set; } = 150;

    /// <summary>How long the second slot is held. Usually a tap.</summary>
    public int SwitcherIntervalBMs { get; set; } = 900;

    /// <summary>How long the game takes to equip a weapon, in the user's judgement.</summary>
    public int SwitcherEquipMs { get; set; } = 60;

    public int RecordFps { get; set; } = 30;

    /// <summary>Keeps the Roblox process above normal priority while on.</summary>
    public bool RobloxPriority { get; set; }

    /// <summary>Master switch for all four hotkeys. On by default — off is a
    /// deliberate act, not a state to wake up in and be confused by.</summary>
    public bool HotkeysEnabled { get; set; } = true;

    /// <summary>Device name of the monitor to capture, or null for all of them.
    /// Stored by name rather than index so unplugging a screen cannot silently
    /// repoint the recording at a different one.</summary>
    public string? RecordDisplay { get; set; }

    /// <summary>
    /// Null when nothing has been stored yet, so a first run uses the designed
    /// size rather than a zero-sized window.
    /// </summary>
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public bool WindowMaximized { get; set; }

    /// <summary>A missing or unreadable file is not an error — it just means defaults.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SETTINGS_FILE)) return FreshInstall();

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SETTINGS_FILE))
                   ?? new AppSettings();
        }
        catch
        {
            // Unreadable is not fresh — leave the wallpaper alone rather than
            // installing the default on top of whatever the user picked.
            return new AppSettings();
        }
    }

    /// <summary>
    /// The first-run defaults, which include installing the shipped wallpaper.
    /// </summary>
    /// <remarks>
    /// Kept separate from the plain constructor so anyone constructing an
    /// AppSettings for tests or migrations does not accidentally trigger a
    /// disk write to the settings folder.
    /// </remarks>
    private static AppSettings FreshInstall() => new()
    {
        WallpaperFile = Wallpaper.InstallDefault()
    };

    public void Save()
    {
        try
        {
            File.WriteAllText(SETTINGS_FILE,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
