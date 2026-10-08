using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace JinxyClicker;

/// <summary>
/// The crosshair page: a gallery of ready-made and home-made crosshairs, a size,
/// and the buttons that write the chosen one into Roblox's cursors or put them
/// back.
/// </summary>
/// <remarks>
/// The crosshair is not a window drawn over the game — it is baked into Roblox's
/// own cursor pictures by <see cref="RobloxCursors"/>, so it is the real cursor
/// you aim with, even in first person. The same <see cref="CrosshairImage"/> that
/// renders those cursor files renders the gallery tiles and the preview, so the
/// tile, the preview and the applied cursor are one picture.
///
/// <para>Each crosshair keeps its own size, so turning a dot up does not blow up
/// a sniper cross. The size is written into the cursor image's dimensions, not
/// just its contents, because Roblox draws the cursor at the image's own pixel
/// size.</para>
/// </remarks>
public partial class MainWindow
{
    private RobloxCursors? _cursors;
    private RobloxCursors CursorWriter => _cursors ??= new RobloxCursors();

    private string _crosshairName = CrosshairGallery.Default.Name;
    private bool _crosshairApplied;
    private Dictionary<string, int> _crosshairSizes = new();
    private List<CustomCrosshair> _customCrosshairs = new();
    private readonly List<Border> _crosshairTiles = new();
    private bool _galleryBuilt;

    // The crosshair being built in the "make your own" card.
    private CrosshairShape _customShape = CrosshairShape.Cross;
    private string _customColor = "#33FF66";
    private string? _customDotColor = "#FFFFFF";

    private const int DefaultSizePercent = 100;

    /// <summary>The gallery entry that means "Roblox's own cursor, no crosshair".</summary>
    private const string DefaultCursorName = "Default";

    private bool IsDefaultSelected => string.Equals(_crosshairName, DefaultCursorName, StringComparison.OrdinalIgnoreCase);

    private static readonly string[] CrosshairPalette =
    {
        "#33FF66", "#00E5FF", "#45B7FF", "#FF3B3B", "#FF2D55", "#FF57E6",
        "#FFE14D", "#FF9C3B", "#A855F7", "#8B5CF6", "#FFFFFF", "#111111"
    };

    private void NavCrosshair_Click(object sender, RoutedEventArgs e) =>
        ShowPage(NavCrosshair, PageCrosshair, "Crosshair", "A crosshair in place of the Roblox cursor");

    /// <summary>Puts the saved crosshair onto the controls, preview and status.</summary>
    private void LoadCrosshair(AppSettings s)
    {
        _crosshairSizes = s.CrosshairSizes ?? new Dictionary<string, int>();
        _customCrosshairs = s.CustomCrosshairs ?? new List<CustomCrosshair>();
        _crosshairName = StyleName(s.CrosshairName);

        BuildCrosshairGallery();
        BuildCustomSwatches();
        HighlightCrosshairTile();
        SetSizeSlider(SizePercentFor(_crosshairName));

        RefreshCustomPreview();

        RefreshCrosshairPreview();

        // If a crosshair was on last time and this isn't the default, put it back
        // now — a Roblox update since then would have replaced the folder with the
        // stock cursor, and re-writing it to every current folder is what keeps it
        // working without the user noticing anything happened.
        if (s.CrosshairApplied && !IsDefaultSelected)
        {
            UpdateCrosshairStatus(ApplyCurrentCrosshair());
        }
        else
        {
            _crosshairApplied = CursorWriter.IsApplied();
            UpdateCrosshairStatus();
        }
    }

    // ---- resolving a name to a style, built-in or home-made ----

