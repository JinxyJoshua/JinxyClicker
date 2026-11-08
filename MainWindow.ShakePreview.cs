using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace JinxyClicker;

/// <summary>
/// The Shaky Tracking page (its own tab under Clicker) and its live preview.
/// </summary>
/// <remarks>
/// The preview runs the exact same easing the engine does, so moving Range /
/// Speed / Smoothness shows what the real shake will look like. It draws the
/// range as a dashed box, the resting point as a faint cross, and the path as a
/// fading trail — a dot drifting inside a box, never a crosshair over a target,
/// because this shake has no target and does no aiming.
/// </remarks>
public partial class MainWindow
{
    private DispatcherTimer? _shakePreviewTimer;
    private double _previewX, _previewY;   // current dot offset from centre (scaled px)

    private readonly Queue<Point> _previewTrail = new();
    private const int PreviewTrailMax = 48;

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

        // No work unless the Shaky Tracking sub-tab is actually on screen (the
        // Clicker page shown and that sub-tab selected); drop the trail so it
        // starts clean next time it opens. IsVisible accounts for the parent page.
        if (PageClk_shake is not { IsVisible: true })
        {
            if (_previewTrail.Count > 0)
            {
                _previewTrail.Clear();
                if (ShakeTrail != null) ShakeTrail.Points = new PointCollection();
            }
            return;
        }

        double w = ShakePreviewCanvas.ActualWidth;
        double h = ShakePreviewCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        double cx = w / 2, cy = h / 2;

        // Resting-point cross.
        const double arm = 7;
        if (ShakeOriginH != null) { ShakeOriginH.X1 = cx - arm; ShakeOriginH.Y1 = cy; ShakeOriginH.X2 = cx + arm; ShakeOriginH.Y2 = cy; }
        if (ShakeOriginV != null) { ShakeOriginV.X1 = cx; ShakeOriginV.Y1 = cy - arm; ShakeOriginV.X2 = cx; ShakeOriginV.Y2 = cy + arm; }

        // Range box, scaled and clamped to the canvas.
        if (ShakeBoundsBox != null)
        {
            double bw = Math.Max(2, (_shakeLeft + _shakeRight) * ShakePreviewZoom);
            double bh = Math.Max(2, (_shakeUp + _shakeDown) * ShakePreviewZoom);
            double bx = cx - _shakeLeft * ShakePreviewZoom;
            double by = cy - _shakeUp * ShakePreviewZoom;
            ShakeBoundsBox.Width = bw;
            ShakeBoundsBox.Height = bh;
            Canvas.SetLeft(ShakeBoundsBox, bx);
            Canvas.SetTop(ShakeBoundsBox, by);
        }

        // A fresh random point inside the box each tick (scaled), exactly as the
        // engine picks one, then eased toward by the smoothness factor.
        double targetX = (Random.Shared.NextDouble() * (_shakeLeft + _shakeRight) - _shakeLeft) * ShakePreviewZoom;
        double targetY = (Random.Shared.NextDouble() * (_shakeUp + _shakeDown) - _shakeUp) * ShakePreviewZoom;

        double smooth = _shakeSmoothness / 100.0;
        _previewX = ShakeMotion.Ease(_previewX, targetX, smooth);
        _previewY = ShakeMotion.Ease(_previewY, targetY, smooth);

        double r = ShakePreviewDot.Width / 2;
        double dotCx = Math.Clamp(cx + _previewX, r, w - r);
        double dotCy = Math.Clamp(cy + _previewY, r, h - r);
        Canvas.SetLeft(ShakePreviewDot, dotCx - r);
        Canvas.SetTop(ShakePreviewDot, dotCy - r);

        // The path it took, as a fading trail.
        if (ShakeTrail != null)
        {
            _previewTrail.Enqueue(new Point(dotCx, dotCy));
            while (_previewTrail.Count > PreviewTrailMax) _previewTrail.Dequeue();
            ShakeTrail.Points = new PointCollection(_previewTrail);
        }

        // Sway at the real rate, so a low speed looks as choppy as it will play.
        double speed = Math.Clamp(_shakeSpeed, MinShakeSpeed, MaxShakeSpeed);
        _shakePreviewTimer!.Interval = TimeSpan.FromMilliseconds(1000.0 / speed);
    }
}
