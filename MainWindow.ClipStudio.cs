using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = false };

        var pick = new CheckBox { IsChecked = ec.Include, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        pick.Checked += (_, _) => ec.Include = true;
        pick.Unchecked += (_, _) => ec.Include = false;
        row.Children.Add(pick);

        string trimNote = (ec.Start.HasValue || ec.End.HasValue)
            ? $"   [trim {Mmss(ec.Start ?? 0)}–{(ec.End.HasValue ? Mmss(ec.End.Value) : "end")}]"
            : "";
        row.Children.Add(new TextBlock
        {
            Text = $"{ec.Info.Name}   ({ec.Info.SavedUtc.ToLocalTime():g}){trimNote}",
            VerticalAlignment = VerticalAlignment.Center
        });

        // Right-docked controls, added right-to-left.
        AddRight(row, "Delete", (_, _) => { ClipLibrary.Delete(ec.Info.Path); RefreshClipList(); });
        AddRight(row, "Play", (_, _) => OpenWithShell(ec.Info.Path));
        AddRight(row, "Trim…", async (_, _) => await TrimClipAsync(ec));
        AddRight(row, "▼", (_, _) => Move(ec, +1));
        AddRight(row, "▲", (_, _) => Move(ec, -1));

        return row;
    }

    private static void AddRight(DockPanel row, string content, RoutedEventHandler onClick)
    {
        var b = new Button { Content = content, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0) };
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
        ClipStudioStatus.Text = "Reading clip length…";
        double dur = await MontageMaker.ProbeDurationAsync(ec.Info.Path);
        ClipStudioStatus.Text = "";
        if (dur <= 0) { ClipStudioStatus.Text = "Couldn't read that clip's length."; return; }

        double start = Math.Clamp(ec.Start ?? 0, 0, dur);
        double end = Math.Clamp(ec.End ?? dur, 0, dur);

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = $"Trim — {ec.Info.Name}  ({Mmss(dur)} long)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) });

        var readout = new TextBlock { Margin = new Thickness(0, 0, 0, 10) };
        var startSlider = new Slider { Minimum = 0, Maximum = dur, Value = start, Margin = new Thickness(0, 4, 0, 0) };
        var endSlider = new Slider { Minimum = 0, Maximum = dur, Value = end, Margin = new Thickness(0, 4, 0, 0) };
        void Sync() => readout.Text = $"Keep {Mmss(startSlider.Value)} → {Mmss(endSlider.Value)}";
        startSlider.ValueChanged += (_, _) => { if (startSlider.Value > endSlider.Value) startSlider.Value = endSlider.Value; Sync(); };
        endSlider.ValueChanged += (_, _) => { if (endSlider.Value < startSlider.Value) endSlider.Value = startSlider.Value; Sync(); };
        Sync();

        panel.Children.Add(new TextBlock { Text = "Start" });
        panel.Children.Add(startSlider);
        panel.Children.Add(new TextBlock { Text = "End", Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(endSlider);
        panel.Children.Add(readout);
        readout.Margin = new Thickness(0, 10, 0, 10);

        var ok = new Button { Content = "Save trim", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0) };
        var reset = new Button { Content = "Use whole clip", Padding = new Thickness(14, 4, 14, 4) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(reset);
        panel.Children.Add(buttons);

        var win = new Window
        {
            Title = "Trim clip",
            Content = panel,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };
        ok.Click += (_, _) =>
        {
            bool whole = startSlider.Value <= 0.05 && endSlider.Value >= dur - 0.05;
            ec.Start = whole ? null : startSlider.Value;
            ec.End = whole ? null : endSlider.Value;
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
