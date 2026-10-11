using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace JinxyClicker;

/// <summary>
/// The custom Roblox font feature (Plus, on the MOD page): pick a bundled preset
/// or import a .ttf/.otf, and it replaces Roblox's default font. The chosen font's
/// bytes live at a fixed path in the app's data folder so the swap can be
/// re-applied after a Roblox update, exactly like the crosshair. The file
/// operations live in <see cref="RobloxFonts"/>.
/// </summary>
public partial class MainWindow
{
    private string _customFontName = "";
    private bool _customFontApplied;
    private Border? _selectedFontTile;

    // Bundled presets (file in the Fonts\ folder, name shown to the user). All are
    // SIL Open Font License — see Fonts\FONTS-LICENSE.txt. "Minecraft" is Monocraft,
    // the open Minecraft-style font, so nothing personal-use-only ships here.
    private static readonly (string File, string Name)[] Presets =
    {
        ("Monocraft.ttf", "Minecraft"),
        ("PressStart2P.ttf", "Press Start 2P"),
        ("VT323.ttf", "VT323"),
        ("Silkscreen.ttf", "Silkscreen"),
        ("Monoton.ttf", "Monoton"),
        ("Orbitron.ttf", "Orbitron"),
        ("Audiowide.ttf", "Audiowide"),
        ("ChakraPetch.ttf", "Chakra Petch"),
        ("Rajdhani.ttf", "Rajdhani"),
        ("Teko.ttf", "Teko"),
        ("RussoOne.ttf", "Russo One"),
        ("BebasNeue.ttf", "Bebas Neue"),
        ("Bungee.ttf", "Bungee"),
        ("Oswald.ttf", "Oswald"),
        ("Creepster.ttf", "Creepster"),
        ("Pacifico.ttf", "Pacifico"),
        ("Lobster.ttf", "Lobster"),
    };

    private static string PresetsDir => Path.Combine(AppContext.BaseDirectory, "Fonts");

    // Where the chosen font's bytes are kept. The stored name is irrelevant to
    // Roblox — its bytes are copied over each default face by their own names.
    private static string FontStorePath => SettingsPath.For("customfont.dat");

    private void LoadFonts(AppSettings s)
    {
        _customFontName = s.CustomFontName ?? "";
        _customFontApplied = s.CustomFontApplied;

        BuildFontPresets();

        if (_customFontApplied)
        {
            if (IsPro)
            {
                ReapplyFontIfNeeded();
            }
            else
            {
                // Plus has lapsed. Don't keep forcing the font back, and restore
                // Roblox's default now — Reset lives on the gated MOD page, so a
                // Free user would otherwise be stuck with a font they can't undo.
                string? dir = RobloxFonts.FindFontsFolder();
                if (dir != null) { try { RobloxFonts.Reset(dir); } catch { /* retry next launch */ } }
                _customFontApplied = false;
                _settingsDirty = true;
            }
        }

        UpdateFontUi();
    }

    // Re-applies the chosen font if it's meant to be on but the live Roblox folder
    // isn't swapped — e.g. a Roblox update laid down a fresh folder with the stock
    // fonts. Best effort: a running Roblox holds the files, so the MOD buttons retry.
    private void ReapplyFontIfNeeded()
    {
        if (!_customFontApplied || !File.Exists(FontStorePath)) return;
        string? dir = RobloxFonts.FindFontsFolder();
        if (dir != null && !RobloxFonts.IsApplied(dir))
        {
            try { RobloxFonts.Apply(dir, FontStorePath); }
            catch { /* best effort */ }
        }
    }

    // Called when the MOD page is opened, so a Roblox update mid-session is caught
    // without restarting Jinxy, and the status reflects the live state.
    private void RefreshFontsOnShow()
    {
        if (IsPro) ReapplyFontIfNeeded();
        UpdateFontUi();
    }

    // Builds one clickable preview tile per preset, each rendered in its own font.
    private void BuildFontPresets()
    {
        if (FontPresetPanel is null || FontPresetPanel.Children.Count > 0) return;

        foreach ((string file, string name) in Presets)
        {
            string path = Path.Combine(PresetsDir, file);
            if (!File.Exists(path)) continue;
            Border tile = BuildPresetTile(path, name);
            FontPresetPanel.Children.Add(tile);
            // Restore the highlight on the font that's currently chosen.
            if (name == _customFontName) HighlightPresetTile(tile);
        }
    }

