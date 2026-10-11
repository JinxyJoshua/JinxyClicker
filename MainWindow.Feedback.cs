using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JinxyClicker;

/// <summary>
/// The in-app feedback form, present in every build. Submissions go to the
/// feedback Worker (see <see cref="FeedbackClient"/>), never GitHub; the developer
/// reads them in the DEV build's inbox. Anyone can send — free or Pro.
/// </summary>
public partial class MainWindow
{
    private FeedbackType _feedbackType = FeedbackType.Feature;
    private bool _sendingFeedback;

    private void NavFeedback_Click(object sender, RoutedEventArgs e) =>
        ShowPage(NavFeedback, PageFeedback, "Feedback", "Send the developer a suggestion, idea or bug report");

    // Only reads the sender's tag, so it is safe when it fires during load (the
    // default button's IsChecked="True") before the other controls exist.
    private void FeedbackType_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag })
            _feedbackType = Feedback.ParseType(tag);
    }

    private void FeedbackText_Changed(object sender, TextChangedEventArgs e)
    {
        if (FeedbackDetails is null || FeedbackCount is null) return;
        FeedbackCount.Text = $"{FeedbackDetails.Text.Length} / {Feedback.MaxDetails}";

        // Editing after a result clears the stale message, unless we're mid-send.
        if (!_sendingFeedback && FeedbackStatus is { Text.Length: > 0 })
            FeedbackStatus.Text = string.Empty;
    }

    private async void FeedbackSubmit_Click(object sender, RoutedEventArgs e)
    {
        if (_sendingFeedback) return;

        string summary = FeedbackSummary?.Text ?? string.Empty;
        string details = FeedbackDetails?.Text ?? string.Empty;

        string? error = Feedback.Validate(summary, details);
        if (error != null)
        {
            SetFeedbackStatus(error, ok: false);
            return;
        }

        _sendingFeedback = true;
        if (FeedbackSubmit is not null) FeedbackSubmit.IsEnabled = false;
        SetFeedbackStatus("Sending…", ok: true);

        var draft = new FeedbackDraft(_feedbackType, summary, details);
        string version = UpdateCheck.CurrentVersion?.ToString(3) ?? "unknown";

        FeedbackResult result;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            result = await FeedbackClient.SubmitAsync(draft, version, cts.Token);
        }
        finally
        {
            _sendingFeedback = false;
            if (FeedbackSubmit is not null) FeedbackSubmit.IsEnabled = true;
        }

        SetFeedbackStatus(result.Message, ok: result.Success);

        if (result.Success)
        {
            // Start the next one fresh, back on "New feature".
            if (FeedbackSummary is not null) FeedbackSummary.Text = string.Empty;
            if (FeedbackDetails is not null) FeedbackDetails.Text = string.Empty;
            if (FeedbackTypeFeature is not null) FeedbackTypeFeature.IsChecked = true;
            _feedbackType = FeedbackType.Feature;
        }
    }

    private void SetFeedbackStatus(string text, bool ok)
    {
        if (FeedbackStatus is null) return;
        FeedbackStatus.Text = text;
        FeedbackStatus.Foreground = (Brush)FindResource(ok ? "TextSoft" : "Accent");
    }
}
