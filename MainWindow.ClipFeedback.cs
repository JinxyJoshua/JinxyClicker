using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;

namespace JinxyClicker;

/// <summary>
/// Clip feedback: the on-screen "Clip saved" note and the sound played when a clip
/// lands. Both are optional and configurable from the Recorder page's Feedback tab.
/// </summary>
public partial class MainWindow
{
    private ClipSound? _clipSoundPlayer;

    private bool _clipNoteEnabled = true;
    private string _clipNoteText = "Clip saved";
    private int _clipNoteCorner = 1;   // 0 TL, 1 TR, 2 BL, 3 BR
    private bool _clipSoundEnabled = true;
    private string _clipSoundName = "chime";
    private string? _clipSoundFile;
    private double _clipSoundVolume = 80;
    private bool _applyingClipFeedback;

    private void LoadClipFeedback(AppSettings s)
    {
        _clipNoteEnabled = s.ClipNoteEnabled;
        _clipNoteText = string.IsNullOrWhiteSpace(s.ClipNoteText) ? "Clip saved" : s.ClipNoteText;
        _clipNoteCorner = Math.Clamp(s.ClipNoteCorner, 0, 3);
        _clipSoundEnabled = s.ClipSoundEnabled;
        _clipSoundName = s.ClipSound;
        _clipSoundFile = s.ClipSoundFile;
        _clipSoundVolume = Math.Clamp(s.ClipSoundVolume, 0, 100);

        _applyingClipFeedback = true;
        if (ClipNoteCheck != null) ClipNoteCheck.IsChecked = _clipNoteEnabled;
        if (ClipNoteTextBox != null) ClipNoteTextBox.Text = _clipNoteText;
        if (ClipNoteCornerBox != null) ClipNoteCornerBox.SelectedIndex = _clipNoteCorner;
        if (ClipSoundCheck != null) ClipSoundCheck.IsChecked = _clipSoundEnabled;
        SetClipSoundRadio(_clipSoundName);
        if (ClipSoundFileText != null) ClipSoundFileText.Text = ShortFile(_clipSoundFile);
        if (ClipVolumeSlider != null) ClipVolumeSlider.Value = _clipSoundVolume;
        _applyingClipFeedback = false;
    }

    private void SetClipSoundRadio(string name)
    {
        if (ClipSoundChime != null) ClipSoundChime.IsChecked = name == "chime";
        if (ClipSoundPop != null) ClipSoundPop.IsChecked = name == "pop";
        if (ClipSoundShutter != null) ClipSoundShutter.IsChecked = name == "shutter";
        if (ClipSoundOwn != null) ClipSoundOwn.IsChecked = name == "own";
    }

    private static string ShortFile(string? path) =>
        string.IsNullOrWhiteSpace(path) ? "No file chosen" : Path.GetFileName(path);

    private void ClipNote_Changed(object sender, RoutedEventArgs e)
    {
        if (_applyingClipFeedback) return;
        _clipNoteEnabled = ClipNoteCheck?.IsChecked == true;
        SaveAppSettings();
    }

    private void ClipNoteText_Changed(object sender, TextChangedEventArgs e)
    {
        if (_applyingClipFeedback) return;
        _clipNoteText = ClipNoteTextBox?.Text ?? "Clip saved";
        SaveAppSettings();
    }

    private void ClipNoteCorner_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingClipFeedback) return;
        _clipNoteCorner = Math.Clamp(ClipNoteCornerBox?.SelectedIndex ?? 1, 0, 3);
        SaveAppSettings();
    }

    private void ClipSound_Changed(object sender, RoutedEventArgs e)
    {
        if (_applyingClipFeedback) return;
        _clipSoundEnabled = ClipSoundCheck?.IsChecked == true;
        SaveAppSettings();
    }

    private void ClipSoundChoice_Checked(object sender, RoutedEventArgs e)
    {
        if (_applyingClipFeedback) return;
        if (sender is RadioButton { Tag: string tag }) _clipSoundName = tag;
        SaveAppSettings();
    }

    private void ClipChooseSound_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Audio (*.wav;*.mp3;*.m4a)|*.wav;*.mp3;*.m4a" };
        if (dlg.ShowDialog() != true) return;

        _clipSoundFile = dlg.FileName;
        _clipSoundName = "own";
        if (ClipSoundFileText != null) ClipSoundFileText.Text = ShortFile(_clipSoundFile);
        if (ClipSoundOwn != null) ClipSoundOwn.IsChecked = true;
        SaveAppSettings();
    }

    private void ClipVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_applyingClipFeedback) return;
        _clipSoundVolume = e.NewValue;   // saved on drag end
    }

    private void ClipVolume_DragCompleted(object sender, DragCompletedEventArgs e) => SaveAppSettings();

    private void ClipTry_Click(object sender, RoutedEventArgs e)
    {
        ShowClipNote();
        PlayClipFeedbackSound();
    }

    // ---- used when a clip actually saves ----

    private void ShowClipNote()
    {
        if (!_clipNoteEnabled) return;
        Toast.Notify(string.IsNullOrWhiteSpace(_clipNoteText) ? "Clip saved" : _clipNoteText,
            Color.FromRgb(0x5B, 0xE5, 0x8B), _captureDisplay, 2.0, _clipNoteCorner);
    }

    private void PlayClipFeedbackSound()
    {
        if (!_clipSoundEnabled) return;
        (_clipSoundPlayer ??= new ClipSound()).Play(_clipSoundName, _clipSoundFile, _clipSoundVolume / 100.0);
    }
}