    private string StyleName(string? name)
    {
        if (string.Equals(name, DefaultCursorName, StringComparison.OrdinalIgnoreCase)) return DefaultCursorName;
        if (CrosshairGallery.IsBuiltIn(name)) return CrosshairGallery.ByName(name).Name;
        CustomCrosshair? c = _customCrosshairs.FirstOrDefault(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        return c?.Name ?? CrosshairGallery.Default.Name;
    }

    private CrosshairStyle StyleFor(string name)
    {
        if (CrosshairGallery.IsBuiltIn(name)) return CrosshairGallery.ByName(name).Style;
        CustomCrosshair? c = _customCrosshairs.FirstOrDefault(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        return c?.ToStyle() ?? CrosshairGallery.Default.Style;
    }

    private CrosshairStyle CurrentCrosshairStyle() => StyleFor(_crosshairName);

    /// <summary>The custom entry for a name, or null if it is built-in or Default.</summary>
    private CustomCrosshair? CustomByName(string name) =>
        _customCrosshairs.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The imported image for the current crosshair, or null if it is not one.</summary>
    private string? CurrentImagePath()
    {
        CustomCrosshair? c = CustomByName(_crosshairName);
        return c is { IsImage: true } ? CrosshairStore.Resolve(c.ImageFile!) : null;
    }

    // ---- size, kept per crosshair ----

    private int SizePercentFor(string name) =>
        _crosshairSizes.TryGetValue(name, out int pct) ? pct : DefaultSizePercent;

    private double SizeFactor() => (CrosshairSizeSlider?.Value ?? DefaultSizePercent) / 100.0;

    private void SetSizeSlider(int percent)
    {
        if (CrosshairSizeSlider == null) return;
        CrosshairSizeSlider.Value = Math.Clamp(percent, CrosshairSizeSlider.Minimum, CrosshairSizeSlider.Maximum);
    }

    // ---- the gallery of tiles ----

    private void BuildCrosshairGallery()
    {
        if (CrosshairGalleryPanel == null || _galleryBuilt) return;
        _galleryBuilt = true;

        AddDefaultTile();

        foreach ((string name, CrosshairStyle style) in CrosshairGallery.All)
            AddTile(name, CrosshairImage.RenderBitmap(style, 48, 1.15), custom: false);

        foreach (CustomCrosshair c in _customCrosshairs)
            AddTile(c.Name, CustomIcon(c), custom: true);
    }

    /// <summary>The first tile: Roblox's own cursor, for switching the crosshair off.</summary>
    private void AddDefaultTile()
    {
        var image = new Image
        {
            Width = 46,
            Height = 46,
            Stretch = Stretch.Uniform,
            Source = CrosshairImage.RenderDefaultCursor(48)
        };

        var label = new TextBlock
        {
            Text = "Default",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextMuted"),
            Width = 96,
            Margin = new Thickness(0, 6, 0, 0),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };

        var stack = new StackPanel { Margin = new Thickness(8) };
        stack.Children.Add(image);
        stack.Children.Add(label);

        var tile = new Border
        {
            Tag = DefaultCursorName,
            Child = stack,
            Width = 112,
            Margin = new Thickness(0, 0, 10, 10),
            CornerRadius = new CornerRadius(6),
            Background = (Brush)FindResource("Control"),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource("Outline"),
            Cursor = Cursors.Hand
        };
        tile.MouseLeftButtonUp += CrosshairTile_Click;

        _crosshairTiles.Add(tile);
        CrosshairGalleryPanel.Children.Add(tile);
    }

    /// <summary>The tile icon for a custom crosshair — its image, or its drawn shape.</summary>
    private static ImageSource? CustomIcon(CustomCrosshair c) =>
        c.IsImage
            ? CrosshairImage.RenderImageBitmap(CrosshairStore.Resolve(c.ImageFile!), 48, 1.15)
              ?? CrosshairImage.RenderBitmap(new CrosshairStyle(), 48, 1.15)
            : CrosshairImage.RenderBitmap(c.ToStyle(), 48, 1.15);

    private void AddTile(string name, ImageSource? icon, bool custom)
    {
        var image = new Image
        {
            Width = 46,
            Height = 46,
            Stretch = Stretch.Uniform,
            Source = icon
        };

        var label = new TextBlock
        {
            Text = name,
            FontSize = 11,
            Foreground = (Brush)FindResource("TextMuted"),
            Width = 96,
            Margin = new Thickness(0, 6, 0, 0),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };

        var stack = new StackPanel { Margin = new Thickness(8) };
        stack.Children.Add(image);
        stack.Children.Add(label);

        var content = new Grid();
        content.Children.Add(stack);

        if (custom)
        {
            // A small cross to delete a home-made crosshair, top-right.
            var del = new Button
            {
                Content = "✕",
                Tag = name,
                Width = 20,
                Height = 20,
                FontSize = 10,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 2, 0),
                Foreground = (Brush)FindResource("TextMuted"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "Delete this crosshair"
            };
            del.Click += CustomDelete_Click;
            content.Children.Add(del);
        }

        var tile = new Border
        {
            Tag = name,
            Child = content,
            Width = 112,
            Margin = new Thickness(0, 0, 10, 10),
            CornerRadius = new CornerRadius(6),
            Background = (Brush)FindResource("Control"),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource("Outline"),
            Cursor = Cursors.Hand
        };
        tile.MouseLeftButtonUp += CrosshairTile_Click;

        _crosshairTiles.Add(tile);
        CrosshairGalleryPanel.Children.Add(tile);
    }

    private void CrosshairTile_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b || b.Tag is not string name) return;
        SelectCrosshair(name, switched: true);
    }

