using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace JinxyClicker;

/// <summary>
/// Daily Rewards: a once-a-day spin (twice for Plus) on a 12-slice wheel, with rarity
/// odds and a Legendary pity timer from <see cref="RewardWheel"/>, a daily streak, a
/// Stardust balance and a cosmetics collection. Stage 2 — the wheel, the spin and the
/// bookkeeping; actually equipping won cosmetics comes in a later pass.
/// </summary>
public partial class MainWindow
{
    // The 12 wheel slices. Every rarity has at least one, so a roll never falls back.
    private static readonly List<Reward> WheelCatalog = new()
    {
        new Reward("sd15",  "15 Stardust",       Rarity.Common,    RewardKind.Stardust, 15),
        new Reward("mintp", "Mint Pop",          Rarity.Common,    RewardKind.Cursor),
        new Reward("sd30",  "30 Stardust",       Rarity.Common,    RewardKind.Stardust, 30),
        new Reward("frost", "Frostbite",         Rarity.Rare,      RewardKind.Cursor),
        new Reward("sm14",  "Smooth 14",         Rarity.Rare,      RewardKind.Crosshair),
        new Reward("sakura","Sakura",            Rarity.Rare,      RewardKind.Cursor),
        new Reward("sd75",  "75 Stardust",       Rarity.Epic,      RewardKind.Stardust, 75),
        new Reward("neon",  "Neon Horizon",      Rarity.Epic,      RewardKind.Wallpaper),
        new Reward("midn",  "Midnight Contours", Rarity.Epic,      RewardKind.Wallpaper),
        new Reward("galaxy","Galaxy",            Rarity.Legendary, RewardKind.Cursor),
        new Reward("gold",  "Golden Cursor",     Rarity.Legendary, RewardKind.Cursor),
        new Reward("prem1", "1 hour Premium",    Rarity.Mythic,    RewardKind.Premium, 1),
    };

    private int _stardust;
    private int _streak;
    private string _lastSpinDate = "";
    private int _spinsSinceLegendary;
    private int _spinsToday;
    private readonly HashSet<string> _owned = new();
    private readonly Random _wheelRng = new();
    private bool _spinning;

    private static string Today => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private int SpinsPerDay => IsPro ? 2 : 1;
    private int SpinsLeft => Math.Max(0, SpinsPerDay - (_lastSpinDate == Today ? _spinsToday : 0));

    private static Color RarityColor(Rarity r) => r switch
    {
        Rarity.Common => Color.FromRgb(0x8A, 0x93, 0xA8),
        Rarity.Rare => Color.FromRgb(0x45, 0xA3, 0xFF),
        Rarity.Epic => Color.FromRgb(0xA7, 0x8B, 0xFA),
        Rarity.Legendary => Color.FromRgb(0xFF, 0xC5, 0x3D),
        Rarity.Mythic => Color.FromRgb(0xFF, 0x5B, 0xC8),
        _ => Colors.Gray
    };

    // ---- settings load/save ----

    private void LoadRewards(AppSettings s)
    {
        _stardust = Math.Max(0, s.Stardust);
        _streak = Math.Max(0, s.RewardStreak);
        _lastSpinDate = s.RewardLastSpin ?? "";
        _spinsSinceLegendary = Math.Max(0, s.RewardPity);
        _spinsToday = s.RewardLastSpin == Today ? Math.Max(0, s.RewardSpinsToday) : 0;
        _owned.Clear();
        if (!string.IsNullOrWhiteSpace(s.RewardOwned))
            foreach (string id in s.RewardOwned.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                _owned.Add(id);
    }

    // ---- page ----

    private void NavRewards_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(NavRewards, PageRewards, "Daily Rewards", "Spin the wheel for cosmetics and Stardust");
        BuildWheel();
        BuildOddsPanel();
        RefreshRewardsHeader();
        BuildCollection();
    }

