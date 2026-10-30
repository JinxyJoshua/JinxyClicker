using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace JinxyClicker;

/// <summary>
/// The little live preview under the shake sliders. It runs the exact same
/// easing the engine does, so moving the Strength / Speed / Smoothness sliders
/// shows what the real shake will look like.
/// </summary>
/// <remarks>
/// A dot, not a crosshair over a target — this shake has no target, so the
/// preview shows honest hand-like drift inside the box rather than implying it
/// aims at anyone. The timer only does work while the Clicker page is on screen.
/// </remarks>
public partial class MainWindow
{
    private DispatcherTimer? _shakePreviewTimer;
    private double _previewX, _previewY;   // current dot offset from centre (scaled px)

    // Magnifies the few-pixel ranges so the motion is visible — the same idea as
    // Lyre's "3x size" preview.
    private const double ShakePreviewZoom = 2.2;

    private void StartShakePreview()
    {
        if (_shakePreviewTimer != null) return;

        _shakePreviewTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _shakePreviewTimer.Tick += ShakePreviewTick;
        _shakePreviewTimer.Start();
    }

    private void ShakePreviewTick(object? sender, EventArgs e)
    {
        if (ShakePreviewCanvas == null || ShakePreviewDot == null) return;

        // No work unless the shake card is actually visible.
        if (PageClicker is not { Visibility: Visibility.Visible }) return;

        double w = ShakePreviewCanvas.ActualWidth;
        double h = ShakePreviewCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        // A fresh random point inside the box each tick (scaled), exactly as the
        // engine picks one, then eased toward by the smoothness factor.
        double targetX = (Random.Shared.NextDouble() * (_shakeLeft + _shakeRight) - _shakeLeft) * ShakePreviewZoom;
        double targetY = (Random.Shared.NextDouble() * (_shakeUp + _shakeDown) - _shakeUp) * ShakePreviewZoom;

        double smooth = _shakeSmoothness / 100.0;
        _previewX = ShakeMotion.Ease(_previewX, targetX, smooth);
        _previewY = ShakeMotion.Ease(_previewY, targetY, smooth);

        double r = ShakePreviewDot.Width / 2;
        double cx = w / 2, cy = h / 2;
        Canvas.SetLeft(ShakePreviewDot, Math.Clamp(cx + _previewX, r, w - r) - r);
        Canvas.SetTop(ShakePreviewDot, Math.Clamp(cy + _previewY, r, h - r) - r);

        // Sway at the real rate, so a low speed looks as choppy as it will play.
        double speed = Math.Clamp(_shakeSpeed, MinShakeSpeed, MaxShakeSpeed);
        _shakePreviewTimer!.Interval = TimeSpan.FromMilliseconds(1000.0 / speed);
    }
}
