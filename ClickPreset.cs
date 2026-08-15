using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace JinxyClicker;

/// <summary>
/// A saved CPS / CDC pair. Built-in presets ship with the app; custom ones are
/// user-created and persisted.
/// </summary>
public sealed class ClickPreset : INotifyPropertyChanged
{
    /// <summary>Widest CPS the bar is scaled against — matches the slider maximum.</summary>
    private const double BarScaleCps = 150.0;
    private const double BarTrackWidth = 134.0;

    public ClickPreset(
        string name, double cps, double cdc,
        bool holdMode = false, bool ultraAccuracy = false, bool shaky = false,
        double shakeLeft = 8, double shakeRight = 20, double shakeUp = 40, double shakeDown = 8)
    {
        Name = name;
        Cps = cps;
        Cdc = cdc;
        HoldMode = holdMode;
        UltraAccuracy = ultraAccuracy;
        Shaky = shaky;
        ShakeLeft = shakeLeft;
        ShakeRight = shakeRight;
        ShakeUp = shakeUp;
        ShakeDown = shakeDown;
    }

    public string Name { get; }
    public double Cps { get; }
    public double Cdc { get; }

    /// <summary>
    /// A preset is a whole way of clicking, not two numbers. Applying one that
    /// left mode and shake untouched would still leave the session half set up.
    /// </summary>
    public bool HoldMode { get; }
    public bool UltraAccuracy { get; }
    public bool Shaky { get; }
    public double ShakeLeft { get; }
    public double ShakeRight { get; }
    public double ShakeUp { get; }
    public double ShakeDown { get; }

    /// <summary>Second line on the card: what this preset does beyond the rates.</summary>
    public string ExtrasText
    {
        get
        {
            var parts = new List<string> { HoldMode ? "Hold" : "Toggle" };

            if (UltraAccuracy) parts.Add("Ultra");

            if (Shaky)
            {
                parts.Add($"Shake {ShakeLeft:0}/{ShakeRight:0}/{ShakeUp:0}/{ShakeDown:0}");
            }

            return string.Join("  ·  ", parts);
        }
    }

    public string CpsText => Cps.ToString("0.0", CultureInfo.CurrentCulture);
    public string CdcText => Cdc.ToString("0.0", CultureInfo.CurrentCulture);

    /// <summary>Bar length, so a preset's speed is legible before reading the number.</summary>
    public double BarWidth => Math.Clamp(Cps / BarScaleCps, 0.015, 1.0) * BarTrackWidth;

    private bool _isApplied;

    /// <summary>True while the sliders match this preset, so the card can show it.</summary>
    public bool IsApplied
    {
        get => _isApplied;
        set
        {
            if (_isApplied == value) return;
            _isApplied = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsApplied)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Persists the whole preset list, not just user-created entries. Any preset can
/// be deleted, so the defaults have to be able to stay deleted — regenerating
/// them from code on every launch would resurrect what the user removed.
/// </summary>
public static class PresetStore
{
    private const string PRESETS_FILE = "click_presets.json";

    /// <summary>
    /// A class with initialisers rather than a positional record, so a file
    /// written before the profile fields existed still loads with sensible
    /// values instead of zeroes.
    /// </summary>
    private sealed class StoredPreset
    {
        public string Name { get; set; } = "";
        public double Cps { get; set; }
        public double Cdc { get; set; }
        public bool HoldMode { get; set; }
        public bool UltraAccuracy { get; set; }
        public bool Shaky { get; set; }
        public double ShakeLeft { get; set; } = 8;
        public double ShakeRight { get; set; } = 20;
        public double ShakeUp { get; set; } = 40;
        public double ShakeDown { get; set; } = 8;
    }

    /// <summary>The list a fresh install starts from, and what Restore Defaults adds back.</summary>
    public static List<ClickPreset> Defaults() => new()
    {
        new ClickPreset("Low", 8, 50),
        new ClickPreset("Normal", 10, 67),
        new ClickPreset("High", 16, 75),
        new ClickPreset("Fast", 20, 80),
        new ClickPreset("Max", 100, 100)
    };

    /// <summary>
    /// A missing file means first run, so seed the defaults. A file that exists
    /// but holds an empty list means the user deleted everything — respect it.
    /// </summary>
    public static List<ClickPreset> Load()
    {
        try
        {
            if (!File.Exists(PRESETS_FILE)) return Defaults();

            var stored = JsonSerializer.Deserialize<List<StoredPreset>>(File.ReadAllText(PRESETS_FILE));
            if (stored == null) return Defaults();

            return stored
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => new ClickPreset(
                    p.Name, p.Cps, p.Cdc,
                    p.HoldMode, p.UltraAccuracy, p.Shaky,
                    p.ShakeLeft, p.ShakeRight, p.ShakeUp, p.ShakeDown))
                .ToList();
        }
        catch
        {
            return Defaults();
        }
    }

    public static void Save(IEnumerable<ClickPreset> presets)
    {
        try
        {
            var stored = presets
                .Select(p => new StoredPreset
                {
                    Name = p.Name,
                    Cps = p.Cps,
                    Cdc = p.Cdc,
                    HoldMode = p.HoldMode,
                    UltraAccuracy = p.UltraAccuracy,
                    Shaky = p.Shaky,
                    ShakeLeft = p.ShakeLeft,
                    ShakeRight = p.ShakeRight,
                    ShakeUp = p.ShakeUp,
                    ShakeDown = p.ShakeDown
                })
                .ToList();

            File.WriteAllText(PRESETS_FILE,
                JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
