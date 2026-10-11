#if DEVTOOLS

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JinxyClicker;

/// <summary>
/// The developer's feedback inbox — the read side of the submit form. Behind
/// DEVTOOLS, so it is compiled into no public build: the public app can only
/// send, never read. The read token is kept out of the binary the same way the
/// dev-update token is — in a file in the settings folder, never compiled in.
/// </summary>
public partial class MainWindow
{
    /// <summary>The file the feedback read token is read from, if it exists.</summary>
    public const string FeedbackDevTokenFile = "feedback-dev.token";

    private StackPanel? _inboxList;
    private TextBlock? _inboxStatus;
    private bool _loadingInbox;

    /// <summary>
    /// The Worker's read/delete secret, from the settings folder rather than
    /// compiled in — so even a dev build handed to a tester carries no credential.
    /// Set the Worker's DEV_TOKEN to the same value.
    /// </summary>
    private static string ReadFeedbackDevToken()
    {
        foreach (string path in new[]
        {
            SettingsPath.For(FeedbackDevTokenFile),
            System.IO.Path.Combine(AppContext.BaseDirectory, FeedbackDevTokenFile)
        })
        {
            try
            {
                if (!System.IO.File.Exists(path)) continue;
                string token = System.IO.File.ReadAllText(path).Trim();
                if (token.Length > 0) return token;
            }
            catch
            {
                // Unreadable is the same as absent: the inbox just says to set it.
            }
        }

        return string.Empty;
    }

    private Border BuildFeedbackInboxPanel()
    {
        var header = new DockPanel { LastChildFill = false };

        header.Children.Add(new TextBlock
        {
            Text = "FEEDBACK INBOX",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("TextMuted")
        });

        var refresh = new Button
        {
            Content = "Refresh",
            Padding = new Thickness(14, 6, 14, 6),
            MinWidth = 84
        };
        DockPanel.SetDock(refresh, Dock.Right);
        refresh.Click += (_, _) => _ = RefreshFeedbackInboxAsync();
        header.Children.Add(refresh);

        _inboxStatus = Note("Press Refresh to load what people have sent.");
        _inboxStatus.Margin = new Thickness(0, 10, 0, 0);

        _inboxList = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };

        var body = new StackPanel();
        body.Children.Add(header);
        body.Children.Add(_inboxStatus);
        body.Children.Add(_inboxList);

        return new Border
        {
            Background = (Brush)FindResource("Panel"),
            BorderBrush = (Brush)FindResource("Outline"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(18),
            Margin = new Thickness(0, 0, 0, 14),
            Child = body
        };
    }

    private async Task RefreshFeedbackInboxAsync()
    {
        if (_loadingInbox || _inboxList is null || _inboxStatus is null) return;

        string token = ReadFeedbackDevToken();
        if (token.Length == 0)
        {
            _inboxList.Children.Clear();
            _inboxStatus.Visibility = Visibility.Visible;
            _inboxStatus.Text = $"No read token. Put your Worker DEV_TOKEN in {SettingsPath.For(FeedbackDevTokenFile)} to read the inbox.";
            return;
        }

        _loadingInbox = true;
        _inboxStatus.Visibility = Visibility.Visible;
        _inboxStatus.Text = "Loading…";
        _inboxList.Children.Clear();

        (bool Ok, string? Error, IReadOnlyList<FeedbackItem> Items) result;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            result = await FeedbackClient.ListAsync(token, cts.Token);
        }
        finally
        {
            _loadingInbox = false;
        }

        if (!result.Ok)
        {
            _inboxStatus.Text = result.Error ?? "Couldn't load the inbox.";
            return;
        }

        if (result.Items.Count == 0)
        {
            _inboxStatus.Text = "Nothing yet — no one has sent anything.";
            return;
        }

        _inboxStatus.Visibility = Visibility.Collapsed;
        foreach (FeedbackItem item in result.Items)
            _inboxList.Children.Add(BuildInboxRow(item));
    }

    private Border BuildInboxRow(FeedbackItem item)
    {
        var top = new DockPanel { LastChildFill = false };

        var del = new Button
        {
            Content = "Delete",
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 11
        };
        DockPanel.SetDock(del, Dock.Right);
        del.Click += async (_, _) =>
        {
            del.IsEnabled = false;
            string token = ReadFeedbackDevToken();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            (bool ok, _) = await FeedbackClient.DeleteAsync(token, item.Id, cts.Token);
            if (ok) await RefreshFeedbackInboxAsync();
            else del.IsEnabled = true;
        };
        top.Children.Add(del);

        string when = item.Submitted == DateTimeOffset.MinValue
            ? string.Empty
            : "   ·   " + item.Submitted.ToLocalTime().ToString("MMM d, h:mm tt");

        top.Children.Add(new TextBlock
        {
            Text = $"{Feedback.TypeLabel(item.Type).ToUpperInvariant()}   ·   v{item.Version}{when}",
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("Accent")
        });

        var body = new StackPanel();
        body.Children.Add(top);
        body.Children.Add(new TextBlock
        {
            Text = item.Summary,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = (Brush)FindResource("Text")
        });

        if (item.Details.Length > 0)
        {
            TextBlock details = Note(item.Details);
            details.Margin = new Thickness(0, 4, 0, 0);
            body.Children.Add(details);
        }

        return new Border
        {
            Background = (Brush)FindResource("Sunken"),
            BorderBrush = (Brush)FindResource("Hairline"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8),
            Child = body
        };
    }
}

#endif
