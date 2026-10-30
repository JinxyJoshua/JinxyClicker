using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace JinxyClicker;

/// <summary>
/// The first-open intro flourish — a short branded reveal over a solid
/// backdrop that fades away to the app, in the manner of Lyre's launch
/// animation. It is purely presentational: the overlay sits on top of
/// everything (ZIndex 500) and is collapsed the moment it finishes or the
/// user clicks to skip, after which it never shows again this run.
/// </summary>
public partial class MainWindow
{
    private bool _introPlayed;

    private void Window_ContentRendered(object? sender, EventArgs e)
    {
        // ContentRendered can fire more than once over a window's life; the
        // intro is a once-per-launch thing.
        if (_introPlayed) return;
        _introPlayed = true;

        // Launched straight into the game from the Play shortcut — skip the
        // flourish so nothing sits between the click and the game opening.
        if (LaunchedWithPlay())
        {
            EndIntro();
            return;
        }

        PlayIntro();
    }

    private void PlayIntro()
    {
        if (IntroOverlay == null) return;

        var sb = new Storyboard();

        // Wordmark fades and settles in.
        Add(sb, IntroContent, "Opacity", 0, 1, 0, 360, new QuadraticEase { EasingMode = EasingMode.EaseOut });
        Add(sb, IntroScale, "ScaleX", 0.86, 1, 0, 460, new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut });
        Add(sb, IntroScale, "ScaleY", 0.86, 1, 0, 460, new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut });

        // Accent bar wipes open under it; the glow breathes up then eases back.
        Add(sb, IntroBarScale, "ScaleX", 0, 1, 120, 520, new CubicEase { EasingMode = EasingMode.EaseOut });
        Add(sb, IntroGlow, "Opacity", 0, 0.8, 140, 420, new QuadraticEase { EasingMode = EasingMode.EaseOut });
        Add(sb, IntroGlow, "Opacity", 0.8, 0.45, 560, 360, new QuadraticEase { EasingMode = EasingMode.EaseInOut });

        // Hold, then the whole overlay fades away to reveal the app.
        Add(sb, IntroOverlay, "Opacity", 1, 0, 1150, 420, new QuadraticEase { EasingMode = EasingMode.EaseIn });

        sb.Completed += (_, _) => EndIntro();
        sb.Begin(this, true);
    }

    private static void Add(Storyboard sb, DependencyObject target, string property,
                            double from, double to, double beginMs, double durMs, IEasingFunction ease)
    {
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            BeginTime = TimeSpan.FromMilliseconds(beginMs),
            Duration = TimeSpan.FromMilliseconds(durMs),
            EasingFunction = ease
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, new System.Windows.PropertyPath(property));
        sb.Children.Add(anim);
    }

    private void IntroOverlay_Skip(object sender, RoutedEventArgs e) => EndIntro();

    private void EndIntro()
    {
        if (IntroOverlay == null) return;
        IntroOverlay.Visibility = Visibility.Collapsed;
        IntroOverlay.IsHitTestVisible = false;
    }
}
