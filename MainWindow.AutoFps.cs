using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace JinxyClicker;

/// <summary>
/// The AUTO button on the recording framerate card.
/// </summary>
/// <remarks>
/// Kept out of MainWindow.xaml.cs because it is a self-contained piece of the
/// recorder page, and the file it would otherwise be added to is already the
/// largest in the project.
/// </remarks>
public partial class MainWindow
{
    private CancellationTokenSource? _fpsProbeCts;

    private bool IsProbingFps => _fpsProbeCts != null;

    private async void AutoFpsButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsProbingFps)
        {
            CancelFpsProbe();
            return;
        }

        string? ffmpeg = ScreenRecorder.FindFfmpeg();

        if (ffmpeg == null)
        {
            AutoFpsStatus.Text = "No recorder found, so there is nothing to measure.";
            return;
        }

        // Refused rather than working around, because the probe measures what a
        // capture costs and a capture already running is part of that cost. The
        // number would describe the two of them together.
        if (_recorder.IsRecording || _replay.IsRunning)
        {
            AutoFpsStatus.Text = "Stop recording first — a measurement has to have the machine to itself.";
            return;
        }

        await RunFpsProbeAsync(ffmpeg);
    }

    private async Task RunFpsProbeAsync(string ffmpeg)
    {
        // Held locally as well as in the field, so this run can tell whether it
        // is still the one on screen when it finishes. A probe that has been
        // superseded must not put the button back to AUTO or write its result
        // over a newer one's — which is exactly what was seen: a finishing run
        // reset the label to AUTO while the run after it was still measuring.
        var probe = new CancellationTokenSource();

        _fpsProbeCts = probe;
        CancellationToken token = probe.Token;

        bool Owns() => ReferenceEquals(_fpsProbeCts, probe);

        AutoFpsButton.Content = "STOP";

        // The monitor being captured decides both how many pixels there are and
        // how many frames there can be, so the probe has to use the one that
        // will actually be recorded rather than the primary.
        DisplayInfo? display = _captureDisplay;
        int refreshHz = display?.EffectiveRefreshHz ?? _displays.FirstOrDefault()?.EffectiveRefreshHz ?? 60;

        var progress = new Progress<int>(fps =>
        {
            if (Owns()) AutoFpsStatus.Text = $"Trying {fps} a second…";
        });

        try
        {
            FpsChoice choice = await new FpsProbe()
                .RunAsync(ffmpeg, display, refreshHz, progress, token)
                .ConfigureAwait(true);

            if (token.IsCancellationRequested || !Owns()) return;

            // Applied, not suggested. The point of measuring is to end up on the
            // right setting, and leaving it to be clicked afterwards is how it
            // goes back to being a guess.
            if (!SelectRecordFps(choice.Fps)) SelectRecordFps(AutoFps.Fallback);

            AutoFpsStatus.Text = choice.Headline + " " + choice.Detail;
        }
        catch (OperationCanceledException)
        {
            if (Owns()) AutoFpsStatus.Text = "Stopped. Nothing was changed.";
        }
        catch (Exception ex) when (Owns())
        {
            AutoFpsStatus.Text = "Could not measure: " + ex.Message;
        }
        finally
        {
            if (Owns())
            {
                _fpsProbeCts = null;
                AutoFpsButton.Content = "AUTO";
            }

            probe.Dispose();
        }
    }

    private void CancelFpsProbe()
    {
        // Left to the probe's own finally to dispose and clear: cancelling here
        // and disposing there keeps one owner for the token source.
        _fpsProbeCts?.Cancel();
        AutoFpsButton.Content = "AUTO";
    }
}
