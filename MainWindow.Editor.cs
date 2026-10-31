using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
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
        public double Full;          // the file's length
        public double In;            // trim start
        public double Out;           // trim end
        public double Speed = 1.0;   // playback speed (2 = twice as fast)
        public string Caption = "";  // burned-in caption, blank for none
        public double? MarkA;        // A/B cut markers (source seconds), transient
        public double? MarkB;
        public double Length => Math.Max(0, Out - In);       // trimmed length on the source
        public double PlayLength => Length / (Speed > 0 ? Speed : 1.0);   // length on screen after speed

        public EditorClip Clone() => new()
        {
            Path = Path, Name = Name, Full = Full, In = In, Out = Out,
            Speed = Speed, Caption = Caption, MarkA = MarkA, MarkB = MarkB
        };
    }

    private readonly List<EditorClip> _edit = new();
    private int _editSel = -1;
    private DispatcherTimer? _editTimer;
    private bool _editScrubbing;
    private bool _editPlayAll;
    private bool _editSyncing;   // suppresses control handlers while we populate them
    private double _editPlayhead; // our own source-time playhead (MediaElement.Position lags when paused)
    private MontageShape _editShape = MontageShape.Landscape;
    private string? _editMusic;

    // Undo/redo: each entry is a full snapshot of the track and the selection.
    private readonly Stack<(List<EditorClip> Clips, int Sel)> _editUndo = new();
    private readonly Stack<(List<EditorClip> Clips, int Sel)> _editRedo = new();

    private const double EditorPxPerSec = 14;
    private const double EditorFrame = 1.0 / 30.0;   // montage normalises to 30fps, so a "frame" is 1/30s

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

        PushUndo();
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
            EditorPreview.SpeedRatio = c.Speed > 0 ? c.Speed : 1.0;
            EditorPreview.Position = TimeSpan.FromSeconds(c.In);
            EditorPreview.Pause();
            _editPlayhead = c.In;
        }
        catch { }

        if (EditorPlayButton != null) EditorPlayButton.Content = "Play";
        UpdateCutControls();
        UpdateClipControls();
        HighlightEditTimeline();
        RefreshEditorButtons();
    }

    /// <summary>Loads the selected clip's speed and caption into their controls.</summary>
    private void UpdateClipControls()
    {
        if (_editSel < 0) return;
        EditorClip c = _edit[_editSel];
        _editSyncing = true;
        try
        {
            if (EditorSpeed != null) EditorSpeed.Value = c.Speed;
            if (EditorSpeedText != null) EditorSpeedText.Text = c.Speed.ToString("0.0#", CultureInfo.CurrentCulture) + "×";
            if (EditorCaptionBox != null) EditorCaptionBox.Text = c.Caption;
        }
        finally { _editSyncing = false; }
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
        _editPlayhead = pos;   // track the live position while playing

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
        _editPlayhead = c.In + e.NewValue;
        EditorPreview.Position = TimeSpan.FromSeconds(_editPlayhead);
    }

    private void EditorSeek_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e) => _editScrubbing = true;
    private void EditorSeek_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) => _editScrubbing = false;

    // ---- A/B cut (CapCut-style: mark two points, delete what's between) ----

    private void EditorSetA_Click(object sender, RoutedEventArgs e) => SetMark(isA: true);
    private void EditorSetB_Click(object sender, RoutedEventArgs e) => SetMark(isA: false);

    private void SetMark(bool isA)
    {
        if (_editSel < 0) return;
        EditorClip c = _edit[_editSel];
        double pos = Math.Clamp(_editPlayhead, c.In, c.Out);
        if (isA) c.MarkA = pos; else c.MarkB = pos;
        UpdateCutControls();
        RefreshEditorButtons();
    }

    private void EditorClearAB_Click(object sender, RoutedEventArgs e)
    {
        if (_editSel < 0) return;
        _edit[_editSel].MarkA = null;
        _edit[_editSel].MarkB = null;
        UpdateCutControls();
        RefreshEditorButtons();
    }

    // ---- frame-step + fine marker control (precision) ----

    private void EditorFramePrev_Click(object sender, RoutedEventArgs e) => StepFrame(-1);
    private void EditorFrameNext_Click(object sender, RoutedEventArgs e) => StepFrame(+1);

    /// <summary>Pauses and nudges the playhead one frame, clamped to the clip's trim.</summary>
    private void StepFrame(int dir)
    {
        if (_editSel < 0) return;
        EditorClip c = _edit[_editSel];
        _editPlayAll = false;
        try { EditorPreview.Pause(); } catch { }
        if (EditorPlayButton != null) EditorPlayButton.Content = "Play";

        double pos = Math.Clamp(_editPlayhead + dir * EditorFrame, c.In, c.Out);
        _editPlayhead = pos;
        try { EditorPreview.Position = TimeSpan.FromSeconds(pos); } catch { }
        SyncSeekToPosition(pos);
    }

    /// <summary>Updates the seek bar and time readout to a paused position.</summary>
    private void SyncSeekToPosition(double pos)
    {
        if (_editSel < 0) return;
        EditorClip c = _edit[_editSel];
        if (EditorSeek != null && c.Length > 0)
        {
            EditorSeek.Maximum = c.Length;
            EditorSeek.Value = Math.Clamp(pos - c.In, 0, c.Length);
        }
        if (EditorTimeText != null) EditorTimeText.Text = $"{Fmt(pos - c.In)} / {Fmt(c.Length)}";
    }

    private void EditorNudge_Click(object sender, RoutedEventArgs e)
    {
        if (_editSel < 0 || sender is not FrameworkElement { Tag: string tag } || tag.Length < 2) return;
        bool isA = tag[0] == 'A';
        int dir = tag[1] == '+' ? 1 : -1;
        NudgeMark(isA, dir * EditorFrame);
    }

    private void NudgeMark(bool isA, double delta)
    {
        EditorClip c = _edit[_editSel];
        double? cur = isA ? c.MarkA : c.MarkB;
        double baseVal = cur ?? Math.Clamp(_editPlayhead, c.In, c.Out);
        double v = Math.Clamp(baseVal + delta, c.In, c.Out);
        if (isA) c.MarkA = v; else c.MarkB = v;
        _editPlayhead = v;
        try { EditorPreview.Pause(); EditorPreview.Position = TimeSpan.FromSeconds(v); } catch { }
        if (EditorPlayButton != null) EditorPlayButton.Content = "Play";
        SyncSeekToPosition(v);
        UpdateCutControls();
        RefreshEditorButtons();
    }

    private void EditorMarkBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) CommitMarkBox(sender as TextBox);
    }

    private void EditorMarkBox_LostFocus(object sender, RoutedEventArgs e) => CommitMarkBox(sender as TextBox);

    /// <summary>Parses a typed time (seconds from the clip start) into the A or B marker.</summary>
    private void CommitMarkBox(TextBox? box)
    {
        if (_editSyncing || box == null || _editSel < 0) return;
        EditorClip c = _edit[_editSel];
        bool isA = ReferenceEquals(box, EditorABoxA);

        if (string.IsNullOrWhiteSpace(box.Text))
        {
            if (isA) c.MarkA = null; else c.MarkB = null;
            UpdateCutControls();
            RefreshEditorButtons();
            return;
        }

        if (double.TryParse(box.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.CurrentCulture, out double rel) ||
            double.TryParse(box.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out rel))
        {
            double v = Math.Clamp(c.In + rel, c.In, c.Out);
            if (isA) c.MarkA = v; else c.MarkB = v;
            _editPlayhead = v;
            try { EditorPreview.Pause(); EditorPreview.Position = TimeSpan.FromSeconds(v); } catch { }
            if (EditorPlayButton != null) EditorPlayButton.Content = "Play";
            SyncSeekToPosition(v);
        }
        UpdateCutControls();
        RefreshEditorButtons();
    }

    /// <summary>
    /// Removes everything between A and B from the selected clip. With a chunk left on
    /// both sides the clip splits in two (a ripple cut, no gap); with only one side the
    /// clip is trimmed at that edge. Speed and caption carry to the new right piece.
    /// </summary>
    private void EditorDeleteMiddle_Click(object sender, RoutedEventArgs e)
    {
        if (_editSel < 0) return;
        EditorClip c = _edit[_editSel];
        if (c.MarkA is not double ma || c.MarkB is not double mb) return;

        double a = Math.Clamp(Math.Min(ma, mb), c.In, c.Out);
        double b = Math.Clamp(Math.Max(ma, mb), c.In, c.Out);
        if (b - a < 0.05) return;   // nothing meaningful between the marks

        double origOut = c.Out;
        bool leftKept = a - c.In > 0.05;
        bool rightKept = origOut - b > 0.05;
        if (!leftKept && !rightKept) return;   // marks span the whole clip; use Remove

        PushUndo();
        c.MarkA = null; c.MarkB = null;

        if (leftKept && rightKept)
        {
            var right = new EditorClip
            {
                Path = c.Path, Name = c.Name, Full = c.Full, In = b, Out = origOut,
                Speed = c.Speed, Caption = c.Caption
            };
            c.Out = a;
            _edit.Insert(_editSel + 1, right);
        }
        else if (rightKept) c.In = b;    // only the tail survives — trims the head
        else c.Out = a;                  // only the head survives — trims the tail

        BuildEditorTimeline();
        SelectEditClip(_editSel);
        try { EditorPreview.Position = TimeSpan.FromSeconds(_edit[_editSel].Out); } catch { }
        RefreshEditorButtons();
    }

    /// <summary>Refreshes the A/B readout, seek range and marker strip for the selected clip.</summary>
    private void UpdateCutControls()
    {
        if (EditorSeek != null && _editSel >= 0 && _edit[_editSel].Length > 0)
            EditorSeek.Maximum = _edit[_editSel].Length;

        _editSyncing = true;
        try
        {
            if (_editSel < 0)
            {
                if (EditorCutText != null) EditorCutText.Text = "Select a clip, then mark A and B";
                if (EditorABText != null) EditorABText.Text = "";
                if (EditorABoxA != null) EditorABoxA.Text = "";
                if (EditorABoxB != null) EditorABoxB.Text = "";
                DrawMarkers();
                return;
            }

            EditorClip cc = _edit[_editSel];
            if (EditorABoxA != null) EditorABoxA.Text = cc.MarkA is double xa ? (xa - cc.In).ToString("0.00", CultureInfo.CurrentCulture) : "";
            if (EditorABoxB != null) EditorABoxB.Text = cc.MarkB is double xb ? (xb - cc.In).ToString("0.00", CultureInfo.CurrentCulture) : "";
        }
        finally { _editSyncing = false; }

        EditorClip c = _edit[_editSel];
        string a = c.MarkA is double va ? Fmt(va - c.In) + "s" : "—";
        string b = c.MarkB is double vb ? Fmt(vb - c.In) + "s" : "—";
        if (EditorABText != null) EditorABText.Text = $"A {a}   B {b}";

        if (EditorCutText != null)
        {
            if (c.MarkA is double x && c.MarkB is double y)
            {
                double cut = Math.Abs(y - x);
                EditorCutText.Text = $"Cuts {Fmt(cut)}s  ·  leaves {Fmt(Math.Max(0, c.Length - cut))}s";
            }
            else EditorCutText.Text = "Mark A and B on the preview, then delete between them";
        }
        DrawMarkers();
    }

    private void EditorMarkerStrip_SizeChanged(object sender, SizeChangedEventArgs e) => DrawMarkers();

    /// <summary>Draws the A/B markers and the shaded cut region on the strip over the seek bar.</summary>
    private void DrawMarkers()
    {
        if (EditorMarkerStrip == null) return;
        EditorMarkerStrip.Children.Clear();

        double w = EditorMarkerStrip.ActualWidth;
        double h = EditorMarkerStrip.ActualHeight;
        if (w <= 1 || _editSel < 0) return;

        EditorClip c = _edit[_editSel];
        double len = c.Length;
        if (len <= 0) return;

        const double inset = 9;            // the slider thumb overhangs the track a little
        double usable = Math.Max(1, w - inset * 2);
        double X(double src) => inset + Math.Clamp((src - c.In) / len, 0, 1) * usable;

        if (c.MarkA is double ma && c.MarkB is double mb)
        {
            double x1 = X(Math.Min(ma, mb)), x2 = X(Math.Max(ma, mb));
            var band = new Rectangle
            {
                Width = Math.Max(1, x2 - x1), Height = h,
                Fill = new SolidColorBrush(Color.FromArgb(0x3A, 0xFF, 0x4D, 0x4D))
            };
            Canvas.SetLeft(band, x1); Canvas.SetTop(band, 0);
            EditorMarkerStrip.Children.Add(band);
        }

        AddMarkerLine(c.MarkA, X, h, Color.FromRgb(0x5A, 0xD1, 0x7A), "A");
        AddMarkerLine(c.MarkB, X, h, Color.FromRgb(0xFF, 0x6B, 0x6B), "B");
    }

    private void AddMarkerLine(double? src, Func<double, double> X, double h, Color color, string label)
    {
        if (src is not double v) return;
        double x = X(v);
        var line = new Rectangle { Width = 2, Height = h, Fill = new SolidColorBrush(color) };
        Canvas.SetLeft(line, x - 1); Canvas.SetTop(line, 0);
        EditorMarkerStrip.Children.Add(line);

        var tag = new TextBlock
        {
            Text = label, FontSize = 9, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(color)
        };
        Canvas.SetLeft(tag, Math.Max(0, x - 4)); Canvas.SetTop(tag, 0);
        EditorMarkerStrip.Children.Add(tag);
    }

    // ---- track operations ----

    private void EditorSplit_Click(object sender, RoutedEventArgs e)
    {
        if (_editSel < 0) return;
        EditorClip c = _edit[_editSel];
        double p = _editPlayhead;
        if (p <= c.In + 0.1 || p >= c.Out - 0.1) return;   // nothing meaningful to split

        PushUndo();
        var right = new EditorClip { Path = c.Path, Name = c.Name, Full = c.Full, In = p, Out = c.Out, Speed = c.Speed, Caption = c.Caption };
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
        PushUndo();
        (_edit[_editSel], _edit[j]) = (_edit[j], _edit[_editSel]);
        _editSel = j;
        BuildEditorTimeline();
        HighlightEditTimeline();
        RefreshEditorButtons();
    }

    private void EditorRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_editSel < 0) return;
        PushUndo();
        _edit.RemoveAt(_editSel);
        if (_editSel >= _edit.Count) _editSel = _edit.Count - 1;
        BuildEditorTimeline();
        if (_editSel >= 0) SelectEditClip(_editSel);
        else { try { EditorPreview.Stop(); EditorPreview.Source = null; } catch { } }
        RefreshEditorButtons();
    }

    // ---- speed / caption ----

    // One snapshot per drag, taken when the thumb is grabbed (shared by trim and speed).
    private void EditorClip_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        if (_editSel >= 0) PushUndo();
    }

    private void EditorSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_editSyncing || _editSel < 0) return;
        EditorClip c = _edit[_editSel];
        c.Speed = Math.Clamp(e.NewValue, 0.25, 3.0);
        if (EditorSpeedText != null) EditorSpeedText.Text = c.Speed.ToString("0.0#", CultureInfo.CurrentCulture) + "×";
        try { EditorPreview.SpeedRatio = c.Speed; } catch { }
        BuildEditorTimeline();   // on-screen length changed
    }

    private void EditorCaption_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_editSel >= 0) PushUndo();   // capture state before the edit begins
    }

    private void EditorCaption_Changed(object sender, TextChangedEventArgs e)
    {
        if (_editSyncing || _editSel < 0 || EditorCaptionBox == null) return;
        _edit[_editSel].Caption = EditorCaptionBox.Text ?? "";
    }

    // ---- undo / redo ----

    private List<EditorClip> SnapshotClips()
    {
        var copy = new List<EditorClip>(_edit.Count);
        foreach (EditorClip c in _edit) copy.Add(c.Clone());
        return copy;
    }

    /// <summary>Records the current track so the next change can be undone.</summary>
    private void PushUndo()
    {
        _editUndo.Push((SnapshotClips(), _editSel));
        _editRedo.Clear();
        RefreshEditorButtons();
    }

    private void EditorUndo_Click(object sender, RoutedEventArgs e)
    {
        if (_editUndo.Count == 0) return;
        _editRedo.Push((SnapshotClips(), _editSel));
        RestoreEditState(_editUndo.Pop());
    }

    private void EditorRedo_Click(object sender, RoutedEventArgs e)
    {
        if (_editRedo.Count == 0) return;
        _editUndo.Push((SnapshotClips(), _editSel));
        RestoreEditState(_editRedo.Pop());
    }

    private void RestoreEditState((List<EditorClip> Clips, int Sel) state)
    {
        _edit.Clear();
        _edit.AddRange(state.Clips);
        _editPlayAll = false;
        try { EditorPreview.Pause(); } catch { }
        if (EditorPlayButton != null) EditorPlayButton.Content = "Play";

        BuildEditorTimeline();
        if (state.Sel >= 0 && state.Sel < _edit.Count) SelectEditClip(state.Sel);
        else
        {
            _editSel = -1;
            try { EditorPreview.Stop(); EditorPreview.Source = null; } catch { }
            UpdateCutControls();
            UpdateClipControls();
        }
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

        int fw = _editShape == MontageShape.Vertical ? 1080 : 1920;
        int fh = _editShape == MontageShape.Vertical ? 1920 : 1080;

        var clips = new List<MontageClip>(_edit.Count);
        foreach (EditorClip c in _edit)
        {
            // A non-empty caption is rendered to a transparent full-frame PNG that the
            // builder overlays onto this clip for its whole length.
            string? capPath = null;
            if (!string.IsNullOrWhiteSpace(c.Caption))
            {
                string cp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"jinxy-cap-{Guid.NewGuid():N}.png");
                capPath = CaptionCard.Render(c.Caption, fw, fh, cp);
            }
            clips.Add(new MontageClip(c.Path, c.In, c.Out > 0 ? c.Out : null,
                Speed: c.Speed > 0 ? c.Speed : 1.0, CaptionImagePath: capPath));
        }

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
            // Badges: speed when not 1x, and a marker when the clip carries a caption.
            string extra = "";
            if (Math.Abs(c.Speed - 1.0) > 0.001) extra += "  " + c.Speed.ToString("0.0#", CultureInfo.CurrentCulture) + "×";
            if (!string.IsNullOrWhiteSpace(c.Caption)) extra += "  “…”";
            var len = new TextBlock
            {
                Text = Fmt(c.PlayLength) + "s" + extra, FontSize = 10, Foreground = Brush("TextMuted"),
                Margin = new Thickness(7, 2, 7, 0)
            };
            var body = new StackPanel();
            body.Children.Add(label);
            body.Children.Add(len);

            var bar = new Border
            {
                Width = Math.Max(54, c.PlayLength * EditorPxPerSec),
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
        if (EditorUndoButton != null) EditorUndoButton.IsEnabled = _editUndo.Count > 0;
        if (EditorRedoButton != null) EditorRedoButton.IsEnabled = _editRedo.Count > 0;

        if (EditorSetAButton != null) EditorSetAButton.IsEnabled = has;
        if (EditorSetBButton != null) EditorSetBButton.IsEnabled = has;
        if (EditorFramePrevButton != null) EditorFramePrevButton.IsEnabled = has;
        if (EditorFrameNextButton != null) EditorFrameNextButton.IsEnabled = has;
        if (EditorNudgeADown != null) EditorNudgeADown.IsEnabled = has;
        if (EditorNudgeAUp != null) EditorNudgeAUp.IsEnabled = has;
        if (EditorNudgeBDown != null) EditorNudgeBDown.IsEnabled = has;
        if (EditorNudgeBUp != null) EditorNudgeBUp.IsEnabled = has;
        if (EditorABoxA != null) EditorABoxA.IsEnabled = has;
        if (EditorABoxB != null) EditorABoxB.IsEnabled = has;

        bool bothMarks = has && _edit[_editSel].MarkA.HasValue && _edit[_editSel].MarkB.HasValue;
        bool anyMark = has && (_edit[_editSel].MarkA.HasValue || _edit[_editSel].MarkB.HasValue);
        if (EditorDeleteMiddleButton != null) EditorDeleteMiddleButton.IsEnabled = bothMarks;
        if (EditorClearABButton != null) EditorClearABButton.IsEnabled = anyMark;
    }

    private static string Fmt(double seconds) => Math.Max(0, seconds).ToString("0.0", CultureInfo.CurrentCulture);
}