    private void SelectCrosshair(string name, bool switched)
    {
        _crosshairName = name;
        HighlightCrosshairTile();
        SetSizeSlider(SizePercentFor(name));   // each crosshair brings its own size
        RefreshCrosshairPreview();

        if (switched && CrosshairStatus != null)
        {
            CrosshairStatus.Text = IsDefaultSelected
                ? "Roblox's normal cursor. Apply to Roblox to put it back, then relaunch."
                : _crosshairApplied
                    ? $"✓ Switched to {name} — Apply to Roblox to use it."
                    : $"✓ Switched to {name}. Apply to Roblox, then relaunch.";
        }

        SaveAppSettings();
    }

    private void HighlightCrosshairTile()
    {
        Brush outline = (Brush)FindResource("Outline");
        Brush accent = (Brush)FindResource("Accent");

        foreach (Border tile in _crosshairTiles)
        {
            bool chosen = tile.Tag is string name
                && string.Equals(name, _crosshairName, StringComparison.OrdinalIgnoreCase);

            tile.BorderBrush = chosen ? accent : outline;
            tile.BorderThickness = new Thickness(chosen ? 2 : 1);
        }
    }

    private void CrosshairSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // The size belongs to the crosshair on screen now, not to all of them.
        _crosshairSizes[_crosshairName] = (int)(CrosshairSizeSlider?.Value ?? DefaultSizePercent);

