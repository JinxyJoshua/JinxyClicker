using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace JinxyClicker;

/// <summary>
/// Recorder quality and file options: a picture-quality tilt on the bitrate and a
/// "sort by app" switch that drops clips into a per-game subfolder.
/// </summary>
public partial class MainWindow
{
    private string _recordQuality = "balanced";
    private bool _recordSortByApp;
    private bool _applyingRecordQuality;

    private void LoadRecordQuality(AppSettings s)
    {
        _recordQuality = s.RecordPictureQuality is "light" or "high" ? s.RecordPictureQuality : "balanced";
        _recordSortByApp = s.RecordSortByApp;
        ApplyPictureQuality();

        _applyingRecordQuality = true;
        if (QualityLight != null) QualityLight.IsChecked = _recordQuality == "light";
        if (QualityBalanced != null) QualityBalanced.IsChecked = _recordQuality == "balanced";
        if (QualityHigh != null) QualityHigh.IsChecked = _recordQuality == "high";
        if (RecordSortByAppCheck != null) RecordSortByAppCheck.IsChecked = _recordSortByApp;
        _applyingRecordQuality = false;
    }

    private void ApplyPictureQuality()
    {
        CaptureBackend.QualityMultiplier = _recordQuality switch
        {
            "light" => 0.65,
            "high" => 1.5,
            _ => 1.0
        };
    }

    private void RecordQuality_Checked(object sender, RoutedEventArgs e)
    {
        if (_applyingRecordQuality) return;
        if (sender is RadioButton { Tag: string tag }) _recordQuality = tag;
        ApplyPictureQuality();
        SaveAppSettings();
    }

    private void RecordSortByApp_Changed(object sender, RoutedEventArgs e)
    {
        if (_applyingRecordQuality) return;
        _recordSortByApp = RecordSortByAppCheck?.IsChecked == true;
        SaveAppSettings();
    }

    /// <summary>The clip folder, with a per-app subfolder when Sort by app is on.</summary>
    private string EffectiveClipFolder()
    {
        if (!_recordSortByApp) return ClipFolder;
        try { return Path.Combine(ClipFolder, ForegroundApp.Name()); }
        catch { return ClipFolder; }
    }
}
