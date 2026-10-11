using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace JinxyClicker;

/// <summary>
/// The aim trainer (Plus): practise reaction, flick accuracy, tracking and clicking
/// entirely inside the app. It drives a Canvas of targets and never touches Roblox.
/// </summary>
public partial class MainWindow
{
    private string _trainerMode = "Accuracy";
    private int _trainerDurationSec = 30;

    private readonly DispatcherTimer _trainerClock = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer _reactionDelay = new();
    private readonly Random _trainerRng = new();

    private bool _trainerRunning;
    private int _trainerRemainingMs;
    private int _trainerHits, _trainerMisses, _trainerClicks;
    private long _reactionSumMs;
    private int _reactionCount;
    private long _targetShownTick;
    private int _trackOn, _trackTotal;
    private bool _reactionReady;
    private Ellipse? _target;
    private double _targetRadius = 22, _targetVx = 3.2, _targetVy = 2.6;

    private void EnsureTrainerWired()
    {
        // Hooked up once; safe to call repeatedly.
        _trainerClock.Tick -= TrainerTick; _trainerClock.Tick += TrainerTick;
        _reactionDelay.Tick -= ReactionDelayTick; _reactionDelay.Tick += ReactionDelayTick;
    }

    private void NavTrainer_Click(object sender, RoutedEventArgs e)
    {
        if (!IsPro) { NavPro_Click(sender, e); return; }   // gated: send to the key page
        EnsureTrainerWired();
        ShowPage(NavTrainer, PageTrainer, "Aim Trainer", "Practise your aim and clicking");
    }

    // The tab stays visible so Free users see it, but clicking it (or a pass
    // expiring here) sends them to the key page. Called from RefreshLicense.
    private void ApplyTrainerPro()
    {
        if (!IsPro && PageTrainer is { Visibility: Visibility.Visible })
        {
            StopTrainer();
            ShowPage(NavProCorner, PagePro, "Plus", "Unlock the paid features with a key");
        }
    }