        RefreshCrosshairPreview();
        SaveAppSettings();
    }

    private void RefreshCrosshairPreview()
    {
        if (CrosshairPreviewImage == null) return;

        // Size means nothing for the plain cursor, so the whole row goes away for it.
        if (CrosshairSizeRow != null)
            CrosshairSizeRow.Visibility = IsDefaultSelected ? Visibility.Collapsed : Visibility.Visible;

        if (IsDefaultSelected)
        {
            CrosshairPreviewImage.Source = CrosshairImage.RenderDefaultCursor(132);
            return;
        }

        string? imagePath = CurrentImagePath();
        CrosshairPreviewImage.Source = imagePath != null
            ? CrosshairImage.RenderImageBitmap(imagePath, 132, SizeFactor())
            : CrosshairImage.RenderBitmap(CurrentCrosshairStyle(), 132, SizeFactor());

        if (CrosshairSizeValue != null)
            CrosshairSizeValue.Text = $"{(int)(CrosshairSizeSlider?.Value ?? DefaultSizePercent)}%";
    }

    // ---- apply and remove ----

    private void CrosshairApply_Click(object sender, RoutedEventArgs e)
    {
        // Applying "Default" means putting Roblox's own cursor back.
        if (IsDefaultSelected)
        {
            CrosshairRemove_Click(sender, e);
            return;
        }

        UpdateCrosshairStatus(ApplyCurrentCrosshair());
        SaveAppSettings();
    }

    /// <summary>
    /// Writes the chosen crosshair, at its chosen size, into Roblox's cursors.
    /// </summary>
    /// <remarks>
    /// The one place the write happens, so the Apply button and the re-apply after a
    /// Roblox update go through the same path and can never drift apart.
    /// </remarks>
    private CursorApplyResult ApplyCurrentCrosshair()
    {
        double factor = SizeFactor();
        string? imagePath = CurrentImagePath();
        CrosshairStyle style = CurrentCrosshairStyle();

        CursorApplyResult result = CursorWriter.Apply(basePixels => imagePath != null
            ? CrosshairImage.RenderImageCursorPng(imagePath, basePixels, factor)
            : CrosshairImage.RenderCursorPng(style, basePixels, factor));

        _crosshairApplied = result.AnyWritten || CursorWriter.IsApplied();
        return result;
    }

    private void CrosshairRemove_Click(object sender, RoutedEventArgs e)
    {
        int removed = CursorWriter.Remove();
        _crosshairApplied = CursorWriter.IsApplied();

        if (CrosshairStatus != null)
            CrosshairStatus.Text = removed > 0
                ? $"Removed — {removed} cursor file(s) back to normal. Relaunch Roblox."
                : "Nothing to remove.";

        if (CrosshairRemoveButton != null) CrosshairRemoveButton.IsEnabled = _crosshairApplied;
        SaveAppSettings();
    }

    private void UpdateCrosshairStatus(CursorApplyResult? applied = null)
    {
        if (CrosshairStatus != null)
        {
            if (applied is CursorApplyResult r)
            {
                CrosshairStatus.Text = (r.Written, r.Failed) switch
                {
                    (0, 0) => "No Roblox install found to apply to.",
                    ( > 0, > 0) => $"Applied to {r.Written} cursor file(s). {r.Failed} were in use — close Roblox and apply again.",
                    ( > 0, 0) => $"✓ Applied — fully close and reopen Roblox to see your crosshair.",
                    _ => "Could not apply — close Roblox and try again."
                };
            }
            else
            {
                CrosshairStatus.Text = _crosshairApplied
                    ? "A crosshair is applied. Relaunch Roblox to see it, or Remove to restore."
                    : "Pick a crosshair below, then Apply to Roblox.";
            }
        }

        if (CrosshairRemoveButton != null) CrosshairRemoveButton.IsEnabled = _crosshairApplied;
    }

    // ---- make your own ----

    private CrosshairStyle BuildingStyle() => new(
        _customShape, Size: 9, Thickness: 3, Gap: 4, DotSize: 3,
        ColorHex: _customColor, DotColorHex: _customDotColor);

    private void BuildCustomSwatches()
    {
        if (CustomColorPanel == null || CustomDotColorPanel == null) return;
        if (CustomColorPanel.Children.Count > 0) return;

        foreach (string hex in CrosshairPalette)
            CustomColorPanel.Children.Add(MakeSwatch(hex, isDot: false));

        // The centre colour can be "none", which just uses the main colour.
        CustomDotColorPanel.Children.Add(MakeNoneSwatch());
        foreach (string hex in CrosshairPalette)
            CustomDotColorPanel.Children.Add(MakeSwatch(hex, isDot: true));

        HighlightSwatches();
    }

    private Button MakeSwatch(string hex, bool isDot)
    {
        var b = new Button
        {
            Tag = hex,
            Width = 28,
            Height = 28,
            Margin = new Thickness(0, 0, 8, 8),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            Background = BrushFromHex(hex),
            Cursor = Cursors.Hand
        };
        if (isDot) b.Click += CustomDotColor_Click; else b.Click += CustomColor_Click;
        return b;
    }

    private Button MakeNoneSwatch()
    {
        var b = new Button
        {
            Tag = "none",
            Content = "None",
            FontSize = 10,
            Height = 28,
            MinWidth = 44,
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(0, 0, 8, 8),
            Cursor = Cursors.Hand
        };
        b.Click += CustomDotColor_Click;
        return b;
    }

    private void CustomShape_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton r && Enum.TryParse(r.Tag as string, out CrosshairShape shape))
        {
            _customShape = shape;
            RefreshCustomPreview();
        }
    }

    private void CustomColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string hex)
        {
            _customColor = hex;
            HighlightSwatches();
            RefreshCustomPreview();
        }
    }

    private void CustomDotColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag)
        {
            _customDotColor = tag == "none" ? null : tag;
            HighlightSwatches();
            RefreshCustomPreview();
        }
    }

    private void HighlightSwatches()
    {
        Mark(CustomColorPanel, _customColor);
        Mark(CustomDotColorPanel, _customDotColor ?? "none");

        static void Mark(WrapPanel? panel, string chosen)
        {
            if (panel == null) return;
            foreach (object child in panel.Children)
            {
                if (child is not Button b || b.Tag is not string tag) continue;
                bool on = string.Equals(tag, chosen, StringComparison.OrdinalIgnoreCase);
                b.BorderBrush = on
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF))
                    : new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
                b.BorderThickness = new Thickness(on ? 2 : 1);
            }
        }
    }

    private void RefreshCustomPreview()
    {
        if (CustomPreviewImage != null)
            CustomPreviewImage.Source = CrosshairImage.RenderBitmap(BuildingStyle(), 96, 1.15);
    }

    private void CustomAdd_Click(object sender, RoutedEventArgs e)
    {
        string name = NextCustomName();
        var custom = new CustomCrosshair
        {
            Name = name,
            Shape = _customShape.ToString(),
            Color = _customColor,
            DotColor = _customDotColor
        };

        _customCrosshairs.Add(custom);
        AddTile(name, CustomIcon(custom), custom: true);
        SelectCrosshair(name, switched: false);

        if (CrosshairStatus != null)
            CrosshairStatus.Text = $"✓ Added {name} to the gallery. Apply to Roblox, then relaunch.";

        SaveAppSettings();
    }

    /// <summary>Imports a crosshair image the user picked, and adds it to the gallery.</summary>
    private void CrosshairImport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a crosshair image",
            Filter = CrosshairStore.FileFilter,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true) return;

        string? stored = CrosshairStore.Store(dialog.FileName);
        if (stored == null)
        {
            MessageBox.Show(this,
                "That image could not be used. It may be open in another program, or in a format this app cannot read. A PNG with a transparent background works best.",
                "Import crosshair", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string name = NextImportName();
        var custom = new CustomCrosshair { Name = name, ImageFile = stored };

        _customCrosshairs.Add(custom);
        AddTile(name, CustomIcon(custom), custom: true);
        SelectCrosshair(name, switched: false);

        if (CrosshairStatus != null)
            CrosshairStatus.Text = $"✓ Imported {name}. Set the size, then Apply to Roblox and relaunch.";

        SaveAppSettings();
    }

    private string NextCustomName() => NextName("Custom");
    private string NextImportName() => NextName("Import");

    private string NextName(string prefix)
    {
        for (int i = 1; ; i++)
        {
            string name = $"{prefix} {i}";
            bool taken = _customCrosshairs.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (!taken) return name;
        }
    }

    private void CustomDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string name) return;

        // An imported crosshair owns a file on disk; take it with the entry.
        CustomCrosshair? removing = CustomByName(name);
        if (removing?.IsImage == true) CrosshairStore.Delete(removing.ImageFile);

        _customCrosshairs.RemoveAll(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        _crosshairSizes.Remove(name);

        Border? tile = _crosshairTiles.FirstOrDefault(t =>
            t.Tag is string n && string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        if (tile != null)
        {
            _crosshairTiles.Remove(tile);
            CrosshairGalleryPanel?.Children.Remove(tile);
        }

        // If the one being deleted was chosen, fall back to the first crosshair.
        if (string.Equals(_crosshairName, name, StringComparison.OrdinalIgnoreCase))
            SelectCrosshair(CrosshairGallery.Default.Name, switched: false);
        else
            SaveAppSettings();
    }

    private static SolidColorBrush BrushFromHex(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return new SolidColorBrush(Color.FromRgb(0x33, 0xFF, 0x66)); }
    }
}
