using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace JinxyClicker;

/// <summary>
/// Daily Rewards: a once-a-day spin (twice for Plus) on a 12-slice wheel, with rarity
/// odds and a Legendary pity timer from <see cref="RewardWheel"/>, a daily streak, a
/// Stardust balance and a cosmetics collection. Equipping a won cosmetic applies the
/// real thing — a cursor cosmetic writes its crosshair into Roblox (via
/// <see cref="CursorWriter"/>), a wallpaper cosmetic installs as the app background —
/// mapped from id to style/design by <see cref="RewardCosmetics"/>.
/// </summary>
public partial class MainWindow
{
    // The wheel slices. Every rarity has at least one, so a roll never falls back.
    // Premium rewards sit in Legendary/Mythic on purpose, so they're hard to land:
    // 1 hour Premium is one of two Mythic slices (~0.75% a spin).
    private static readonly List<Reward> WheelCatalog = new()
    {
        new Reward("sd15",  "15 Shards",         Rarity.Common,    RewardKind.Stardust, 15),
        new Reward("mintp", "Mint Pop",          Rarity.Common,    RewardKind.Cursor),
        new Reward("sd30",  "30 Shards",         Rarity.Common,    RewardKind.Stardust, 30),
        new Reward("frost", "Frostbite",         Rarity.Rare,      RewardKind.Cursor),
        new Reward("sm14",  "Smooth 14",         Rarity.Rare,      RewardKind.Crosshair),
        new Reward("sakura","Sakura",            Rarity.Rare,      RewardKind.Cursor),
        new Reward("sd75",  "75 Shards",         Rarity.Epic,      RewardKind.Stardust, 75),
        new Reward("neon",  "Neon Horizon",      Rarity.Epic,      RewardKind.Wallpaper),
        new Reward("midn",  "Midnight Contours", Rarity.Epic,      RewardKind.Wallpaper),
        new Reward("galaxy","Galaxy",            Rarity.Legendary, RewardKind.Cursor),
        new Reward("prem15","15 min Premium",    Rarity.Legendary, RewardKind.Premium, 15),
        new Reward("prem30","30 min Premium",    Rarity.Mythic,    RewardKind.Premium, 30),
        new Reward("prem60","1 hour Premium",    Rarity.Mythic,    RewardKind.Premium, 60),
    };

    private int _stardust;
    private int _streak;
    private string _lastSpinDate = "";
    private int _spinsSinceLegendary;
    private int _spinsToday;
    private int _boughtSpins;
    private string _boughtDate = "";
    // Two independent equip slots, so a won cursor and a won wallpaper can both
    // be on at the same time.
    private string _equippedCursor = "";
    private string _equippedWallpaper = "";
    private string _rewardModalShownDate = "";
    private DateTime? _rewardPremiumUntil;
    private readonly HashSet<string> _owned = new();
    private readonly Random _wheelRng = new();
    private bool _spinning;

    // True while an equipped reward cursor owns Roblox's cursor files, with a
    // snapshot of whether the Crosshair page had its own crosshair applied — so
    // un-equipping can hand the cursor back to the page rather than just wiping it.
    private bool _cursorOwnedByReward;
    private bool _pageCursorApplied;

    // The wallpaper that was set before a wallpaper cosmetic was equipped, so
    // un-equipping in the same session puts the user's own background back.
    private string _wallpaperBeforeCosmetic = "";

    // Whether the unlock chime plays. On by default; the modal has a toggle.
    private bool _unlockSoundOn = true;

    private const int SpinCost = 60;   // Stardust to buy an extra spin

    private static string Today => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private int SpinsPerDay => IsPro ? 2 : 1;
    private int SpinsLeft => Math.Max(0, SpinsPerDay
        + (_boughtDate == Today ? _boughtSpins : 0)
        - (_lastSpinDate == Today ? _spinsToday : 0));

