using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace MyBlinkStyleClicker;

/// <summary>
/// A saved CPS / CDC pair. Built-in presets ship with the app; custom ones are
/// user-created and persisted.
/// </summary>
public sealed class ClickPreset : INotifyPropertyChanged
{
    /// <summary>Widest CPS the bar is scaled against — matches the slider maximum.</summary>
    private const double BarScaleCps = 150.0;
    private const double BarTrackWidth = 134.0;

    public ClickPreset(string name, double cps, double cdc)
    {
        Name = name;
        Cps = cps;
        Cdc = cdc;
    }

    public string Name { get; }
    public double Cps { get; }
    public double Cdc { get; }

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

    private sealed record StoredPreset(string Name, double Cps, double Cdc);

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
                .Select(p => new ClickPreset(p.Name, p.Cps, p.Cdc))
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
                .Select(p => new StoredPreset(p.Name, p.Cps, p.Cdc))
                .ToList();

            File.WriteAllText(PRESETS_FILE,
                JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