    private void RefreshRewardsHeader()
    {
        if (RewardStardustText != null) RewardStardustText.Text = _stardust.ToString();
        if (RewardStreakText != null) RewardStreakText.Text = _streak + (_streak == 1 ? " day" : " days");
        if (RewardSpinsText != null)
            RewardSpinsText.Text = SpinsLeft > 0
                ? (SpinsLeft == 1 ? "1 spin ready" : SpinsLeft + " spins ready")
                : "Come back tomorrow";
        if (SpinButton != null) SpinButton.IsEnabled = SpinsLeft > 0 && !_spinning;
        if (RewardPityText != null) RewardPityText.Text = $"Legendary guaranteed within {RewardWheel.PityThreshold} spins";
        if (RewardPityBar != null)
            RewardPityBar.Width = Math.Min(1.0, _spinsSinceLegendary / (double)RewardWheel.PityThreshold) * 260;
        if (RewardCollectedText != null)
        {
            int total = WheelCatalog.FindAll(r => r.Collectible).Count;
            int have = _owned.Count;
            RewardCollectedText.Text = $"{have} / {total} collected";
        }
    }

    // ---- the wheel ----

    private const double WheelR = 168;
    private const double WheelCx = 175, WheelCy = 175;

    private void BuildWheel()
    {
        if (WheelSectors == null) return;
        WheelSectors.Children.Clear();

        int n = WheelCatalog.Count;
        double step = 360.0 / n;
        for (int i = 0; i < n; i++)
        {
            Reward reward = WheelCatalog[i];
            double a0 = i * step, a1 = (i + 1) * step;
            WheelSectors.Children.Add(Sector(a0, a1, RarityColor(reward.Rarity), i % 2 == 0 ? 0.92 : 0.78));
            WheelSectors.Children.Add(SliceLabel(reward, (a0 + a1) / 2));
        }
    }

    private static Point OnCircle(double deg, double radius)
    {
        double t = deg * Math.PI / 180.0;   // clockwise from the top
        return new Point(WheelCx + radius * Math.Sin(t), WheelCy - radius * Math.Cos(t));
    }