    /// <summary>True while a wheel-won Premium reward is still in effect (ORed into IsPro).</summary>
    public bool RewardPremiumActive => _rewardPremiumUntil is { } u && DateTime.UtcNow < u;

    /// <summary>Each cosmetic's signature accent, applied when it is equipped.</summary>
    private static Color SignatureColor(Reward r) => r.Id switch
    {
        "sakura" => Color.FromRgb(0xFF, 0x5B, 0xC8),   // pink
        "gold"   => Color.FromRgb(0xFF, 0xC5, 0x3D),   // gold
        "frost"  => Color.FromRgb(0x8F, 0xD4, 0xFF),   // ice
        "galaxy" => Color.FromRgb(0x9B, 0x5B, 0xFA),   // violet
        "mintp"  => Color.FromRgb(0x4B, 0xD4, 0x7B),   // mint
        "sm14"   => Color.FromRgb(0x2F, 0xD4, 0xA8),   // teal
        "neon"   => Color.FromRgb(0x45, 0xB7, 0xFF),   // neon cyan
        "midn"   => Color.FromRgb(0x5B, 0x7C, 0xFA),   // indigo
        _ => RarityColor(r.Rarity)
    };

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
        _boughtDate = s.RewardBoughtDate ?? "";
        _boughtSpins = _boughtDate == Today ? Math.Max(0, s.RewardBought) : 0;
        _equippedCursor = s.RewardEquippedCursor ?? "";
        _equippedWallpaper = s.RewardEquippedWallpaper ?? "";
        _wallpaperBeforeCosmetic = s.RewardWallpaperPrev ?? "";
        _unlockSoundOn = s.UnlockSoundEnabled;
        if (UnlockSoundToggle != null) UnlockSoundToggle.IsChecked = _unlockSoundOn;
        // Migrate an old single-slot equip into whichever slot its kind belongs to.
        if (!string.IsNullOrEmpty(s.RewardEquipped))
        {
            if (RewardCosmetics.IsCursor(s.RewardEquipped) && _equippedCursor.Length == 0)
                _equippedCursor = s.RewardEquipped;
            else if (RewardCosmetics.IsWallpaper(s.RewardEquipped) && _equippedWallpaper.Length == 0)
                _equippedWallpaper = s.RewardEquipped;
        }
        _rewardModalShownDate = s.RewardModalShown ?? "";
        _rewardPremiumUntil = DateTime.TryParse(s.RewardPremiumUntil, CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out DateTime u) ? u : null;
        _owned.Clear();
        if (!string.IsNullOrWhiteSpace(s.RewardOwned))
            foreach (string id in s.RewardOwned.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                _owned.Add(id);

        RefreshLicense();   // apply any still-active Premium reward to IsPro

        // Re-assert an equipped cursor cosmetic. This runs after LoadCrosshair has
        // already re-applied the Crosshair page's own pick, so the reward wins — and
        // like the Crosshair page, re-writing on every launch is what survives a
        // Roblox update replacing the version folder with the stock cursor.
        if (_equippedCursor.Length > 0 && _owned.Contains(_equippedCursor))
            ApplyCosmeticEffect(_equippedCursor, fromUser: false);
    }

    // ---- page ----

    private void OpenRewards_Click(object sender, RoutedEventArgs e) => ShowRewardsModal();

    private void CloseRewards_Click(object sender, RoutedEventArgs e)
    {
        if (RewardsModal != null) RewardsModal.Visibility = Visibility.Collapsed;
    }

    private void ShowRewardsModal()
    {
        if (RewardsModal == null) return;
        BuildWheel();
        BuildOddsPanel();
        RefreshRewardsHeader();
        BuildCollection();
        if (RewardResultCard != null) RewardResultCard.Visibility = Visibility.Collapsed;
        RewardsModal.Visibility = Visibility.Visible;
    }

