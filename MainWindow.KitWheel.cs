using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace JinxyClicker;

/// <summary>One kit as the roster shows it.</summary>
/// <param name="Image">Its picture, or null when it has none.</param>
/// <param name="NoImage">
/// Visibility for the placeholder. Carried as a value rather than worked out in
/// the template, because a binding cannot turn null into Visible on its own
/// without a converter, and one line here is cheaper than one.
/// </param>
/// <summary>A saved wheel as the page shows it.</summary>
public sealed record PresetEntry(string Name, string Count, string Tip);

public sealed record KitEntry(
    string Name, bool Selected, double Dimmed, ImageSource? Image, Visibility NoImage,
    Brush Edge, Brush Ink, string Tip, Visibility Removable);

/// <summary>
/// The kit randomizer: tick a set, then roll through it one at a time.
/// </summary>
/// <remarks>
/// Two lists, not one. The roster is every kit someone might play — the game
/// has well over a hundred — and the wheel is the handful they ticked for this
/// run. Collapsing them would mean deleting kits to exclude them, and adding
/// them back to play them again.
///
/// Rolls without replacement: the run is getting through the ticked set once,
/// so a rolled kit leaves the pool and the remaining count actually counts down.
///
/// Split out of MainWindow's own file, which is already four thousand lines. The
/// choosing and counting live in <see cref="KitWheel"/> and are tested there.
/// </remarks>
public partial class MainWindow
{
    private readonly KitRoster _roster = KitWheelStore.Load();
    private readonly List<string> _rolled = new();
    private bool _rolling;

    /// <summary>The headline's usual size, and the smaller one a sentence needs.</summary>
    /// <remarks>
    /// "READY?" and a kit's name are one or two words and carry the panel at 44.
    /// The first-run prompt is a whole sentence, which at that size runs off
    /// both sides of the card.
    /// </remarks>
    private const double HeadlineSize = 44;
    private const double FirstRunHeadlineSize = 27;

    /// <summary>How many kits go past before it settles.</summary>
    private const int ReelLength = 14;

    /// <summary>Gap between kits at the start of the roll, and at the end.</summary>
    /// <remarks>
    /// The gap grows across the reel rather than staying put. A fixed interval
    /// reads as a strobe — every frame the same, then an abrupt stop — where a
    /// reel that slows into its result reads as one motion coming to rest, which
    /// is the whole difference between flickering and rolling.
    /// </remarks>
    private const double ReelFastMs = 30;
    private const double ReelSlowMs = 130;

    private void NavKitWheel_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(NavKitWheel, PageKitWheel, "Kit Randomizer", "Roll a kit you have not played yet");

        OpenKitListIfNothingPicked();