    private Border BuildPresetTile(string fontPath, string name)
    {
        var preview = new TextBlock
        {
            Text = "Ag 12",
            FontSize = 22,
            Foreground = (Brush)FindResource("TextBright"),
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        // Render the sample in the actual font so the tile is a real preview.
        try
        {
            FontFamily? fam = Fonts.GetFontFamilies(fontPath).FirstOrDefault();
            if (fam != null) preview.FontFamily = fam;
        }
        catch { /* leave in the default font if this one won't load */ }

        var label = new TextBlock
        {
            Text = name,
            FontSize = 10,
            Foreground = (Brush)FindResource("TextMuted"),
            Margin = new Thickness(0, 6, 0, 0),
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var stack = new StackPanel { Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(preview);
        stack.Children.Add(label);

        var tile = new Border
        {
            Width = 136,
            Height = 76,
            Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(6),
            Background = (Brush)FindResource("Panel"),
            BorderBrush = (Brush)FindResource("Outline"),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = stack,
            Tag = (fontPath, name),
            ToolTip = $"Use the {name} font",
        };
        tile.MouseLeftButtonUp += FontPreset_Click;
        return tile;
    }

    private void FontPreset_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border tile || tile.Tag is not ValueTuple<string, string> pick) return;
        (string path, string name) = pick;

        if (!File.Exists(path)) { SetFontStatus("That preset font is missing from this build."); return; }

        try
        {
            File.Copy(path, FontStorePath, overwrite: true);
            _customFontName = name;
            // A fresh pick isn't on Roblox until Apply, so it must not be re-applied
            // after a Roblox update in place of whatever is actually live.
            _customFontApplied = false;
            _settingsDirty = true;
            HighlightPresetTile(tile);
            SetFontStatus($"\"{name}\" selected. Click Apply to put it on Roblox.");
        }
        catch (Exception ex)
        {
            SetFontStatus("Couldn't select that font: " + ex.Message);
        }
        UpdateFontUi();
    }

    private void HighlightPresetTile(Border? tile)
    {
        if (_selectedFontTile is not null)
        {
            _selectedFontTile.BorderBrush = (Brush)FindResource("Outline");
            _selectedFontTile.BorderThickness = new Thickness(1);
        }

        _selectedFontTile = tile;
        if (tile is not null)
        {
            tile.BorderBrush = (Brush)FindResource("Accent");
            tile.BorderThickness = new Thickness(2);
        }
    }

    private void ImportFont_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose a font",
            Filter = "Fonts (*.ttf;*.otf)|*.ttf;*.otf|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) != true) return;

        // Reject anything that isn't a real, loadable font before it can be written
        // over Roblox's faces (the dialog's "All files" option lets any file through).
        try { _ = new GlyphTypeface(new Uri(dlg.FileName)); }
        catch
        {
            SetFontStatus("That file isn't a usable font — pick a .ttf or .otf.");
            return;
        }

        try
        {
            File.Copy(dlg.FileName, FontStorePath, overwrite: true);
            _customFontName = Path.GetFileNameWithoutExtension(dlg.FileName);
            _customFontApplied = false;   // not on Roblox until Apply
            _settingsDirty = true;
            HighlightPresetTile(null);   // an imported font is no preset
            SetFontStatus($"Imported \"{_customFontName}\". Click Apply to put it on Roblox.");
        }
        catch (Exception ex)
        {
            SetFontStatus("Couldn't read that font file: " + ex.Message);
        }
        UpdateFontUi();
    }

    private void ApplyFont_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(FontStorePath)) { SetFontStatus("Pick a preset or import a font first."); return; }

        string? dir = RobloxFonts.FindFontsFolder();
        if (dir == null)
        {
            SetFontStatus("Couldn't find your Roblox install. Open Roblox once, then try again.");
            return;
        }

        try
        {
            int n = RobloxFonts.Apply(dir, FontStorePath);
            if (n == 0) { SetFontStatus("Couldn't apply — no default fonts were found to replace."); return; }

            _customFontApplied = true;
            _settingsDirty = true;
            SetFontStatus("Font applied. Restart Roblox (or rejoin a game) to see it.");
        }
        catch (Exception ex)
        {
            SetFontStatus("Couldn't apply the font — is Roblox open? Close it and try again. (" + ex.Message + ")");
        }
        UpdateFontUi();
    }

    private void ResetFont_Click(object sender, RoutedEventArgs e)
    {
        string? dir = RobloxFonts.FindFontsFolder();
        int restored;
        try
        {
            restored = dir == null ? 0 : RobloxFonts.Reset(dir);
        }
        catch (Exception ex)
        {
            // A running Roblox holds the files — don't crash, and don't claim it's
            // reset when it isn't.
            SetFontStatus("Couldn't restore — is Roblox open? Close it and try again. (" + ex.Message + ")");
            return;
        }

        _customFontApplied = false;
        _settingsDirty = true;
        HighlightPresetTile(null);
        SetFontStatus(restored > 0
            ? "Restored Roblox's default font. Restart Roblox to see it."
            : "Nothing to restore — Roblox is already on its default font.");
        UpdateFontUi();
    }

    private void UpdateFontUi()
    {
        bool hasFont = _customFontName.Length > 0 && File.Exists(FontStorePath);
        string? dir = RobloxFonts.FindFontsFolder();
        bool liveApplied = dir != null && RobloxFonts.IsApplied(dir);

        if (FontCurrentText is not null)
            FontCurrentText.Text = hasFont ? $"Selected: {_customFontName}" : "No font selected yet.";

        if (FontStateText is not null)
            FontStateText.Text = liveApplied ? "Applied" : hasFont ? "Not applied" : "";

        if (ApplyFontButton is not null) ApplyFontButton.IsEnabled = hasFont;
        if (ResetFontButton is not null) ResetFontButton.IsEnabled = liveApplied || _customFontApplied;
    }

    private void SetFontStatus(string text)
    {
        if (FontStatusText is not null) FontStatusText.Text = text;
    }
}
