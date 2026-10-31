using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace JinxyClicker;

/// <summary>
/// Stage 1 of the video editor: add clips, preview the selected one with its trim,
/// arrange a single track (reorder / split / remove), and export the whole track to
/// one MP4 through <see cref="MontageMaker"/>. A full multi-track timeline with a
/// playhead that plays across clips, text and transitions comes in later stages.
/// </summary>
public partial class MainWindow
{
    private sealed class EditorClip
    {
        public string Path = "";
        public string Name = "";
        public double Full;    // the file's length
        public double In;      // trim start
        public double Out;     // trim end
        public double Length => Math.Max(0, Out - In);
    }

    private readonly List<EditorClip> _edit = new();
    private int _editSel = -1;
    private DispatcherTimer? _editTimer;
    private bool _editScrubbing;
    private bool _editPlayAll;
    private MontageShape _editShape = MontageShape.Landscape;
    private string? _editMusic;

    private const double EditorPxPerSec = 14;

    private void NavEditor_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(NavEditor, PageEditor, "Video editor", "Trim and stitch your clips into one video");
        RefreshEditorButtons();
    }

    // ---- adding / selecting ----

    private async void EditorAdd_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Video (*.mp4;*.mov;*.mkv;*.webm)|*.mp4;*.mov;*.mkv;*.webm|All files|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        foreach (string path in dlg.FileNames)
        {
            double full = await MontageMaker.ProbeDurationAsync(path);
            if (full <= 0) full = 0;
            _edit.Add(new EditorClip { Path = path, Name = Path.GetFileName(path), Full = full, In = 0, Out = full });
        }

        BuildEditorTimeline();
        if (_editSel < 0 && _edit.Count > 0) SelectEditClip(_edit.Count - dlg.FileNames.Length);
        RefreshEditorButtons();
    }

    private void SelectEditClip(int index)
    {
        if (index < 0 || index >= _edit.Count) { _editSel = -1; return; }
        _editSel = index;
        EditorClip c = _edit[index];

        try
        {
            EditorPreview.Stop();
            EditorPreview.Source = new Uri(c.Path, UriKind.Absolute);
            EditorPreview.Position = TimeSpan.FromSeconds(c.In);
            EditorPreview.Pause();
        }
        catch { }

        if (EditorPlayButton != null) EditorPlayButton.Content = "Play";
        UpdateTrimControls();
        HighlightEditTimeline();
        RefreshEditorButtons();
    }

    // ---- preview transport ----

    private void EditorPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_editSel < 0) return;
        _editPlayAll = false;
        EnsureEditTimer();

        if (EditorPlayButton.Content as string == "Pause")
        {
            EditorPreview.Pause();
            EditorPlayButton.Content = "Play";
            return;
        }

        EditorClip c = _edit[_editSel];
        if (EditorPreview.Position.TotalSeconds < c.In || EditorPreview.Position.TotalSeconds >= c.Out - 0.05)
            EditorPreview.Position = TimeSpan.FromSeconds(c.In);
        EditorPreview.Play();
        EditorPlayButton.Content = "Pause";
    }

    private void EditorPlayAll_Click(object sender, RoutedEventArgs e)
    {
        if (_edit.Count == 0) return;
        if (_editSel < 0) SelectEditClip(0);
        _editPlayAll = true;
        EnsureEditTimer();

        EditorClip c = _edit[_editSel];
        EditorPreview.Position = TimeSpan.FromSeconds(c.In);
        EditorPreview.Play();
        if (EditorPlayButton != null) EditorPlayButton.Content = "Pause";
    }

    private void EnsureEditTimer()
    {
        if (_editTimer != null) return;
        _editTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _editTimer.Tick += (_, _) => EditTick();
        _editTimer.Start();
    }

    private void EditTick()
    {
        if (_editSel < 0 || _editScrubbing) return;
        EditorClip c = _edit[_editSel];
        double pos = EditorPreview.Position.TotalSeconds;

        // At the trim's out point: advance to the next clip in play-all, else stop.
        if (EditorPlayButton?.Content as string == "Pause" && pos >= c.Out)
        {
            if (_editPlayAll && _editSel < _edit.Count - 1)
            {
                int next = _editSel + 1;
                SelectEditClip(next);
                EditorClip n = _edit[next];
                EditorPreview.Position = TimeSpan.FromSeconds(n.In);
                EditorPreview.Play();
                if (EditorPlayButton != null) EditorPlayButton.Content = "Pause";
                return;
            }

            EditorPreview.Pause();
            EditorPreview.Position = TimeSpan.FromSeconds(c.In);
            EditorPlayButton.Content = "Play";
            _editPlayAll = false;
            pos = c.In;
        }

        if (EditorSeek != null && c.Length > 0)
        {
            double rel = Math.Clamp(pos - c.In, 0, c.Length);
            EditorSeek.Maximum = c.Length;
            EditorSeek.Value = rel;
        }
        if (EditorTimeText != null)
            EditorTimeText.Text = $"{Fmt(pos - c.In)} / {Fmt(c.Length)}";
    }

    private void EditorSeek_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_editSel < 0 || !_editScrubbing) return;
        EditorClip c = _edit[_editSel];
        EditorPreview.Position = TimeSpan.FromSeconds(c.In + e.NewValue);
    }

    private void EditorSeek_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e) => _editScrubbing = true;
    private void EditorSeek_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) => _editScrubbing = false;

    // ---- trim ----

    private void UpdateTrimControls()
    {
        if (_editSel < 0) return;
        EditorClip c = _edit[_editSel];

        if (EditorTrimIn != null) { EditorTrimIn.Maximum = Math.Max(0.1, c.Full); EditorTrimIn.Value = c.In; }
        if (EditorTrimOut != null) { EditorTrimOut.Maximum = Math.Max(0.1, c.Full); EditorTrimOut.Value = c.Out; }
        if (EditorTrimText != null) EditorTrimText.Text = $"In {Fmt(c.In)}   Out {Fmt(c.Out)}   ({Fmt(c.Length)})";
    }

    private void EditorTrimIn_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_editSel < 0) return;
        EditorClip c = _edit[_editSel];
        c.In = Math.Min(e.NewValue, c.Out - 0.1);
        if (c.In < 0) c.In = 0;
        try { EditorPreview.Position = TimeSpan.FromSeconds(c.In); } catch { }
        UpdateTrimControls();
        BuildEditorTimeline();
    }

    private void EditorTrimOut_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_editSel < 0) return;
        EditorClip c = _edit[_editSel];
        c.Out = Math.Max(e.NewValue, c.In + 0.1);
        if (c.Out > c.Full && c.Full > 0) c.Out = c.Full;
        UpdateTrimControls();
        BuildEditorTimeline();
    }

    private void EditorTrim_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) => HighlightEditTimeline();

    // ---- track operations ----

    private void EditorSplit_Click(object sender, RoutedEventArgs e)
    {
        if (_editSel < 0) return;
        EditorClip c = _edit[_editSel];
        double p = EditorPreview.Position.TotalSeconds;
        if (p <= c.In + 0.1 || p >= c.Out - 0.1) return;   // nothing meaningful to split

        var right = new EditorClip { Path = c.Path, Name = c.Name, Full = c.Full, In = p, Out = c.Out };
        c.Out = p;
        _edit.Insert(_editSel + 1, right);
        BuildEditorTimeline();
        SelectEditClip(_editSel);
    }

    private void EditorMoveLeft_Click(object sender, RoutedEventArgs e) => MoveEdit(-1);
    private void EditorMoveRight_Click(object sender, RoutedEventArgs e) => MoveEdit(1);

    private void MoveEdit(int dir)
    {
        int j = _editSel + dir;
        if (_editSel < 0 || j < 0 || j >= _edit.Count) return;
        (_edit[_editSel], _edit[j]) = (_edit[j], _edit[_editSel]);
        _editSel = j;
        BuildEditorTimeline();
        HighlightEditTimeline();
        RefreshEditorButtons();
    }

    private void EditorRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_editSel < 0) return;
        _edit.RemoveAt(_editSel);
        if (_editSel >= _edit.Count) _editSel = _edit.Count - 1;
        BuildEditorTimeline();
        if (_editSel >= 0) SelectEditClip(_editSel);
        else { try { EditorPreview.Stop(); EditorPreview.Source = null; } catch { } }
        RefreshEditorButtons();
    }

    // ---- music / shape / export ----

    private void EditorShape_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag })
            _editShape = tag == "vertical" ? MontageShape.Vertical : MontageShape.Landscape;
    }

    private void EditorChooseMusic_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Audio (*.mp3;*.m4a;*.wav)|*.mp3;*.m4a;*.wav|All files|*.*" };
        if (dlg.ShowDialog() != true) return;
        _editMusic = dlg.FileName;
        if (EditorMusicText != null) EditorMusicText.Text = Path.GetFileName(_editMusic);
    }

    private void EditorClearMusic_Click(object sender, RoutedEventArgs e)
    {
        _editMusic = null;
        if (EditorMusicText != null) EditorMusicText.Text = "No music";
    }

    private async void EditorExport_Click(object sender, RoutedEventArgs e)
    {
        if (_edit.Count == 0) { SetEditorStatus("Add a clip first."); return; }

        var clips = new List<MontageClip>(_edit.Count);
        foreach (EditorClip c in _edit)
            clips.Add(new MontageClip(c.Path, c.In, c.Out > 0 ? c.Out : null));

        // Intro title (rendered to a PNG) and crossfade — both run through MontageMaker.
        string? titlePath = null;
        string? titleText = EditorTitleBox?.Text?.Trim();
        if (!string.IsNullOrEmpty(titleText))
        {
            int tw = _editShape == MontageShape.Vertical ? 1080 : 1920;
            int th = _editShape == MontageShape.Vertical ? 1920 : 1080;
            string tp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"jinxy-title-{DateTime.Now:HHmmssfff}.png");
            titlePath = TitleCard.Render(titleText, tw, th, AccentColorValue(), tp);
        }
        bool crossfade = EditorCrossfadeCheck?.IsChecked == true;
        var options = new MontageOptions(_editShape, Crossfade: crossfade, TitleImagePath: titlePath, TitleSeconds: 2.0);

        SetEditorStatus("Exporting…");
        if (EditorExportButton != null) EditorExportButton.IsEnabled = false;

        string? outPath = await MontageMaker.RenderAsync(clips, _editMusic, options, ClipFolder);

        if (EditorExportButton != null) EditorExportButton.IsEnabled = true;
        if (outPath == null) { SetEditorStatus("Export failed — check the clips and try again."); return; }

        SetEditorStatus($"Saved {Path.GetFileName(outPath)} to your clips folder.");
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(outPath) { UseShellExecute = true }); } catch { }
    }

    private void SetEditorStatus(string text)
    {
        if (EditorStatus != null) EditorStatus.Text = text;
    }

    // ---- timeline UI ----

    private void BuildEditorTimeline()
    {
        if (EditorTimeline == null) return;
        EditorTimeline.Children.Clear();

        if (_edit.Count == 0)
        {
            EditorTimeline.Children.Add(new TextBlock
            {
                Text = "Add clips to start — they line up here.",
                Foreground = Brush("TextMuted"), FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0)
            });
            return;
        }

        for (int i = 0; i < _edit.Count; i++)
        {
            EditorClip c = _edit[i];
            int index = i;

            var label = new TextBlock
            {
                Text = c.Name, FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = Brush("TextBright"), TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(7, 6, 7, 0)
            };
            var len = new TextBlock
            {
                Text = Fmt(c.Length) + "s", FontSize = 10, Foreground = Brush("TextMuted"),
                Margin = new Thickness(7, 2, 7, 0)
            };
            var body = new StackPanel();
            body.Children.Add(label);
            body.Children.Add(len);

            var bar = new Border
            {
                Width = Math.Max(54, c.Length * EditorPxPerSec),
                Height = 58, CornerRadius = new CornerRadius(6),
                Background = Brush("Panel2"), BorderBrush = Brush("Outline"),
                BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 6, 0),
                Cursor = System.Windows.Input.Cursors.Hand, Tag = index, Child = body
            };
            bar.MouseLeftButtonUp += (_, _) => SelectEditClip(index);
            EditorTimeline.Children.Add(bar);
        }

        HighlightEditTimeline();
    }

    private void HighlightEditTimeline()
    {
        if (EditorTimeline == null) return;
        foreach (object child in EditorTimeline.Children)
            if (child is Border { Tag: int idx } b)
            {
                bool on = idx == _editSel;
                b.BorderBrush = on ? Brush("Accent") : Brush("Outline");
                b.BorderThickness = new Thickness(on ? 2 : 1);
            }
    }

    private void RefreshEditorButtons()
    {
        bool has = _editSel >= 0;
        if (EditorSplitButton != null) EditorSplitButton.IsEnabled = has;
        if (EditorRemoveButton != null) EditorRemoveButton.IsEnabled = has;
        if (EditorLeftButton != null) EditorLeftButton.IsEnabled = has && _editSel > 0;
        if (EditorRightButton != null) EditorRightButton.IsEnabled = has && _editSel < _edit.Count - 1;
        if (EditorPlayButton != null) EditorPlayButton.IsEnabled = has;
        if (EditorExportButton != null) EditorExportButton.IsEnabled = _edit.Count > 0;
    }

    private static string Fmt(double seconds) => Math.Max(0, seconds).ToString("0.0", CultureInfo.CurrentCulture);
}