        _ = FetchMissingKitArtAsync();
    }

    private readonly CancellationTokenSource _kitArtCts = new();
    private bool _kitArtStarted;

    /// <summary>
    /// Pulls down any kit pictures this copy of the app has not got.
    /// </summary>
    /// <remarks>
    /// The install ships a picture for every kit on the roster, so the usual
    /// outcome is that this finds nothing missing and never touches the network.
    /// It covers what the installer cannot: a kit added to the roster by hand,
    /// or one whose picture failed to install.
    ///
    /// Started from opening the page rather than from launch: somebody who never
    /// opens it never spends the bandwidth, and by the time the page is on
    /// screen the pictures are wanted.
    /// </remarks>
    private async Task FetchMissingKitArtAsync()
    {
        if (_kitArtStarted) return;

        // Switchable from the published config, for the case where the wiki
        // changes shape and every fetch starts saving rubbish. Turning it off
        // beats waiting for everyone to install a fix.
        if (!RemoteConfig.Current.KitArtFetchEnabled) return;

        _kitArtStarted = true;

        await KitArtFetch.RunAsync(
            _roster.Kits,
            batchDone: () =>
            {
                // Cached nulls from before the download have to go, or the
                // tiles keep showing placeholders for pictures now on disk.
                _kitArt.Clear();
                RefreshKitWheel();

                return Task.CompletedTask;
            },
            _kitArtCts.Token).ConfigureAwait(true);
    }

    // ---- the roster ----

    private void AddKit_Click(object sender, RoutedEventArgs e) => AddTypedKit();

    private void KitNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        AddTypedKit();
        e.Handled = true;
    }

    private void AddTypedKit()
    {
        string? name = KitWheel.CleanName(KitNameBox.Text);

        if (name != null && KitWheel.Add(_roster.Kits, name))
        {
            // Ticked on the way in. Somebody who just typed a kit's name wants
            // it in this run; making them tick it as a second step is a step
            // that would never be skipped.
            _roster.Selected.Add(name);

            KitNameBox.Clear();
            Save();
            RefreshKitWheel();
            return;
        }

        KitCountText.Text = name == null ? "Type a kit name" : "Already on the roster";
    }

    private void RemoveKit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kit } || _rolling) return;

        // The tile behind this button is a button too, and its click would
        // otherwise fire straight after — putting the kit back on the wheel as
        // it was being deleted.
        e.Handled = true;

        if (Shipped.Contains(kit)) return;

        _roster.Kits.RemoveAll(k => k.Equals(kit, StringComparison.OrdinalIgnoreCase));
        _roster.Selected.RemoveAll(k => k.Equals(kit, StringComparison.OrdinalIgnoreCase));
        _rolled.RemoveAll(k => k.Equals(kit, StringComparison.OrdinalIgnoreCase));

        Save();
        RefreshKitWheel();
    }

    private void KitTicked(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: string kit } box) return;

        // Ignored while rolling: unticking the kit being rolled would change the
        // pool underneath the animation.
        if (_rolling)
        {
            RefreshKitWheel();
            return;
        }

        _roster.Selected.RemoveAll(k => k.Equals(kit, StringComparison.OrdinalIgnoreCase));

        if (box.IsChecked == true) _roster.Selected.Add(kit);

        // A kit unticked mid-run was possibly already rolled; dropping it from
        // the rolled list too keeps the progress count honest.
        if (box.IsChecked != true)
            _rolled.RemoveAll(k => k.Equals(kit, StringComparison.OrdinalIgnoreCase));

        Save();
        RefreshKitWheel();
    }

    // ---- searching the roster ----

    private string _kitSearch = "";

    /// <summary>The kits the list is currently showing.</summary>
    private List<string> ShownKits() => KitWheel.Matching(_roster.Kits, _kitSearch);

    private bool Searching => _kitSearch.Trim().Length > 0;

    private void KitSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _kitSearch = KitSearchBox.Text;

        RefreshKitWheel();
    }

    private void ClearKitSearch_Click(object sender, RoutedEventArgs e) =>
        KitSearchBox.Clear();

    /// <summary>
    /// Ticks everything the list is showing.
    /// </summary>
    /// <remarks>
    /// Scoped to the search results when there is a search, which is the point
    /// of having one — type "ice", take all of them. The button says which it
    /// will do, because "Select all" doing either would be a guess either way.
    ///
    /// Adds to the selection rather than replacing it, so selecting the matches
    /// for one search and then another accumulates instead of the second wiping
    /// the first. With no search this still ends up as the whole roster.
    /// </remarks>
    private void SelectAllKits_Click(object sender, RoutedEventArgs e)
    {
        if (_rolling) return;

        if (!Searching)
        {
            _roster.Selected = new List<string>(_roster.Kits);
        }
        else
        {
            foreach (string kit in ShownKits())
            {
                if (!_roster.Selected.Any(s => s.Equals(kit, StringComparison.OrdinalIgnoreCase)))
                    _roster.Selected.Add(kit);
            }
        }

        Save();
        RefreshKitWheel();
    }

    /// <summary>
    /// Unticks everything the list is showing.
    /// </summary>
    /// <remarks>
    /// Only the matches while searching. Clearing the whole wheel from behind a
    /// filter — where most of what would be cleared is not even on screen — is
    /// the kind of thing that gets noticed one roll too late.
    ///
    /// The rolled list is only emptied on a full clear. Dropping a few kits mid
    /// run should not restart the run.
    /// </remarks>
    private void ClearKits_Click(object sender, RoutedEventArgs e)
    {
        if (_rolling) return;

        if (!Searching)
        {
            _roster.Selected.Clear();
            _rolled.Clear();
        }
        else
        {
            foreach (string kit in ShownKits())
            {
                _roster.Selected.RemoveAll(k => k.Equals(kit, StringComparison.OrdinalIgnoreCase));
                _rolled.RemoveAll(k => k.Equals(kit, StringComparison.OrdinalIgnoreCase));
            }
        }

        Save();
        RefreshKitWheel();
    }

    private void Save() => KitWheelStore.Save(_roster);

    // ---- saved wheels ----

    private void SaveWheel_Click(object sender, RoutedEventArgs e) => SaveCurrentWheel();

    private void WheelNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        SaveCurrentWheel();
        e.Handled = true;
    }

    private void SaveCurrentWheel()
    {
        if (_rolling) return;

        if (KitWheel.SavePreset(_roster.Presets, WheelNameBox.Text, _roster.Selected))
        {
            WheelNameBox.Clear();
            Save();
            RefreshKitWheel();
            return;
        }

        // Said on the hint line rather than in a dialog: every way this fails is
        // visible on the page already.
        WheelHintText.Text =
            _roster.Selected.Count == 0 ? "Pick some kits first"
            : KitWheel.CleanName(WheelNameBox.Text) == null ? "Give it a name"
            : $"{KitWheel.MaxPresets} saved wheels is the limit";
    }

    private void LoadWheel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name } || _rolling) return;

        KitPreset? preset = _roster.Presets
            .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (preset == null) return;

        _roster.Selected = KitWheel.ApplyPreset(_roster.Kits, preset.Kits);

        // Loading a wheel starts that wheel. Keeping the rolled list would leave
        // a run half finished against a set it was never run against.
        _rolled.Clear();

        RollBadgeText.Text = "KIT ROLL";
        RollHeadlineText.Text = "READY?";
        RollSubText.Text = $"Loaded {preset.Name}";

        Save();
        RefreshKitWheel();
    }

    private void DeleteWheel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name } || _rolling) return;

        _roster.Presets.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        Save();
        RefreshKitWheel();
    }

    /// <summary>
    /// The kits that came with the app, which cannot be deleted.
    /// </summary>
    /// <remarks>
    /// Only what somebody typed in themselves can be removed. Deleting a real
    /// kit off the roster is almost always a misclick — it is one of a hundred
    /// small tiles — and getting it back means knowing its exact spelling,
    /// including the accents and brackets the game uses. Unticking already does
    /// the thing people actually want, which is keeping it off the wheel.
    /// </remarks>
    private static readonly HashSet<string> Shipped =
        new(KitWheel.StarterRoster(), StringComparer.OrdinalIgnoreCase);

    private static readonly Brush PickedEdge = Frozen(0xA7, 0x8B, 0xFA);
    private static readonly Brush PlainEdge = Frozen(0x33, 0x2A, 0x52);
    private static readonly Brush PickedInk = Frozen(0xFF, 0xFF, 0xFF);
    private static readonly Brush PlainInk = Frozen(0x9A, 0x90, 0xB8);

    /// <summary>A brush that can be shared across every tile without copying.</summary>
    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();

        return brush;
    }

    /// <summary>Clicking a tile puts the kit on the wheel, or takes it off.</summary>
    private void KitTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kit } || _rolling) return;

        bool picked = _roster.Selected.Any(k => k.Equals(kit, StringComparison.OrdinalIgnoreCase));

        _roster.Selected.RemoveAll(k => k.Equals(kit, StringComparison.OrdinalIgnoreCase));

        if (picked)
        {
            // Taking a kit off the wheel takes it out of the run as well, or the
            // progress count would keep counting something no longer in it.
            _rolled.RemoveAll(k => k.Equals(kit, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            _roster.Selected.Add(kit);
        }

        Save();
        RefreshKitWheel();
    }

    /// <summary>
    /// Decoded pictures, kept for the life of the window.
    /// </summary>
    /// <remarks>
    /// The roster repaints on every tick, roll and removal, and decoding forty
    /// images each time would make ticking a checkbox visibly slow. Cleared for
    /// one kit when its picture changes rather than wholesale.
    /// </remarks>
    private readonly Dictionary<string, ImageSource?> _kitArt = new(StringComparer.OrdinalIgnoreCase);

    private ImageSource? LoadKitImage(string kit)
    {
        if (_kitArt.TryGetValue(kit, out ImageSource? cached)) return cached;

        string? path = KitImages.Find(kit);
        ImageSource? art = path == null ? null : DecodeFrozen(path);

        _kitArt[kit] = art;

        return art;
    }

    /// <summary>
    /// Reads a picture and puts it on the backdrop, ready to show.
    /// </summary>
    /// <remarks>
    /// Both steps live in KitArtImage rather than here. The decode in particular
    /// is not the one-liner it appears to be — done the obvious way WPF discards
    /// the transparency and every kit draws as a coloured rectangle.
    ///
    /// Composed here rather than baked into the file, so the backdrop can change
    /// without re-fetching a hundred pictures, and a picture somebody drops in
    /// themselves gets the same framing.
    /// </remarks>
    private static ImageSource? DecodeFrozen(string path)
    {
        System.Windows.Media.Imaging.BitmapSource? image = KitArtImage.Decode(path);

        return image == null ? null : KitArtImage.OnBackdrop(image);
    }

    private void SetKitImage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kit } || _rolling) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Picture for {kit}",
            Filter = KitImages.FileFilter,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true) return;

        if (KitImages.Set(kit, dialog.FileName) == null)
        {
            MessageBox.Show(this,
                "That image could not be used. It may be open in another program, or in a format this app cannot read.",
                "Kit picture", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Only this kit's entry is stale.
        _kitArt.Remove(kit);

        RefreshKitWheel();
    }

    /// <summary>
    /// Opens and closes the roster.
    /// </summary>
    /// <remarks>
    /// Closed by default. Forty kits is most of the page, and once a set is
    /// picked the list is not what anyone came back for — the roll button is.
    /// The header keeps showing what is picked, so closing it does not hide the
    /// answer, only the controls.
    /// </remarks>
    private void KitListToggle_Click(object sender, RoutedEventArgs e) =>
        SetKitListOpen(KitListBody.Visibility != Visibility.Visible);

    private void SetKitListOpen(bool open)
    {
        KitListBody.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        KitListChevron.Text = open ? "▾" : "▸";

        if (!open) return;

        // Fades in rather than appearing, so a list this tall does not read as
        // the page having jumped.
        KitListBody.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.0, 1.0, new Duration(TimeSpan.FromMilliseconds(140))));
    }

    private bool _kitListAutoOpened;

    /// <summary>
    /// Opens the roster by itself when there is nothing on the wheel yet.
    /// </summary>
    /// <remarks>
    /// Closed is the right default once a set is picked, but on a first run it
    /// hides the only thing there is to do — the page offers a roll button that
    /// cannot roll and a collapsed row people were not noticing.
    ///
    /// Two guards, and both matter. It runs at most once per launch, so someone
    /// who deliberately closes it is not overruled every time they come back to
    /// the page. And it only fires with nothing picked, so it stops happening
    /// the moment the wheel has kits on it.
    /// </remarks>
    private void OpenKitListIfNothingPicked()
    {
        if (_kitListAutoOpened) return;

        _kitListAutoOpened = true;

        if (_roster.Selected.Count > 0) return;

        SetKitListOpen(true);
    }

    /// <summary>
    /// The picked kits, as one line for the closed header.
    /// </summary>
    /// <remarks>
    /// Names rather than a count, because the count is already on the right and
    /// "9 selected" does not answer the question anyone actually has when the
    /// list is shut, which is <em>which</em> nine.
    /// </remarks>
    private static string SelectionSummary(IReadOnlyList<string> chosen, int maxChars = 70)
    {
        if (chosen.Count == 0) return "Nothing picked yet";

        string joined = string.Join(", ", chosen);

        if (joined.Length <= maxChars) return joined;

        // Trimmed on a name boundary; a summary cut mid-word reads as a bug.
        var kept = new List<string>();
        int used = 0;

        foreach (string kit in chosen)
        {
            if (used + kit.Length + 2 > maxChars) break;

            kept.Add(kit);
            used += kit.Length + 2;
        }

        int hidden = chosen.Count - kept.Count;

        return kept.Count == 0
            ? $"{chosen.Count} kits picked"
            : string.Join(", ", kept) + $" +{hidden} more";
    }

    // ---- the run ----

    private void ResetRun_Click(object sender, RoutedEventArgs e)
    {
        if (_rolling) return;

        _rolled.Clear();

        RollBadgeText.Text = "KIT ROLL";
        RollHeadlineText.Text = "READY?";
        RollHeadlineText.FontSize = HeadlineSize;
        RollSubText.Text = "Press roll to begin";
        ReelStack.Visibility = Visibility.Collapsed;
        RollHeadlineText.Visibility = Visibility.Visible;
        ReelBar.Value = 0;
        ReelBar.Visibility = Visibility.Hidden;

        RefreshKitWheel();
    }

    /// <summary>Repaints the roster, the counters and the progress bar.</summary>
    private void RefreshKitWheel()
    {
        if (KitList == null) return;

        List<string> chosen = _roster.Selected;

        // Only the matches are shown, but every count below still speaks for the
        // whole roster — a search narrows the list, not the wheel.
        List<string> shown = ShownKits();

        KitList.ItemsSource = shown
            .Select(k =>
            {
                ImageSource? art = LoadKitImage(k);
                bool picked = chosen.Any(s => s.Equals(k, StringComparison.OrdinalIgnoreCase));
                bool played = _rolled.Any(r => r.Equals(k, StringComparison.OrdinalIgnoreCase));

                return new KitEntry(
                    k,
                    picked,
                    played ? 0.4 : 1.0,
                    art,
                    art == null ? Visibility.Visible : Visibility.Collapsed,
                    // Picked tiles are outlined and their names lit. Selection has
                    // to be readable at a glance across a hundred tiles, which a
                    // switch on each one was not.
                    picked ? PickedEdge : PlainEdge,
                    picked ? PickedInk : PlainInk,
                    played ? $"{k} — already rolled this run"
                           : picked ? $"{k} — on the wheel. Click to remove, right-click for a picture."
                                    : $"{k} — click to add. Right-click for a picture.",
                    Shipped.Contains(k) ? Visibility.Collapsed : Visibility.Visible);
            })
            .ToList();

        WheelList.ItemsSource = _roster.Presets
            .Select(p => new PresetEntry(
                p.Name,
                p.Kits.Count == 1 ? "1 kit" : $"{p.Kits.Count} kits",
                $"Load {p.Name} — {string.Join(", ", p.Kits.Take(8))}"
                + (p.Kits.Count > 8 ? $" +{p.Kits.Count - 8} more" : "")))
            .ToList();

        WheelEmptyText.Visibility =
            _roster.Presets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        int left = KitWheel.Remaining(chosen, _rolled).Count;

        KitCountText.Text = $"{chosen.Count} selected";
        KitSelectionText.Text = SelectionSummary(chosen);
        KitsRemainingText.Text = $"{left} KIT{(left == 1 ? "" : "S")} REMAINING";
        // An empty roster and a search that found nothing look identical on
        // screen but mean opposite things — one needs a kit added, the other
        // needs the search changed — so they do not share a message.
        KitEmptyText.Text = _roster.Kits.Count == 0
            ? "No kits on the roster. Type one above and press Add kit."
            : $"No kit matches “{_kitSearch.Trim()}”.";

        KitEmptyText.Visibility =
            shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // The buttons say which they will act on, because either meaning is a
        // reasonable guess and guessing wrong on Clear loses a selection.
        SelectAllKitsButton.Content = Searching ? "Select matches" : "Select all";
        ClearKitsButton.Content = Searching ? "Clear matches" : "Clear";
        ClearKitSearchButton.Visibility = Searching ? Visibility.Visible : Visibility.Collapsed;

        ChallengeProgressText.Text = $"{_rolled.Count} / {chosen.Count}";
        ChallengeProgressBar.Value = KitWheel.Progress(chosen.Count, _rolled.Count);

        bool finished = KitWheel.IsComplete(chosen, _rolled);

        RollButton.IsEnabled = KitWheel.CanSpin(chosen) && !finished && !_rolling;
        ResetRunButton.IsEnabled = _rolled.Count > 0 && !_rolling;

        if (_rolling) return;

        if (finished)
        {
            RollBadgeText.Text = "RUN COMPLETE";
            RollHeadlineText.Text = "ALL DONE";
            RollHeadlineText.FontSize = HeadlineSize;
            RollSubText.Text = $"All {chosen.Count} of your picked kits have been played";
        }
        else if (chosen.Count == 0)
        {
            // What a fresh install opens on. "READY?" is a lie when nothing is
            // picked — there is nothing to roll and no hint of what to do
            // about it, so the panel asks for the one thing it needs instead.
            RollBadgeText.Text = "GET STARTED";
            RollHeadlineText.Text = "Choose Your Kits For the Wheel";
            RollHeadlineText.FontSize = FirstRunHeadlineSize;
            RollSubText.Text = "Open the list below and pick the ones you want";
        }
        else if (!KitWheel.CanSpin(chosen))
        {
            RollBadgeText.Text = "KIT ROLL";
            RollHeadlineText.Text = "READY?";
            RollHeadlineText.FontSize = HeadlineSize;
            RollSubText.Text = $"One more — a wheel needs at least {KitWheel.MinKits}";
        }
        else
        {
            RollHeadlineText.FontSize = HeadlineSize;
        }
    }

    private void RollKit_Click(object sender, RoutedEventArgs e)
    {
        if (_rolling) return;

        string? winner = KitWheel.Roll(_roster.Selected, _rolled, Random.Shared.Next);
        if (winner == null) return;

        // Decided before the flicker starts, so the names going past are
        // decoration and cannot change the outcome.
        List<string> pool = KitWheel.Remaining(_roster.Selected, _rolled);

        _rolling = true;
        RollButton.IsEnabled = false;
        ResetRunButton.IsEnabled = false;

        RollBadgeText.Text = "ROLLING";
        RollSubText.Text = "";

        // The reel is built up front and ends on the winner, so the last thing
        // shown is the result rather than a coincidence of where the timer
        // stopped. Everything before it is scenery drawn from the same pool.
        var reel = new List<string>();
        for (int i = 0; i < ReelLength; i++) reel.Add(pool[Random.Shared.Next(pool.Count)]);
        reel.Add(winner);

        // Hidden rather than collapsed when idle, so showing it does not shift
        // the name and picture upward the moment a roll begins.
        ReelBar.Value = 0;
        ReelBar.Visibility = Visibility.Visible;
        ReelStack.Visibility = Visibility.Visible;
        RollHeadlineText.Visibility = Visibility.Collapsed;

        int at = 0;
        var ticker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ReelFastMs) };

        ticker.Tick += (_, _) =>
        {
            if (at >= reel.Count)
            {
                ticker.Stop();
                Settle(winner);
                return;
            }

            ShowPassingKit(reel[at], ticker.Interval.TotalMilliseconds);

            // Cubed, so it holds its speed for most of the reel and then loses
            // it quickly at the end — a linear slowdown feels like dragging.
            double t = (at + 1) / (double)reel.Count;
            double eased = 1 - Math.Pow(1 - t, 3);

            // Filled by how far along the reel is, not by time. The reel slows
            // down, so a time-based bar would race ahead of it and sit full
            // while kits were still going past.
            ReelBar.Value = t;

            ticker.Interval = TimeSpan.FromMilliseconds(
                ReelFastMs + (ReelSlowMs - ReelFastMs) * eased);

            at++;
        };

        ticker.Start();
    }

    /// <summary>Which of the two reel layers is currently in front.</summary>
    private bool _reelOnA = true;

    /// <summary>
    /// Dissolves one kit into the next.
    /// </summary>
    /// <param name="gapMs">
    /// How long this kit is on screen. The dissolve is scaled to it rather than
    /// fixed: a set duration is longer than the gap at the start of the reel, so
    /// the fades stack and nothing is ever fully drawn — which is what made the
    /// fast part look muddy instead of quick.
    /// </param>
    private void ShowPassingKit(string kit, double gapMs)
    {
        // The layer that is currently invisible takes the new kit, then the two
        // trade places. Nothing is ever blank: the outgoing kit is still there
        // at full strength as the incoming one arrives over it.
        StackPanel incoming = _reelOnA ? ReelLayerB : ReelLayerA;
        StackPanel outgoing = _reelOnA ? ReelLayerA : ReelLayerB;
        Image target = _reelOnA ? ReelImageB : ReelImageA;
        TextBlock label = _reelOnA ? ReelNameB : ReelNameA;

        label.Text = kit;
        target.Source = LoadKitImage(kit);

        var span = new Duration(TimeSpan.FromMilliseconds(Math.Clamp(gapMs * 0.85, 30.0, 240.0)));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        incoming.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.0, 1.0, span) { EasingFunction = ease });
        outgoing.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1.0, 0.0, span) { EasingFunction = ease });

        _reelOnA = !_reelOnA;
    }

    /// <summary>Puts a kit on the front layer with no transition.</summary>
    private void SetReelKit(string kit)
    {
        StackPanel front = _reelOnA ? ReelLayerA : ReelLayerB;
        StackPanel back = _reelOnA ? ReelLayerB : ReelLayerA;

        (_reelOnA ? ReelNameA : ReelNameB).Text = kit;
        (_reelOnA ? ReelImageA : ReelImageB).Source = LoadKitImage(kit);

        front.BeginAnimation(OpacityProperty, null);
        back.BeginAnimation(OpacityProperty, null);
        front.Opacity = 1;
        back.Opacity = 0;
    }

    /// <summary>Lands on the rolled kit and marks it played.</summary>
    private void Settle(string winner)
    {
        _rolled.Add(winner);
        _rolling = false;

        // Refreshed first, then the result written over it: the refresh paints
        // from the roster's state, which has no idea a roll just happened.
        RefreshKitWheel();

        SetReelKit(winner);

        if (!KitWheel.IsComplete(_roster.Selected, _rolled))
        {
            RollBadgeText.Text = "KIT LOCKED IN";
            RollHeadlineText.Text = winner;
            RollSubText.Text = "Play this one, then roll again";
        }
        else
        {
            RollHeadlineText.Text = winner;
            RollSubText.Text = "That was the last one — run complete";
        }

        // A short overshoot, so the name arrives rather than merely appearing
        // once the flicker stops.
        var pop = new DoubleAnimationUsingKeyFrames();
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(0.86, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.06, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140))));
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });

        RevealScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        RevealScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }
}
