using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace JinxyClicker;

/// <summary>
/// The crosshair page: a gallery of ready-made crosshairs, a size, and the two
/// buttons that write the chosen one into Roblox's cursors or put them back.
/// </summary>
/// <remarks>
/// The crosshair is not a window drawn over the game — it is baked into Roblox's
/// own cursor pictures by <see cref="RobloxCursors"/>, so it is the real cursor
/// you aim with, even in first person. The same <see cref="CrosshairImage"/> that
/// renders those cursor files renders the gallery tiles and the preview, so the
/// tile, the preview and the applied cursor are one picture.
/// </remarks>
public partial class MainWindow
{
    private RobloxCursors? _cursors;
    private RobloxCursors CursorWriter => _cursors ??= new RobloxCursors();

    private string _crosshairName = CrosshairGallery.Default.Name;
    private bool _crosshairApplied;
    private readonly List<Border> _crosshairTiles = new();

    private void NavCrosshair_Click(object sender, RoutedEventArgs e) =>
        ShowPage(NavCrosshair, PageCrosshair, "Crosshair", "A crosshair in place of the Roblox cursor");

    /// <summary>Puts the saved crosshair onto the controls, preview and status.</summary>
    private void LoadCrosshair(AppSettings s)
    {
        _crosshairName = CrosshairGallery.ByName(s.CrosshairName).Name;

        if (CrosshairSizeSlider != null)
            CrosshairSizeSlider.Value = Math.Clamp(
                s.CrosshairSizePercent, CrosshairSizeSlider.Minimum, CrosshairSizeSlider.Maximum);

        BuildCrosshairGallery();
        HighlightCrosshairTile();

        _crosshairApplied = CursorWriter.IsApplied();
        RefreshCrosshairPreview();
        UpdateCrosshairStatus();
    }

    /// <summary>The crosshair currently chosen in the gallery.</summary>
    private CrosshairStyle CurrentCrosshairStyle() => CrosshairGallery.ByName(_crosshairName).Style;

    /// <summary>The size slider as a 0–1.6 multiplier for the renderer.</summary>
    private double SizeFactor() => (CrosshairSizeSlider?.Value ?? 100) / 100.0;

    /// <summary>Builds the gallery tiles once, each a small rendered crosshair.</summary>
    private void BuildCrosshairGallery()
    {
        if (CrosshairGalleryPanel == null || _crosshairTiles.Count > 0) return;

        Brush control = (Brush)FindResource("Control");
        Brush outline = (Brush)FindResource("Outline");
        Brush muted = (Brush)FindResource("TextMuted");

        foreach ((string name, CrosshairStyle style) in CrosshairGallery.All)
        {
            var image = new Image
            {
                Width = 46,
                Height = 46,
                Stretch = Stretch.Uniform,
                Source = CrosshairImage.RenderBitmap(style, 48, 1.15)
            };

            var label = new TextBlock
            {
                Text = name,
                FontSize = 11,
                Foreground = muted,
                Width = 96,
                Margin = new Thickness(0, 6, 0, 0),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };

            var content = new StackPanel { Margin = new Thickness(8) };
            content.Children.Add(image);
            content.Children.Add(label);

            var tile = new Border
            {
                Tag = name,
                Child = content,
                Width = 112,
                Margin = new Thickness(0, 0, 10, 10),
                CornerRadius = new CornerRadius(6),
                Background = control,
                BorderThickness = new Thickness(1),
                BorderBrush = outline,
                Cursor = Cursors.Hand
            };
            tile.MouseLeftButtonUp += CrosshairTile_Click;

            _crosshairTiles.Add(tile);
            CrosshairGalleryPanel.Children.Add(tile);
        }
    }

    private void CrosshairTile_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b || b.Tag is not string name) return;

        _crosshairName = name;
        HighlightCrosshairTile();
        RefreshCrosshairPreview();
        SaveAppSettings();
    }

    /// <summary>Rings the chosen tile with the accent colour.</summary>
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
        RefreshCrosshairPreview();
        SaveAppSettings();
    }

    /// <summary>Draws the chosen crosshair, at the chosen size, into the preview.</summary>
    private void RefreshCrosshairPreview()
    {
        if (CrosshairPreviewImage == null) return;

        CrosshairPreviewImage.Source = CrosshairImage.RenderBitmap(CurrentCrosshairStyle(), 132, SizeFactor());

        if (CrosshairSizeValue != null)
            CrosshairSizeValue.Text = $"{(int)(CrosshairSizeSlider?.Value ?? 100)}%";
    }

    private void CrosshairApply_Click(object sender, RoutedEventArgs e)
    {
        CrosshairStyle style = CurrentCrosshairStyle();
        double factor = SizeFactor();

        CursorApplyResult result = CursorWriter.Apply(px => CrosshairImage.RenderPng(style, px, factor));

        _crosshairApplied = result.AnyWritten || CursorWriter.IsApplied();
        UpdateCrosshairStatus(result);
        SaveAppSettings();
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

    /// <summary>Sets the status line, either from a fresh apply or the current state.</summary>
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
                    ( > 0, 0) => $"Applied to {r.Written} cursor file(s) — relaunch Roblox.",
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
}
