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

    public double ShakeLeft { get; set; } = 8;
    public double ShakeRight { get; set; } = 20;
    public double ShakeUp { get; set; } = 40;
    public double ShakeDown { get; set; } = 8;

    /// <summary>A snapshot the user can return to after experimenting.</summary>
    public bool HasSavedShake { get; set; }
    public double SavedShakeLeft { get; set; }
    public double SavedShakeRight { get; set; }
    public double SavedShakeUp { get; set; }
    public double SavedShakeDown { get; set; }

    public bool ShakyTracking { get; set; }
    public bool UltraAccuracy { get; set; }
    public bool PingSync { get; set; }
    public bool HitFix { get; set; } = true;
    public bool HoldMode { get; set; }

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

    /// <summary>Where clips are written. Null or empty means the Videos folder.</summary>
    public string? ClipFolder { get; set; }

    /// <summary>Capture framerate for both the recorder and the replay buffer.</summary>
    public int RecordFps { get; set; } = 30;

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
            if (!File.Exists(SETTINGS_FILE)) return new AppSettings();

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SETTINGS_FILE))
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

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
