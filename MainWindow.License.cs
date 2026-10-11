using System;
using System.Linq;
using System.Threading.Tasks;
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
    private PassServer? _passServer;
    private string? _licenseKey;
    private DateTime? _licenseFirstUseUtc;

    /// <summary>True when a valid Pro key is active. Every Pro feature reads this.</summary>
    public bool IsPro { get; private set; }

    // Called from ApplyAppSettings with the freshly loaded settings.
    private void LoadLicense(AppSettings s)
    {
        _license = new License(LicenseKeys.PublicKey(), LicenseKeys.Blocklist, () => DateTime.UtcNow);
        _passServer = string.IsNullOrEmpty(LicenseKeys.PassServerUrl)
            ? null : new PassServer(LicenseKeys.PassServerUrl);
        _licenseKey = s.LicenseKey;
        _licenseFirstUseUtc = s.LicenseFirstUseUtc;
        RefreshLicense();
        SyncPassWithServerAsync();   // fire-and-forget: the server's date wins once online

        // A timed pass expires against the clock, but the app can sit in the tray
        // for days — re-evaluate every minute so Pro turns off when the pass runs
        // out, not just at the next restart or Pro-page visit.
        if (_licenseRecheck is null)
        {
            _licenseRecheck = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(1)
            };
            _licenseRecheck.Tick += (_, _) => RefreshLicense();
            _licenseRecheck.Start();
        }
    }

    private System.Windows.Threading.DispatcherTimer? _licenseRecheck;

    // For an active pass, pull the authoritative first-use date from the server and
    // let it win over the local one — so deleting local files or reinstalling can't
    // restart a pass. Does nothing for lifetime keys or when offline.
    private async void SyncPassWithServerAsync()
    {
        if (_license is null || _passServer is null || string.IsNullOrEmpty(_licenseKey)) return;

        ProTier? tier = _license.TierOf(_licenseKey);
        if (tier is null || tier == ProTier.Lifetime) return;

        DateTime? serverFirst = await _passServer.FirstUseUtcAsync(_licenseKey!);
        if (serverFirst is null) return; // offline: keep the local date

        if (serverFirst != _licenseFirstUseUtc)
        {
            _licenseFirstUseUtc = serverFirst;
            SaveAppSettings();
            RefreshLicense();
        }
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

        // Per-page Pro gating that should react the moment a key activates — or a
        // pass expires — even if the user is sitting on that page (null-guarded
        // for early calls).
        ApplyCrosshairPro();
        ApplySetupsPro();
        ApplyClipStudioPro();
        ApplySocdPro();

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

        // Once Pro is active there's nothing to activate, so lock the controls.
        // They unlock again on their own if a pass later expires (IsPro goes false).
        bool canActivate = !IsPro;
        if (ProKeyInput is not null) ProKeyInput.IsEnabled = canActivate;
        if (ProPasteButton is not null) ProPasteButton.IsEnabled = canActivate;
        if (ProActivateButton is not null) ProActivateButton.IsEnabled = canActivate;
        if (ProActivateResult is not null && IsPro)
            ProActivateResult.Text = "You're already Pro — no need to activate again.";
    }

    // One reliable click: read the whole key straight from the clipboard (so a
    // half-finished paste in the box can't get in the way), show it, and activate.
    private async void ProPaste_Click(object sender, RoutedEventArgs e)
    {
        if (IsPro) return; // already active; nothing to re-activate
        string clip = ReadClipboard();
        if (clip.Length > 0) ProKeyInput.Text = clip;
        await TryActivateAsync(ProKeyInput.Text ?? "");
    }

    private async void ProActivate_Click(object sender, RoutedEventArgs e)
    {
        if (_license is null || IsPro) return; // already active; nothing to re-activate

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

        await TryActivateAsync(typed);
    }

    // Verifies and applies a key. PrepareActivation decides the first-use date:
    // a new key starts a fresh clock, the SAME key re-pasted keeps its clock. For a
    // pass, the server then owns the first-use date so it can't be reset by wiping
    // local files; lifetime keys skip the server and stay offline.
    private async Task TryActivateAsync(string key)
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

        ProTier? tier = _license.TierOf(_licenseKey);
        if (_passServer is not null && tier is not null && tier != ProTier.Lifetime)
        {
            ProActivateResult.Text = "Checking…";
            DateTime? serverFirst = await _passServer.FirstUseUtcAsync(_licenseKey!);
            if (serverFirst is not null) _licenseFirstUseUtc = serverFirst;
            // null = offline: keep the local date so the pass still activates
        }

        RefreshLicense();   // stamps a fresh pass if needed
        SaveAppSettings();  // persists the key and first-use date

        if (IsPro)
        {
            ProActivateResult.Text = "Activated. Thank you!";
            PlayCelebration();
        }
        else
        {
            // A valid-but-inactive key is either past its expiry or revoked; the
            // status doesn't say which, so the message covers both honestly.
            ProActivateResult.Text = "That key is expired or has been revoked.";
        }
    }

    // A confetti burst and a pop on the plan line when a key activates. Purely
    // cosmetic, click-through, and cleans itself up after a few seconds.
    private void PlayCelebration()
    {
        try { PlayCelebrationCore(); } catch { /* cosmetic only; never break activation */ }
    }

    private void PlayCelebrationCore()
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