    /// <summary>Pops the rewards up once per day; also refreshes the gift badge.</summary>
    private void MaybeShowDailyRewards()
    {
        RefreshGiftBadge();
        if (_rewardModalShownDate != Today)
        {
            _rewardModalShownDate = Today;
            _settingsDirty = true;
            SaveAppSettings();
            ShowRewardsModal();
        }
    }

    private void RefreshGiftBadge()
    {
        if (RewardsGiftBadge != null)
            RewardsGiftBadge.Visibility = SpinsLeft > 0 ? Visibility.Visible : Visibility.Collapsed;
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
        if (BuySpinButton != null)
        {
            BuySpinButton.IsEnabled = _stardust >= SpinCost && !_spinning;
            BuySpinButton.Content = $"Buy a spin  ✦ {SpinCost}";
        }
        if (RewardPremiumText != null)
        {
            if (RewardPremiumActive)
            {
                int mins = (int)Math.Ceiling((_rewardPremiumUntil!.Value - DateTime.UtcNow).TotalMinutes);
                RewardPremiumText.Text = mins >= 60 ? $"Plus active — {mins / 60}h {mins % 60}m left" : $"Plus active — {mins}m left";
                RewardPremiumText.Visibility = Visibility.Visible;
            }
            else RewardPremiumText.Visibility = Visibility.Collapsed;
        }
        RefreshGiftBadge();
    }

    // ---- the wheel ----
    //
    // Not a carnival pie: a hollow loot ring. Each slice is a glowing band
    // between an inner and an outer radius, separated from its neighbours by a
    // small gap, so the wheel reads as a ring of lit segments with the spin orb
    // floating in the hollow centre. The angular layout per index is unchanged,
    // so the landing maths in Spin_Click still points the pointer at the right
    // slice - only the drawing differs.

    private const double WheelR = 160;        // outer radius of the ring
    private const double WheelInner = 92;      // inner radius (the hollow)
    private const double WheelCx = 175, WheelCy = 175;
    private const double SegGapDeg = 2.6;      // gap between adjacent segments

    private void BuildWheel()
    {
        if (WheelSectors == null) return;
        WheelSectors.Children.Clear();

        // A faint dark backing ring so the gaps between lit segments read as
        // seams in one object rather than holes onto the panel behind.
        WheelSectors.Children.Add(BackingRing());

        int n = WheelCatalog.Count;
        double step = 360.0 / n;
        for (int i = 0; i < n; i++)
        {
            Reward reward = WheelCatalog[i];
            double a0 = i * step, a1 = (i + 1) * step;
            WheelSectors.Children.Add(RingSegment(a0, a1, reward.Rarity));
            WheelSectors.Children.Add(SliceLabel(reward, (a0 + a1) / 2));
        }
    }

    private static Point OnCircle(double deg, double radius)
    {
        double t = deg * Math.PI / 180.0;   // clockwise from the top
        return new Point(WheelCx + radius * Math.Sin(t), WheelCy - radius * Math.Cos(t));
    }

