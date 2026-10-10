using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace JinxyClicker;

/// <summary>
/// Clip Studio (Pro): lists the clips saved with the clip hotkey and stitches them
/// into one music-backed montage. The capture side is the existing replay buffer;
/// this is the library + the montage button.
/// </summary>
public partial class MainWindow
{
    private bool _montageVertical;
    private string? _montageMusic;

    private void LoadClipStudio(AppSettings s)
    {
        _montageVertical = s.MontageVertical;
        _montageMusic = s.MontageMusic;
        if (MontageVerticalCheck is not null) MontageVerticalCheck.IsChecked = _montageVertical;
        UpdateMusicLabel();
    }

    private void NavClipStudio_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(NavClipStudio, PageClipStudio, "Clip Studio", "Turn your clips into a montage");
        ApplyClipStudioPro();
        RefreshClipList();
    }

    // Locks the montage controls when not Pro; the clip list stays visible either way.
    private void ApplyClipStudioPro()
    {
        bool pro = IsPro;
        if (ClipStudioLockNote is not null)
            ClipStudioLockNote.Visibility = pro ? Visibility.Collapsed : Visibility.Visible;
        if (MontageButton is not null) MontageButton.IsEnabled = pro;
        if (MusicChooseButton is not null) MusicChooseButton.IsEnabled = pro;
        if (MontageVerticalCheck is not null) MontageVerticalCheck.IsEnabled = pro;
    }

    // Shows the chosen music file name, or that there's none (a silent montage).
    private void UpdateMusicLabel()
    {
        if (MusicChosenText is null) return;
        MusicChosenText.Text = HasMusic ? Path.GetFileName(_montageMusic!) : "None (montage will be silent)";
    }

    private bool HasMusic => !string.IsNullOrEmpty(_montageMusic) && File.Exists(_montageMusic);

    private void RefreshClipList()
    {
        if (ClipList is null) return;
        var clips = ClipLibrary.List(ClipFolder);
        ClipList.Items.Clear();
        foreach (ClipInfo clip in clips)
            ClipList.Items.Add(BuildClipRow(clip));
        if (clips.Count == 0 && ClipStudioStatus is not null)
            ClipStudioStatus.Text = "No clips yet. Press your clip hotkey in-game to save one.";
    }

    private FrameworkElement BuildClipRow(ClipInfo clip)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = false };
        var name = new TextBlock
        {
            Text = $"{clip.Name}   ({clip.SavedUtc.ToLocalTime():g})",
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(name);

        var del = new Button { Content = "Delete", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(del, Dock.Right);
        del.Click += (_, _) => { ClipLibrary.Delete(clip.Path); RefreshClipList(); };
        row.Children.Add(del);

        var play = new Button { Content = "Play", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(play, Dock.Right);
        play.Click += (_, _) => OpenWithShell(clip.Path);
        row.Children.Add(play);

        return row;
    }

    private static void OpenWithShell(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }

    private void MontageVertical_Changed(object sender, RoutedEventArgs e)
    {
        _montageVertical = MontageVerticalCheck.IsChecked == true;
        _settingsDirty = true;
    }

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

    private async void MakeMontage_Click(object sender, RoutedEventArgs e)
    {
        if (!IsPro) return;

        var clips = ClipLibrary.List(ClipFolder).Select(c => c.Path).Reverse().ToList(); // oldest first
        if (clips.Count == 0) { ClipStudioStatus.Text = "No clips to montage yet."; return; }

        // Music is whatever file the user chose, or none (a silent montage).
        string? music = HasMusic ? _montageMusic : null;

        MontageButton.IsEnabled = false;
        ClipStudioStatus.Text = "Making your montage… this can take a minute.";
        try
        {
            MontageShape shape = _montageVertical ? MontageShape.Vertical : MontageShape.Landscape;
            // Montages go to their own subfolder so they are kept apart from source
            // clips and can't be re-listed, re-montaged, or deleted as if they were clips.
            string montageFolder = Path.Combine(ClipFolder, "montages");
            string? outPath = await MontageMaker.RenderAsync(clips, music, shape, montageFolder);
            ClipStudioStatus.Text = outPath != null
                ? "Montage ready: " + Path.GetFileName(outPath)
                : "Montage failed. Make sure your clips still exist and try again.";
            if (outPath != null) { OpenWithShell(outPath); RefreshClipList(); }
        }
        finally
        {
            MontageButton.IsEnabled = IsPro;
        }
    }
}
