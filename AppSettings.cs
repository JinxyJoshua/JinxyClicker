using System;
using System.IO;
using System.Text.Json;

namespace MyBlinkStyleClicker;

/// <summary>
/// Everything on the Clicker page that the user tunes, so a session starts where
/// the last one left off. Hotkeys and presets have their own files; this covers
/// the sliders, the direction fields and the toggles.
/// </summary>
public sealed class AppSettings
{
    private const string SETTINGS_FILE = "app_settings.json";

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