    private static Ellipse BackingRing()
    {
        double outer = WheelR + 4;
        return new Ellipse
        {
            Width = outer * 2,
            Height = outer * 2,
            Fill = null,
            Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0x12, 0x0E, 0x22)),
            StrokeThickness = (WheelR + 6) - (WheelInner - 6),
            Margin = new Thickness(WheelCx - outer, WheelCy - outer, 0, 0),
        };
    }

    // One annular segment: a glowing band from WheelInner to WheelR, inset by
    // half the gap on each side, filled dark-core-to-bright-rim and haloed in
    // its rarity colour (higher rarity glows harder).
    private static Path RingSegment(double a0, double a1, Rarity rarity)
    {
        double g = SegGapDeg / 2.0;
        double b0 = a0 + g, b1 = a1 - g;

        Point oi = OnCircle(b0, WheelInner);   // inner-start
        Point oo = OnCircle(b0, WheelR);        // outer-start
        Point po = OnCircle(b1, WheelR);        // outer-end
        Point pi = OnCircle(b1, WheelInner);    // inner-end

        var fig = new PathFigure { StartPoint = oi, IsClosed = true };
        fig.Segments.Add(new LineSegment(oo, true));
        fig.Segments.Add(new ArcSegment(po, new Size(WheelR, WheelR), 0, false, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(pi, true));
        fig.Segments.Add(new ArcSegment(oi, new Size(WheelInner, WheelInner), 0, false, SweepDirection.Counterclockwise, true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);

        Color c = RarityColor(rarity);
        var fill = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = new Point(WheelCx, WheelCy),
            GradientOrigin = new Point(WheelCx, WheelCy),
            RadiusX = WheelR,
            RadiusY = WheelR,
        };
        fill.GradientStops.Add(new GradientStop(Scale(c, 0.22), WheelInner / WheelR));
        fill.GradientStops.Add(new GradientStop(Scale(c, 0.80), 0.90));
        fill.GradientStops.Add(new GradientStop(Lighten(c, 0.30), 1.0));
        fill.Freeze();

        double glow = rarity switch
        {
            Rarity.Mythic => 1.0,
            Rarity.Legendary => 0.9,
            Rarity.Epic => 0.65,
            Rarity.Rare => 0.45,
            _ => 0.28,
        };

        return new Path
        {
            Data = geo,
            Fill = fill,
            Stroke = new SolidColorBrush(Lighten(c, 0.35)),
            StrokeThickness = 1.4,
            Effect = new DropShadowEffect
            {
                Color = c,
                BlurRadius = 16,
                ShadowDepth = 0,
                Opacity = glow,
            },
        };
    }

    private static Color Scale(Color c, double k) =>
        Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));

    private static Color Lighten(Color c, double t) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * t),
        (byte)(c.G + (255 - c.G) * t),
        (byte)(c.B + (255 - c.B) * t));

    private static TextBlock SliceLabel(Reward reward, double midDeg)
    {
        Point at = OnCircle(midDeg, (WheelInner + WheelR) / 2.0);
        double rot = midDeg;
        bool flip = midDeg > 90 && midDeg < 270;   // keep text upright on the lower half
        if (flip) rot += 180;

        var tb = new TextBlock
        {
            Text = reward.Name,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
            Width = 72,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 12,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(rot - 90),   // read outward along the spoke
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 0, Opacity = 0.9 },
        };
        Canvas.SetLeft(tb, at.X - 36);
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

        // Land the chosen slice under the top pointer, after five full turns.
        double step = 360.0 / WheelCatalog.Count;
        double sliceCentre = index * step + step / 2;
        double jitter = (_wheelRng.NextDouble() - 0.5) * (step * 0.6);
        double landing = 360 - sliceCentre - jitter;   // orientation that puts the slice up top
        double to = RewardWheel.SpinTarget(WheelRotate.Angle, landing, turns: 5);

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

        // A cosmetic counts as newly unlocked only the first time it is won.
        bool newUnlock = reward.Collectible && !_owned.Contains(reward.Id);
        GrantReward(reward);

        _spinning = false;
        _settingsDirty = true;
        SaveAppSettings();

        if (newUnlock) PlayUnlockSound();
        ShowRewardResult(reward, newUnlock);
        RefreshRewardsHeader();
        BuildCollection(newUnlock ? reward.Id : null);   // pulse the chip that just unlocked
    }

    /// <summary>The unlock sparkle — reuses the clip-feedback sound engine's "unlock" chime.</summary>
    private void PlayUnlockSound()
    {
        if (!_unlockSoundOn) return;
        try { (_clipSoundPlayer ??= new ClipSound()).Play("unlock", null, 0.6); }
        catch { /* a cosmetic unlocks whether or not the chime plays */ }
    }

    private void UnlockSound_Toggled(object sender, RoutedEventArgs e)
    {
        _unlockSoundOn = UnlockSoundToggle?.IsChecked == true;
        _settingsDirty = true;
        SaveAppSettings();
    }

    private void GrantReward(Reward reward)
    {
        switch (reward.Kind)
        {
            case RewardKind.Stardust:
                _stardust += reward.Value;
                break;
            case RewardKind.Premium:
                DateTime from = RewardPremiumActive ? _rewardPremiumUntil!.Value : DateTime.UtcNow;
                _rewardPremiumUntil = from + TimeSpan.FromMinutes(reward.Value);
                RefreshLicense();   // flip IsPro on now, not at the next minute tick
                break;
            default:
                _owned.Add(reward.Id);   // cosmetics
                break;
        }
    }

    // ---- Stage 3: buy spins, equip cosmetics ----

    private void BuySpin_Click(object sender, RoutedEventArgs e)
    {
        if (_spinning || _stardust < SpinCost) return;
        _stardust -= SpinCost;
        if (_boughtDate != Today) { _boughtDate = Today; _boughtSpins = 0; }
        _boughtSpins++;
        _settingsDirty = true;
        SaveAppSettings();
        RefreshRewardsHeader();
    }

    /// <summary>
    /// Equips a won cosmetic — the real cursor (into Roblox) or wallpaper (as the
    /// app background) — or clears it, handing the cursor back to the Crosshair
    /// page and restoring the background it replaced.
    /// </summary>
    private void EquipCosmetic(string id)
    {
        // A cursor and a wallpaper live in their own slots, so equipping one never
        // knocks the other off.
        ref string slot = ref (RewardCosmetics.IsWallpaper(id) ? ref _equippedWallpaper : ref _equippedCursor);

        if (slot == id)
        {
            UndoCosmeticEffect(id);          // clicking the equipped one takes it off
            slot = "";
            SetRewardEquipStatus("");
        }
        else
        {
            UndoCosmeticEffect(slot);        // take off whatever was in this slot
            slot = id;
            ApplyCosmeticEffect(id, fromUser: true);
        }

        _settingsDirty = true;
        SaveAppSettings();
        BuildCollection();
        RefreshRewardsHeader();
    }

    /// <summary>Applies a cosmetic's real effect: a crosshair into Roblox, or a background.</summary>
    private void ApplyCosmeticEffect(string id, bool fromUser)
    {
        if (RewardCosmetics.Cursor(id) is { } style)
        {
            // Remember the Crosshair page's own state only when the reward is first
            // taking the cursor over, so un-equip can give it back untouched.
            if (!_cursorOwnedByReward) _pageCursorApplied = _crosshairApplied;

            CursorApplyResult res = CursorWriter.Apply(px => CrosshairImage.RenderCursorPng(style, px, 1.0));
            _crosshairApplied = res.AnyWritten || CursorWriter.IsApplied();
            _cursorOwnedByReward = true;

            if (fromUser)
                SetRewardEquipStatus(res switch
                {
                    { AnyWritten: true } => $"Equipped — fully close and reopen Roblox to see your {NameOf(id)} cursor.",
                    { Failed: > 0 } => "Close Roblox and equip again — its cursor files were in use.",
                    _ => "No Roblox install found to apply the cursor to."
                });
            return;
        }

        if (RewardCosmetics.Wallpaper(id) is { } design)
        {
            // Only snapshot the user's own wallpaper the first time a cosmetic takes
            // the slot, so swapping one reward wallpaper for another still restores
            // the user's picture (not the previous reward) on un-equip.
            if (_wallpaperFile != Wallpaper.RewardWallpaperName)
                _wallpaperBeforeCosmetic = _wallpaperFile;

            string? stored = WallpaperGallery.InstallReward(design);
            if (stored != null) { _wallpaperFile = stored; ApplyWallpaper(); }

            if (fromUser)
                SetRewardEquipStatus(stored != null
                    ? $"Equipped — {design.Name} is now your app background."
                    : "That background could not be saved — the settings folder may be read-only.");
        }
    }

    /// <summary>Takes a cosmetic's real effect back off: hands the cursor back, or restores the background.</summary>
    private void UndoCosmeticEffect(string id)
    {
        if (id.Length == 0) return;

        if (RewardCosmetics.IsCursor(id))
        {
            // Give the cursor back to the Crosshair page if it had one applied,
            // otherwise put Roblox's own cursor back.
            if (_pageCursorApplied && !IsDefaultSelected)
            {
                CursorApplyResult res = ApplyCurrentCrosshair();
                _crosshairApplied = res.AnyWritten || CursorWriter.IsApplied();
            }
            else
            {
                CursorWriter.Remove();
                _crosshairApplied = CursorWriter.IsApplied();
            }
            _cursorOwnedByReward = false;
        }
        else if (RewardCosmetics.IsWallpaper(id))
        {
            _wallpaperFile = Wallpaper.Resolve(_wallpaperBeforeCosmetic) != null ? _wallpaperBeforeCosmetic : "";
            _wallpaperBeforeCosmetic = "";
            ApplyWallpaper();
        }
    }

    /// <summary>
    /// Called when the user sets or clears their own wallpaper: any equipped
    /// wallpaper cosmetic is no longer what's showing, so drop it and forget the
    /// old restore baseline (their new choice is the baseline now).
    /// </summary>
    private void ClearWallpaperCosmeticEquip()
    {
        if (_equippedWallpaper.Length == 0 && _wallpaperBeforeCosmetic.Length == 0) return;
        _equippedWallpaper = "";
        _wallpaperBeforeCosmetic = "";
        BuildCollection();
    }

    private void SetRewardEquipStatus(string text)
    {
        if (RewardEquipStatus == null) return;
        RewardEquipStatus.Text = text;
        RewardEquipStatus.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The display name for a cosmetic id, for status lines.</summary>
    private static string NameOf(string id) => WheelCatalog.Find(r => r.Id == id)?.Name ?? "new";

    /// <summary>The user-facing word for a cosmetic kind — crosshairs read as "Cursor".</summary>
    private static string KindWord(RewardKind kind) => kind switch
    {
        RewardKind.Wallpaper => "Wallpaper",
        _ => "Cursor"   // Cursor and Crosshair both apply as the in-game cursor
    };

    private void ShowRewardResult(Reward reward, bool newUnlock)
    {
        if (RewardResultCard == null || RewardResultName == null || RewardResultRarity == null) return;
        RewardResultName.Text = reward.Kind switch
        {
            RewardKind.Stardust => $"+{reward.Value} Shards",
            RewardKind.Premium => reward.Value >= 60 ? $"+{reward.Value / 60}h Plus unlocked" : $"+{reward.Value} min Plus unlocked",
            _ => reward.Name
        };
        RewardResultRarity.Text = reward.Rarity.ToString().ToUpperInvariant();
        Color rc = RarityColor(reward.Rarity);
        RewardResultRarity.Foreground = new SolidColorBrush(rc);
        RewardResultCard.BorderBrush = new SolidColorBrush(rc);

        if (RewardResultSub != null)
            RewardResultSub.Text = reward.Kind switch
            {
                RewardKind.Stardust => "Added to your balance",
                RewardKind.Premium => "Plus time added — enjoy",
                _ => newUnlock ? "New — added to your collection" : "Already in your collection",
            };

        // The badge only fires for a genuinely new cosmetic; a duplicate or a
        // currency/Premium win just shows the card.
        if (RewardUnlockedBadge != null)
        {
            RewardUnlockedBadge.Visibility = newUnlock ? Visibility.Visible : Visibility.Collapsed;
            RewardUnlockedBadge.Foreground = new SolidColorBrush(rc);
        }

        RewardResultCard.Visibility = Visibility.Visible;
        PlayRewardRevealAnimation(newUnlock);
    }

    /// <summary>
    /// A subtle reveal when a spin lands: the card fades in with a small
    /// scale-pop, and a won cosmetic's "UNLOCKED" badge springs in a touch after.
    /// </summary>
    private void PlayRewardRevealAnimation(bool newUnlock)
    {
        // Card: fade + a gentle pop (a small overshoot reads as "landed", not bouncy).
        RewardResultCard!.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));

        if (RewardResultScale != null)
        {
            var pop = new DoubleAnimation(0.92, 1.0, TimeSpan.FromMilliseconds(360))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 }
            };
            RewardResultScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            RewardResultScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }

        if (!newUnlock || RewardUnlockedBadge == null || RewardUnlockedScale == null) return;

        // Badge: a slightly livelier spring, started just after the card so the eye
        // catches it as a second beat.
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
        {
            BeginTime = TimeSpan.FromMilliseconds(110)
        };
        RewardUnlockedBadge.BeginAnimation(UIElement.OpacityProperty, fade);

        var spring = new DoubleAnimation(0.7, 1.0, TimeSpan.FromMilliseconds(420))
        {
            BeginTime = TimeSpan.FromMilliseconds(110),
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 }
        };
        RewardUnlockedScale.BeginAnimation(ScaleTransform.ScaleXProperty, spring);
        RewardUnlockedScale.BeginAnimation(ScaleTransform.ScaleYProperty, spring);
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

    private void BuildCollection(string? pulseId = null)
    {
        if (CollectionPanel == null) return;
        CollectionPanel.Children.Clear();
        foreach (Reward reward in WheelCatalog)
        {
            if (!reward.Collectible) continue;
            bool have = _owned.Contains(reward.Id);
            bool equipped = have && (_equippedCursor == reward.Id || _equippedWallpaper == reward.Id);
            Color rc = SignatureColor(reward);

            var restBorder = new SolidColorBrush(equipped ? rc : have ? Color.FromArgb(0x66, rc.R, rc.G, rc.B) : Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF));
            var chip = new Border
            {
                Width = 150, Height = 58, CornerRadius = new CornerRadius(8),
                Background = Brush("Panel2"), Margin = new Thickness(0, 0, 10, 10),
                BorderBrush = restBorder,
                BorderThickness = new Thickness(equipped ? 2 : 1),
                Padding = new Thickness(10, 8, 10, 8),
                Opacity = have ? 1.0 : 0.45,
                Cursor = have ? System.Windows.Input.Cursors.Hand : null,
                ToolTip = have ? (equipped ? "Equipped — click to remove" : "Click to equip") : "Win this from the wheel"
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = have ? reward.Name : "Locked", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brush("TextBright") });
            sp.Children.Add(new TextBlock
            {
                Text = equipped ? $"{KindWord(reward.Kind)}  •  EQUIPPED"
                                : $"{KindWord(reward.Kind)}  •  {reward.Rarity}",
                FontSize = 10, Foreground = new SolidColorBrush(rc), Margin = new Thickness(0, 2, 0, 0)
            });
            chip.Child = sp;
            if (have) { string id = reward.Id; chip.MouseLeftButtonUp += (_, _) => EquipCosmetic(id); }
            CollectionPanel.Children.Add(chip);

            if (reward.Id == pulseId) PulseChip(chip, restBorder, rc);
        }
    }

    /// <summary>
    /// A gentle breath on a chip that just unlocked: a couple of scale pulses
    /// and a one-shot border flash in the cosmetic's colour.
    /// </summary>
    private static void PulseChip(Border chip, SolidColorBrush border, Color color)
    {
        chip.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(1, 1);
        chip.RenderTransform = scale;

        var pulse = new DoubleAnimation(1.0, 1.06, TimeSpan.FromMilliseconds(280))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(2),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);

        // Flash the border bright, then settle back to its resting colour.
        var flash = new ColorAnimation(color, Color.FromArgb(border.Color.A, border.Color.R, border.Color.G, border.Color.B),
            TimeSpan.FromMilliseconds(900))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
        };
        border.BeginAnimation(SolidColorBrush.ColorProperty, flash);
    }
}
