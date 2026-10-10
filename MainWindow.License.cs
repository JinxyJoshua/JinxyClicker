using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace JinxyClicker;

/// <summary>
/// The Pro gate wired into the window. The app does not keep a live AppSettings
/// object — it loads one, applies it, and rebuilds a fresh one on save — so the
/// license key and its first-use date are held here as fields and written back by
/// <c>SaveAppSettings</c>. A missing or invalid key simply leaves <see cref="IsPro"/>
/// false; nothing here is trusted, because the key is re-verified every launch.
/// </summary>
public partial class MainWindow
{
    private License? _license;
    private string? _licenseKey;
    private DateTime? _licenseFirstUseUtc;

    /// <summary>True when a valid Pro key is active. Every Pro feature reads this.</summary>
    public bool IsPro { get; private set; }

    // Called from ApplyAppSettings with the freshly loaded settings.
    private void LoadLicense(AppSettings s)
    {
        _license = new License(LicenseKeys.PublicKey(), LicenseKeys.Blocklist, () => DateTime.UtcNow);
        _licenseKey = s.LicenseKey;
        _licenseFirstUseUtc = s.LicenseFirstUseUtc;
        RefreshLicense();
    }

    // Shows the Pro page and refreshes its status, in case a pass expired since
    // the page was last seen.
    private void NavPro_Click(object sender, RoutedEventArgs e)
    {
        RefreshLicense();
        ShowPage(NavProCorner, PagePro, "Pro", "Unlock the paid features with a key");
    }

    // Evaluates the stored key, updates IsPro and the status line. The first time a
    // timed pass is seen it stamps the first-use date so the countdown starts now;
    // that stamp is persisted by the next SaveAppSettings (always after activation).
    private void RefreshLicense()
    {
        if (_license is null) return;

        LicenseStatus status = _license.Evaluate(_licenseKey, _licenseFirstUseUtc);
        if (status.NeedsFirstUseStamp)
            _licenseFirstUseUtc = DateTime.UtcNow;

        IsPro = status.IsPro;

        // The bottom-left shortcut reflects the plan: a nudge when free, a badge
        // when active.
        if (NavProCorner is not null)
            NavProCorner.Content = IsPro ? "★ PRO" : "★ GET PRO";

        if (ProStatusText is null) return; // page not built yet
        ProStatusText.Text = status switch
        {
            { IsPro: true, Tier: ProTier.Lifetime } => "Pro — lifetime",
            { IsPro: true, ExpiresUtc: { } exp } => $"Pro — {status.Tier}, until {exp.ToLocalTime():d}",
            _ => "Free version"
        };
    }

    // One reliable click: read the whole key straight from the clipboard (so a
    // half-finished paste in the box can't get in the way), show it, and activate.
    private void ProPaste_Click(object sender, RoutedEventArgs e)
    {
        string clip = ReadClipboard();
        if (clip.Length > 0) ProKeyInput.Text = clip;
        TryActivate(ProKeyInput.Text ?? "");
    }

    private void ProActivate_Click(object sender, RoutedEventArgs e)
    {
        if (_license is null) return;

        string typed = ProKeyInput.Text ?? "";

        // If the box doesn't hold a valid key, fall back to the clipboard — the box
        // may have only caught part of a long key. This rescues a bad paste.
        if (_license.PrepareActivation(_licenseKey, _licenseFirstUseUtc, typed) is null)
        {
            string clip = ReadClipboard();
            if (clip.Length > 0 &&
                _license.PrepareActivation(_licenseKey, _licenseFirstUseUtc, clip) is not null)
            {
                ProKeyInput.Text = clip;
                typed = clip;
            }
        }

        TryActivate(typed);
    }

    // Verifies and applies a key. PrepareActivation decides the first-use date:
    // a new key starts a fresh clock, the SAME key re-pasted keeps its clock, so an
    // expired pass cannot be renewed just by pasting it again.
    private void TryActivate(string key)
    {
        if (_license is null) return;

        var prep = _license.PrepareActivation(_licenseKey, _licenseFirstUseUtc, key);
        if (prep is null)
        {
            ProActivateResult.Text = "That key isn't valid. Copy the whole key, then click \"Paste & activate\".";
            return;
        }

        _licenseKey = prep.Value.Key;
        _licenseFirstUseUtc = prep.Value.FirstUseUtc;
        RefreshLicense();   // stamps a fresh pass if needed
        SaveAppSettings();  // persists the key and first-use date

        if (IsPro)
        {
            ProActivateResult.Text = "Activated. Thank you!";
            PlayCelebration();
        }
        else
        {
            ProActivateResult.Text = "That key has expired.";
        }
    }

    // A confetti burst and a pop on the plan line when a key activates. Purely
    // cosmetic, click-through, and cleans itself up after a few seconds.
    private void PlayCelebration()
    {
        if (ProStatusText is not null)
        {
            var scale = new ScaleTransform(1, 1);
            ProStatusText.RenderTransformOrigin = new Point(0, 0.5);
            ProStatusText.RenderTransform = scale;
            var pop = new DoubleAnimation(0.75, 1, TimeSpan.FromMilliseconds(550))
            {
                EasingFunction = new BackEase { Amplitude = 0.7, EasingMode = EasingMode.EaseOut }
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }

        Canvas? canvas = ProCelebration;
        if (canvas is null) return;
        canvas.Children.Clear();

        double w = canvas.ActualWidth > 0 ? canvas.ActualWidth : 820;
        double h = canvas.ActualHeight > 0 ? canvas.ActualHeight : 620;
        var rnd = new Random();
        Brush[] colors =
            new[] { "#FF5C8A", "#FFD24C", "#4CE0B3", "#5CA8FF", "#B47CFF", "#FF8A4C" }
            .Select(c => (Brush)new BrushConverter().ConvertFrom(c)!)
            .ToArray();

        for (int i = 0; i < 70; i++)
        {
            double dur = 1.4 + rnd.NextDouble() * 1.5;
            double startX = rnd.NextDouble() * w;
            double startY = -20 - rnd.NextDouble() * 120;

            var rot = new RotateTransform(rnd.Next(360));
            var piece = new Rectangle
            {
                Width = rnd.Next(6, 13),
                Height = rnd.Next(9, 18),
                Fill = colors[rnd.Next(colors.Length)],
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = rot
            };
            Canvas.SetLeft(piece, startX);
            Canvas.SetTop(piece, startY);
            canvas.Children.Add(piece);

            piece.BeginAnimation(Canvas.TopProperty,
                new DoubleAnimation(startY, h + 40, TimeSpan.FromSeconds(dur))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
            piece.BeginAnimation(Canvas.LeftProperty,
                new DoubleAnimation(startX, startX + (rnd.NextDouble() * 180 - 90), TimeSpan.FromSeconds(dur)));
            rot.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(rot.Angle, rot.Angle + (rnd.Next(2) == 0 ? 720 : -720), TimeSpan.FromSeconds(dur)));
            piece.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1, 0, TimeSpan.FromSeconds(dur))
                { BeginTime = TimeSpan.FromSeconds(dur * 0.6) });
        }

        var clear = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.2) };
        clear.Tick += (_, _) => { clear.Stop(); canvas.Children.Clear(); };
        clear.Start();
    }

    private static string ReadClipboard()
    {
        try
        {
            return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : "";
        }
        catch
        {
            return ""; // clipboard momentarily locked by another app
        }
    }
}
