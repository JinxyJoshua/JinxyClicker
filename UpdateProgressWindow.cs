using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JinxyClicker;

/// <summary>
/// Shown while an update downloads, so accepting the prompt visibly does
/// something.
/// </summary>
/// <remarks>
/// The installer is around a hundred megabytes. Without this the app sits
/// unchanged for tens of seconds after the prompt is accepted and then closes
/// abruptly, which reads as the button having done nothing followed by a crash.
///
/// Built in code rather than as a XAML window because it exists for one moment
/// in the app's life and pulls in none of the styling the rest of the window
/// relies on — a resource dictionary lookup here would be one more thing that
/// can throw while the app is on its way out.
/// </remarks>
public sealed class UpdateProgressWindow : Window
{
    private readonly ProgressBar _bar;
    private readonly TextBlock _detail;

    public UpdateProgressWindow(string tag)
    {
        Title = "Updating";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0x14, 0x18, 0x20));

        var heading = new TextBlock
        {
            Text = $"Downloading {tag}",
            Foreground = Brushes.White,
            FontSize = 15,
            FontWeight = FontWeights.Bold
        };

        _bar = new ProgressBar { Height = 8, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 14, 0, 0) };

        _detail = new TextBlock
        {
            Text = "Starting…",
            Foreground = new SolidColorBrush(Color.FromRgb(0x8D, 0x98, 0xAA)),
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children = { heading, _bar, _detail }
        };
    }

    /// <summary>
    /// Updates the bar as bytes arrive.
    /// </summary>
    /// <param name="total">
    /// Null when the server sent no length. Shown as megabytes so far rather
    /// than as a percentage, because a bar that cannot fill is worse than one
    /// that does not pretend to.
    /// </param>
    public void Report(long written, long? total)
    {
        double mb = written / 1024.0 / 1024.0;

        if (total is > 0)
        {
            _bar.IsIndeterminate = false;
            _bar.Value = Math.Clamp(written * 100.0 / total.Value, 0, 100);
            _detail.Text = $"{mb:0.0} MB of {total.Value / 1024.0 / 1024.0:0.0} MB";
        }
        else
        {
            _bar.IsIndeterminate = true;
            _detail.Text = $"{mb:0.0} MB";
        }
    }

    /// <summary>
    /// Says what happens next, because what happens next is the app closing.
    /// </summary>
    /// <remarks>
    /// Windows shows an unsigned installer a SmartScreen warning, so the last
    /// thing on screen has to explain that a prompt is expected. Otherwise the
    /// app vanishes and a security warning appears, which is indistinguishable
    /// from something having gone wrong.
    /// </remarks>
    public void HandingOver()
    {
        _bar.IsIndeterminate = false;
        _bar.Value = 100;
        _detail.Text =
            "Starting the installer. This app will close and reopen when it finishes. "
            + "Windows may warn that the publisher is unknown — that is expected.";
    }
}