    private void TrainerMode_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag }) _trainerMode = tag;
        StopTrainer();
    }

    private void TrainerDur_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && int.TryParse(tag, out int d)) _trainerDurationSec = d;
    }

    private void TrainerStart_Click(object sender, RoutedEventArgs e)
    {
        StopTrainer();

        _trainerRunning = true;
        _trainerRemainingMs = _trainerDurationSec * 1000;
        _trainerHits = _trainerMisses = _trainerClicks = 0;
        _reactionSumMs = 0; _reactionCount = 0; _trackOn = 0; _trackTotal = 0;
        _reactionReady = false;
        if (TrainerCanvas is not null) TrainerCanvas.Children.Clear();
        _target = null;
        UpdateTrainerStats();

        switch (_trainerMode)
        {
            case "Accuracy": SpawnTarget(22); break;
            case "Reaction": ArmReaction(); break;
            case "Tracking": SpawnTarget(28); _targetVx = 3.2; _targetVy = 2.6; break;
            // CPS: no target, just click anywhere.
        }

        _trainerClock.Start();
    }

    private void StopTrainer()
    {
        _trainerRunning = false;
        _trainerClock.Stop();
        _reactionDelay.Stop();
    }

    private void TrainerTick(object? sender, EventArgs e)
    {
        if (!_trainerRunning) return;

        // Leaving the page ends the run so timers don't tick on a hidden canvas.
        if (PageTrainer is not { Visibility: Visibility.Visible }) { StopTrainer(); return; }

        _trainerRemainingMs -= 50;
        if (_trainerRemainingMs <= 0) { EndTrainer(); return; }

        if (_trainerMode == "Tracking" && _target is not null)
        {
            MoveTarget();
            SampleTracking();
        }

        UpdateTrainerStats();
    }

    private void SpawnTarget(double radius)
    {
        if (TrainerCanvas is null) return;
        TrainerCanvas.Children.Clear();
        _targetRadius = radius;

        double w = Math.Max(2 * radius + 10, TrainerCanvas.ActualWidth);
        double h = Math.Max(2 * radius + 10, TrainerCanvas.ActualHeight);
        double x = _trainerRng.NextDouble() * (w - 2 * radius);
        double y = _trainerRng.NextDouble() * (h - 2 * radius);

        var dot = new Ellipse
        {
            Width = 2 * radius,
            Height = 2 * radius,
            Fill = (Brush)FindResource("Accent"),
            Stroke = Brushes.White,
            StrokeThickness = 2,
            Cursor = Cursors.Cross,
        };
        dot.MouseLeftButtonDown += TrainerTarget_Click;
        Canvas.SetLeft(dot, x);
        Canvas.SetTop(dot, y);
        TrainerCanvas.Children.Add(dot);
        _target = dot;
        _targetShownTick = Environment.TickCount64;
    }

    private void MoveTarget()
    {
        if (_target is null || TrainerCanvas is null) return;
        double x = Canvas.GetLeft(_target) + _targetVx;
        double y = Canvas.GetTop(_target) + _targetVy;
        double maxX = Math.Max(0, TrainerCanvas.ActualWidth - 2 * _targetRadius);
        double maxY = Math.Max(0, TrainerCanvas.ActualHeight - 2 * _targetRadius);
        if (x <= 0 || x >= maxX) { _targetVx = -_targetVx; x = Math.Clamp(x, 0, maxX); }
        if (y <= 0 || y >= maxY) { _targetVy = -_targetVy; y = Math.Clamp(y, 0, maxY); }
        Canvas.SetLeft(_target, x);
        Canvas.SetTop(_target, y);
    }

    private void SampleTracking()
    {
        if (_target is null || TrainerCanvas is null) return;
        Point p = Mouse.GetPosition(TrainerCanvas);
        double cx = Canvas.GetLeft(_target) + _targetRadius;
        double cy = Canvas.GetTop(_target) + _targetRadius;
        _trackTotal++;
        double dx = p.X - cx, dy = p.Y - cy;
        if (dx * dx + dy * dy <= _targetRadius * _targetRadius) _trackOn++;
    }

    private void ArmReaction()
    {
        if (TrainerCanvas is not null) TrainerCanvas.Children.Clear();
        _target = null;
        _reactionReady = false;
        _reactionDelay.Interval = TimeSpan.FromMilliseconds(400 + _trainerRng.Next(1400));
        _reactionDelay.Stop();
        _reactionDelay.Start();
    }

    private void ReactionDelayTick(object? sender, EventArgs e)
    {
        _reactionDelay.Stop();
        if (!_trainerRunning) return;
        SpawnTarget(26);
        _reactionReady = true;
    }

    private void TrainerTarget_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;   // a hit on the target is never a miss
        if (!_trainerRunning) return;

        switch (_trainerMode)
        {
            case "Accuracy":
                _trainerHits++;
                SpawnTarget(22);
                break;
            case "Reaction":
                if (_reactionReady)
                {
                    _reactionSumMs += Environment.TickCount64 - _targetShownTick;
                    _reactionCount++;
                    _trainerHits++;
                    ArmReaction();
                }
                break;
            case "Tracking":
                break;   // tracking is scored by time-on-target, not clicks
            case "CPS":
                _trainerClicks++;
                break;
        }
        UpdateTrainerStats();
    }

    private void TrainerCanvas_Click(object sender, MouseButtonEventArgs e)
    {
        if (!_trainerRunning) return;

        if (_trainerMode == "CPS") { _trainerClicks++; }
        else if (_trainerMode is "Accuracy" or "Reaction") { _trainerMisses++; }
        UpdateTrainerStats();
    }

    private void UpdateTrainerStats()
    {
        if (TrainerHits is not null) TrainerHits.Text = _trainerMode == "CPS" ? _trainerClicks.ToString() : _trainerHits.ToString();
        if (TrainerMisses is not null) TrainerMisses.Text = _trainerMisses.ToString();

        if (TrainerAccuracy is not null)
        {
            string acc = "—";
            if (_trainerMode == "Tracking" && _trackTotal > 0)
                acc = (100.0 * _trackOn / _trackTotal).ToString("0") + "%";
            else if (_trainerMode is "Accuracy" or "Reaction")
            {
                int shots = _trainerHits + _trainerMisses;
                if (shots > 0) acc = (100.0 * _trainerHits / shots).ToString("0") + "%";
            }
            TrainerAccuracy.Text = acc;
        }

        if (TrainerAvg is not null)
        {
            string avg = "—";
            if (_trainerMode == "Reaction" && _reactionCount > 0)
                avg = (_reactionSumMs / _reactionCount) + "ms";
            else if (_trainerMode == "CPS")
            {
                double elapsed = Math.Max(0.001, (_trainerDurationSec * 1000 - _trainerRemainingMs) / 1000.0);
                avg = (_trainerClicks / elapsed).ToString("0.0", CultureInfo.InvariantCulture) + " cps";
            }
            TrainerAvg.Text = avg;
        }

        if (TrainerTimeLeft is not null)
            TrainerTimeLeft.Text = _trainerRunning ? Math.Max(0, (_trainerRemainingMs + 999) / 1000) + "s" : "—";
    }

    private void EndTrainer()
    {
        StopTrainer();
        _trainerRemainingMs = 0;
        if (TrainerCanvas is not null) TrainerCanvas.Children.Clear();
        _target = null;
        UpdateTrainerStats();
        if (TrainerTimeLeft is not null) TrainerTimeLeft.Text = "done";
    }
}