    private static Path Sector(double a0, double a1, Color color, double shade)
    {
        Point p0 = OnCircle(a0, WheelR), p1 = OnCircle(a1, WheelR);
        var fig = new PathFigure { StartPoint = new Point(WheelCx, WheelCy), IsClosed = true };
        fig.Segments.Add(new LineSegment(p0, true));
        fig.Segments.Add(new ArcSegment(p1, new Size(WheelR, WheelR), 0, false, SweepDirection.Clockwise, true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        return new Path
        {
            Data = geo,
            Fill = new SolidColorBrush(Color.FromRgb(
                (byte)(color.R * shade), (byte)(color.G * shade), (byte)(color.B * shade))),
            Stroke = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1
        };
    }

    private static TextBlock SliceLabel(Reward reward, double midDeg)
    {
        Point at = OnCircle(midDeg, WheelR * 0.60);
        double rot = midDeg;
        bool flip = midDeg > 90 && midDeg < 270;   // keep text upright on the lower half
        if (flip) rot += 180;

        var tb = new TextBlock
        {
            Text = reward.Name,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
            Width = 96,
            TextWrapping = TextWrapping.Wrap,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(rot - 90)   // read outward along the spoke
        };
        Canvas.SetLeft(tb, at.X - 48);
        Canvas.SetTop(tb, at.Y - 12);
        return tb;
    }

    // ---- spin ----

    private void Spin_Click(object sender, RoutedEventArgs e)
    {
        if (_spinning || SpinsLeft <= 0 || WheelRotate == null) return;
        _spinning = true;
        if (SpinButton != null) SpinButton.IsEnabled = false;

        Reward reward = RewardWheel.Roll(WheelCatalog, _spinsSinceLegendary, _wheelRng);
        int index = WheelCatalog.IndexOf(reward);

        // Land the chosen slice under the top pointer, after several full turns.
        double step = 360.0 / WheelCatalog.Count;
        double sliceCentre = index * step + step / 2;
        double jitter = (_wheelRng.NextDouble() - 0.5) * (step * 0.6);
        double current = WheelRotate.Angle % 360;
        double target = 360 * 6 + (360 - sliceCentre - jitter);
        double to = current + ((target - current) % 360 + 360) % 360 + 360 * 5;

        var anim = new DoubleAnimation(WheelRotate.Angle, to, TimeSpan.FromSeconds(4))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        anim.Completed += (_, _) => OnSpinLanded(reward);
        WheelRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
    }

    private void OnSpinLanded(Reward reward)
    {
        // Streak: +1 on the first spin of a new day; reset if a day was skipped.
        if (_lastSpinDate != Today)
        {
            string yesterday = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            _streak = _lastSpinDate == yesterday ? _streak + 1 : 1;
            _spinsToday = 0;
            _lastSpinDate = Today;
        }
        _spinsToday++;

        _spinsSinceLegendary = RewardWheel.ResetsPity(reward.Rarity) ? 0 : _spinsSinceLegendary + 1;
        GrantReward(reward);

        _spinning = false;
        _settingsDirty = true;
        SaveAppSettings();

        ShowRewardResult(reward);
        RefreshRewardsHeader();
        BuildCollection();
    }

    private void GrantReward(Reward reward)
    {
        switch (reward.Kind)
        {
            case RewardKind.Stardust: _stardust += reward.Value; break;
            case RewardKind.Premium: /* Stage 3: grant temporary Plus */ break;
            default: _owned.Add(reward.Id); break;   // cosmetics
        }
    }

    private void ShowRewardResult(Reward reward)
    {
        if (RewardResultCard == null || RewardResultName == null || RewardResultRarity == null) return;
        RewardResultName.Text = reward.Kind == RewardKind.Stardust ? $"+{reward.Value} Stardust" : reward.Name;
        RewardResultRarity.Text = reward.Rarity.ToString().ToUpperInvariant();
        RewardResultRarity.Foreground = new SolidColorBrush(RarityColor(reward.Rarity));
        RewardResultCard.BorderBrush = new SolidColorBrush(RarityColor(reward.Rarity));
        RewardResultCard.Visibility = Visibility.Visible;
    }

    // ---- odds + collection ----

    private void BuildOddsPanel()
    {
        if (OddsPanel == null) return;
        OddsPanel.Children.Clear();
        foreach (Rarity r in new[] { Rarity.Mythic, Rarity.Legendary, Rarity.Epic, Rarity.Rare, Rarity.Common })
        {
            var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 6) };
            var dot = new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(RarityColor(r)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(dot, Dock.Left);
            var pct = new TextBlock { Text = RewardWheel.Odds[r].ToString("0.#", CultureInfo.InvariantCulture) + "%", Foreground = Brush("TextBright"), FontWeight = FontWeights.SemiBold, FontSize = 12 };
            DockPanel.SetDock(pct, Dock.Right);
            var name = new TextBlock { Text = r.ToString(), Foreground = Brush("TextSoft"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(dot);
            row.Children.Add(pct);
            row.Children.Add(name);
            OddsPanel.Children.Add(row);
        }
    }

    private void BuildCollection()
    {
        if (CollectionPanel == null) return;
        CollectionPanel.Children.Clear();
        foreach (Reward reward in WheelCatalog)
        {
            if (!reward.Collectible) continue;
            bool have = _owned.Contains(reward.Id);
            Color rc = RarityColor(reward.Rarity);

            var chip = new Border
            {
                Width = 150, Height = 58, CornerRadius = new CornerRadius(8),
                Background = Brush("Panel2"), Margin = new Thickness(0, 0, 10, 10),
                BorderBrush = new SolidColorBrush(have ? rc : Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(have ? 2 : 1),
                Padding = new Thickness(10, 8, 10, 8),
                Opacity = have ? 1.0 : 0.45
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = have ? reward.Name : "Locked", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brush("TextBright") });
            sp.Children.Add(new TextBlock { Text = reward.Rarity.ToString(), FontSize = 10, Foreground = new SolidColorBrush(rc), Margin = new Thickness(0, 2, 0, 0) });
            chip.Child = sp;
            CollectionPanel.Children.Add(chip);
        }
    }
}
