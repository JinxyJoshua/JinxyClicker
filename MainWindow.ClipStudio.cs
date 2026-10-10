using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace JinxyClicker;

/// <summary>
/// Clip Studio (Pro): the clip library plus a one-button montage. Clips can be
/// picked, reordered, and trimmed before the montage is stitched. Capture itself
/// is the existing replay buffer.
/// </summary>
public partial class MainWindow
{
    private bool _montageVertical;
    private string? _montageMusic;

    // One editable entry per clip: whether it's in the montage, and its trim window.
    // Order in this list IS the montage order (top to bottom).
    private sealed class EditClip
    {
        public ClipInfo Info;
        public bool Include = true;
        public double? Start;
        public double? End;
    }

    private readonly List<EditClip> _editClips = new();

    private void LoadClipStudio(AppSettings s)
    {
        _montageVertical = s.MontageVertical;
        _montageMusic = s.MontageMusic;
        if (MontageVerticalCheck is not null) MontageVerticalCheck.IsChecked = _montageVertical;
        UpdateMusicLabel();
    }

    private void NavClipStudio_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(NavClipStudio, PageClipStudio, "Clip Studio", "Edit your clips into a montage");
        ApplyClipStudioPro();
        RefreshClipList();
    }

    private void ApplyClipStudioPro()
    {
        bool pro = IsPro;
        if (ClipStudioLockNote is not null)
            ClipStudioLockNote.Visibility = pro ? Visibility.Collapsed : Visibility.Visible;
        if (MontageButton is not null) MontageButton.IsEnabled = pro;
        if (MusicChooseButton is not null) MusicChooseButton.IsEnabled = pro;
        if (MontageVerticalCheck is not null) MontageVerticalCheck.IsEnabled = pro;
    }

    // ---- music ----

    private void UpdateMusicLabel()
    {
        if (MusicChosenText is null) return;
        MusicChosenText.Text = HasMusic ? Path.GetFileName(_montageMusic!) : "None (montage will be silent)";
    }

    private bool HasMusic => !string.IsNullOrEmpty(_montageMusic) && File.Exists(_montageMusic);

    private void MontagePickMusic_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Audio (*.mp3;*.wav;*.m4a)|*.mp3;*.wav;*.m4a" };
        if (dlg.ShowDialog() == true)
        {
            _montageMusic = dlg.FileName;
            _settingsDirty = true;
            UpdateMusicLabel();
        }
    }

    private void MontageVertical_Changed(object sender, RoutedEventArgs e)
    {
        _montageVertical = MontageVerticalCheck.IsChecked == true;
        _settingsDirty = true;
    }

    // ---- clip library (pick / reorder / trim) ----

    // Syncs the editable list with what's on disk, keeping existing order, picks,
    // and trims; new clips are appended (oldest-first), deleted ones drop out.
    private void RefreshClipList()
    {
        if (ClipList is null) return;

        var onDisk = ClipLibrary.List(ClipFolder);               // newest first
        var present = new HashSet<string>(onDisk.Select(c => c.Path));
        _editClips.RemoveAll(c => !present.Contains(c.Info.Path));

        var tracked = new HashSet<string>(_editClips.Select(c => c.Info.Path));
        foreach (ClipInfo clip in Enumerable.Reverse(onDisk.ToList())) // oldest first
            if (!tracked.Contains(clip.Path))
                _editClips.Add(new EditClip { Info = clip });

        ClipList.Items.Clear();
        foreach (EditClip ec in _editClips) ClipList.Items.Add(BuildClipRow(ec));

        if (_editClips.Count == 0 && ClipStudioStatus is not null)
            ClipStudioStatus.Text = "No clips yet. Press your clip hotkey in-game to save one.";
    }

    private FrameworkElement BuildClipRow(EditClip ec)
    {
        var dock = new DockPanel { LastChildFill = false };

        var pick = new CheckBox { IsChecked = ec.Include, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        pick.Checked += (_, _) => ec.Include = true;
        pick.Unchecked += (_, _) => ec.Include = false;
        dock.Children.Add(pick);

        bool trimmed = ec.Start.HasValue || ec.End.HasValue;
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = ec.Info.Name, Foreground = Brush("TextBright"), FontSize = 12 });
        string sub = ec.Info.SavedUtc.ToLocalTime().ToString("g");
        if (trimmed) sub += $"      trim {Mmss(ec.Start ?? 0)}–{(ec.End.HasValue ? Mmss(ec.End.Value) : "end")}";
        info.Children.Add(new TextBlock { Text = sub, Foreground = trimmed ? Brush("Accent") : Brush("TextMuted"), FontSize = 10, Margin = new Thickness(0, 2, 0, 0) });
        dock.Children.Add(info);

        // Right-docked actions, added right-to-left.
        AddChip(dock, "Delete", (_, _) => { ClipLibrary.Delete(ec.Info.Path); RefreshClipList(); });
        AddChip(dock, "Play", (_, _) => OpenWithShell(ec.Info.Path));
        AddChip(dock, "Trim…", async (_, _) => await TrimClipAsync(ec));
        AddChip(dock, "▼", (_, _) => Move(ec, +1));
        AddChip(dock, "▲", (_, _) => Move(ec, -1));

        return new Border
        {
            Background = Brush("Panel"),
            BorderBrush = Brush("Outline"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 8),
            Child = dock
        };
    }

    private Brush Brush(string key) => (Brush)FindResource(key);

    private void AddChip(DockPanel row, string content, RoutedEventHandler onClick)
    {
        var b = new Button { Content = content, Style = (Style)FindResource("ChipButton"), Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(b, Dock.Right);
        b.Click += onClick;
        row.Children.Add(b);
    }

    private void Move(EditClip ec, int delta)
    {
        int i = _editClips.IndexOf(ec);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= _editClips.Count) return;
        (_editClips[i], _editClips[j]) = (_editClips[j], _editClips[i]);
        ClipList.Items.Clear();
        foreach (EditClip e in _editClips) ClipList.Items.Add(BuildClipRow(e));
    }

    private static string Mmss(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
    }

    private async System.Threading.Tasks.Task TrimClipAsync(EditClip ec)
    {
        ClipStudioStatus.Text = "Reading clip…";
        double dur = await MontageMaker.ProbeDurationAsync(ec.Info.Path);
        ClipStudioStatus.Text = "";
        if (dur <= 0) { ClipStudioStatus.Text = "Couldn't read that clip."; return; }

        double startSec = Math.Clamp(ec.Start ?? 0, 0, dur);
        double endSec = Math.Clamp(ec.End ?? dur, 0, dur);

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = $"Trim — {ec.Info.Name}", FontWeight = FontWeights.Bold, FontSize = 15, Foreground = Brush("TextBright") });
        panel.Children.Add(new TextBlock
        {
            Text = "Drag the bar to move through the clip, then click “Set start here” and “Set end here” to mark the part you want to keep.",
            Foreground = Brush("TextMuted"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 12)
        });

        var media = new MediaElement
        {
            Source = new Uri(ec.Info.Path),
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
            Stretch = Stretch.Uniform,
            Height = 230,
            Volume = 0,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var previewNote = new TextBlock { Visibility = Visibility.Collapsed, Foreground = Brush("TextMuted"), FontSize = 11, Margin = new Thickness(0, 0, 0, 8) };
        media.MediaFailed += (_, _) =>
        {
            media.Visibility = Visibility.Collapsed;
            previewNote.Text = "(Preview won't play for this clip — use the bar and the times below to trim.)";
            previewNote.Visibility = Visibility.Visible;
        };
        panel.Children.Add(new Border
        {
            Background = Brushes.Black,
            BorderBrush = Brush("Outline"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 10),
            Child = media
        });
        panel.Children.Add(previewNote);

        bool playing = false, syncing = false;

        // The playhead bar: one slider for the whole clip.
        var bar = new Slider { Minimum = 0, Maximum = dur, Value = startSec };
        var timeLabel = new TextBlock { Foreground = Brush("TextMuted"), FontSize = 11, Margin = new Thickness(0, 2, 0, 8) };
        void UpdateTime() => timeLabel.Text = $"Position {Mmss(bar.Value)} / {Mmss(dur)}";

        var nudge = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        nudge.Tick += (_, _) => { nudge.Stop(); if (!playing) media.Pause(); };
        void ShowFrame(double pos)
        {
            try { media.Position = TimeSpan.FromSeconds(Math.Clamp(pos, 0, dur)); media.Play(); nudge.Stop(); nudge.Start(); } catch { }
        }

        bar.ValueChanged += (_, _) =>
        {
            UpdateTime();
            if (syncing) return;                 // the play timer moved it, not the user
            if (playing) { try { media.Position = TimeSpan.FromSeconds(bar.Value); } catch { } }
            else ShowFrame(bar.Value);
        };
        UpdateTime();

        var playBtn = new Button { Content = "▶ Play", Style = (Style)FindResource("ChipButton"), Padding = new Thickness(14, 5, 14, 5), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
        var playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        void StopPlaying() { playing = false; playTimer.Stop(); try { media.Pause(); } catch { } media.Volume = 0; playBtn.Content = "▶ Play"; }
        playTimer.Tick += (_, _) =>
        {
            double pos = media.Position.TotalSeconds;
            if (pos >= dur - 0.1) { StopPlaying(); return; }
            syncing = true; bar.Value = pos; syncing = false;
        };
        playBtn.Click += (_, _) =>
        {
            if (playing) { StopPlaying(); return; }
            try { media.Volume = 0.6; media.Position = TimeSpan.FromSeconds(bar.Value); media.Play(); playing = true; playTimer.Start(); playBtn.Content = "⏸ Pause"; } catch { }
        };

        panel.Children.Add(bar);
        panel.Children.Add(timeLabel);
        panel.Children.Add(playBtn);

        // Set start / set end at the current playhead.
        var selLabel = new TextBlock { FontWeight = FontWeights.Bold, FontSize = 13, Foreground = Brush("TextBright"), Margin = new Thickness(0, 6, 0, 12) };
        void UpdateSel() => selLabel.Text = $"Keeping {Mmss(startSec)} → {Mmss(endSec)}   ({Mmss(Math.Max(0, endSec - startSec))} long)";

        var setStart = new Button { Content = "⮜ Set start here", Style = (Style)FindResource("ChipButton"), Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        var setEnd = new Button { Content = "Set end here ⮞", Style = (Style)FindResource("ChipButton"), Padding = new Thickness(14, 6, 14, 6) };
        setStart.Click += (_, _) => { startSec = bar.Value; if (startSec > endSec) endSec = startSec; UpdateSel(); };
        setEnd.Click += (_, _) => { endSec = bar.Value; if (endSec < startSec) startSec = endSec; UpdateSel(); };
        var setRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
        setRow.Children.Add(setStart);
        setRow.Children.Add(setEnd);
        panel.Children.Add(setRow);

        UpdateSel();
        panel.Children.Add(selLabel);

        var reset = new Button { Content = "Use whole clip", Style = (Style)FindResource("ChipButton"), Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = "Save trim", Style = (Style)FindResource("AccentButton"), Padding = new Thickness(18, 0, 18, 0), Height = 34, FontWeight = FontWeights.Bold };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(reset);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);

        var win = new Window
        {
            Title = "Trim clip",
            Content = panel,
            Background = Brush("Sidebar"),
            Width = 470,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };
        win.Loaded += (_, _) => ShowFrame(bar.Value);
        win.Closed += (_, _) => { try { playTimer.Stop(); nudge.Stop(); media.Close(); } catch { } };
        ok.Click += (_, _) =>
        {
            bool whole = startSec <= 0.05 && endSec >= dur - 0.05;
            ec.Start = whole ? null : startSec;
            ec.End = whole ? null : endSec;
            win.DialogResult = true;
        };
        reset.Click += (_, _) => { ec.Start = null; ec.End = null; win.DialogResult = true; };

        if (win.ShowDialog() == true) RefreshClipList();
    }

    private static void OpenWithShell(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }

    // ---- montage ----

    private async void MakeMontage_Click(object sender, RoutedEventArgs e)
    {
        if (!IsPro) return;

        var chosen = _editClips
            .Where(c => c.Include)
            .Select(c => new MontageClip(c.Info.Path, c.Start, c.End))
            .ToList();

        if (chosen.Count == 0) { ClipStudioStatus.Text = "Tick at least one clip to put in the montage."; return; }

        string? music = HasMusic ? _montageMusic : null;

        MontageButton.IsEnabled = false;
        ClipStudioStatus.Text = "Making your montage… this can take a minute.";
        try
        {
            MontageShape shape = _montageVertical ? MontageShape.Vertical : MontageShape.Landscape;
            // Montages go to their own subfolder so they're kept apart from clips.
            string montageFolder = Path.Combine(ClipFolder, "montages");
            string? outPath = await MontageMaker.RenderAsync(chosen, music, shape, montageFolder);
            ClipStudioStatus.Text = outPath != null
                ? "Montage ready: " + Path.GetFileName(outPath)
                : "Montage failed. Make sure your clips still exist and try again.";
            if (outPath != null) OpenWithShell(outPath);
        }
        finally
        {
            MontageButton.IsEnabled = IsPro;
        }
    }
}
