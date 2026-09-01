#if DEVTOOLS

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JinxyClicker;

/// <summary>
/// The developer panel, which exists only in a build made with DEVTOOLS.
/// </summary>
/// <remarks>
/// The whole file is behind the switch, and the panel is built in code rather
/// than written in MainWindow.xaml, because that distinction is the point.
/// XAML is compiled into the assembly whether or not it is ever shown — the
/// markup, the element names and the labels all ship, and anyone reading the
/// binary would find "DEV TOOLS" sitting in a build that supposedly has no such
/// thing. A panel assembled here leaves nothing behind at all: no markup, no
/// handler, no strings.
///
/// So there is no unlocking to be done and no key to check. Having the build is
/// the permission. Hand somebody this installer and they have the panel; hand
/// them the public one and there is nothing there to find, whatever they know.
///
/// Counting is deliberately not in here. Every copy reports its opens and hours
/// — see SetUpDevTools — because a total across everybody is only worth reading
/// if the copies that never see it still add to it.
///
/// Build one with:
///     dotnet build JinxyClicker.csproj -c Release -p:DevTools=true
/// </remarks>
public partial class MainWindow
{
    private TextBlock? _devDownloads;
    private TextBlock? _devOpened;
    private TextBlock? _devOpenedLabel;
    private TextBlock? _devHours;
    private TextBlock? _devHoursLabel;
    private TextBlock? _devPerRelease;
    private TextBlock? _devFootnote;

    private StackPanel? _devPage;
    private ItemsControl? _devRows;
    private TextBlock? _devRowsEmpty;
    private UsageSpan _devSpan = UsageSpan.Daily;
    private readonly List<Button> _devSpanButtons = new();

    /// <summary>
    /// Adds the DEV tab, its page, and the sidebar button that reaches it.
    /// </summary>
    /// <remarks>
    /// The two containers are found through existing children rather than by
    /// name. Naming them in MainWindow.xaml would be one more thing about this
    /// panel present in a public build, and the parent of the settings tab is
    /// by definition the strip the tabs live in.
    /// </remarks>
    /// <summary>
    /// Where a dev build gets its own updates.
    /// </summary>
    /// <remarks>
    /// Empty by default, and that default is deliberate: with no source a dev
    /// build never updates itself, which is the only safe behaviour. Pointing
    /// it at the public releases would have it download the public installer
    /// and overwrite itself with the public app — the DEV tab would vanish and
    /// look like a bug rather than like the build replacing itself.
    ///
    /// To have dev builds update themselves, make a private repository holding
    /// only the dev installer and name it here.
    /// </remarks>
    private const string DevUpdateOwner = "";
    private const string DevUpdateRepo = "";

    /// <summary>
    /// The token, read from a file rather than compiled in.
    /// </summary>
    /// <remarks>
    /// This repository is public. A token written into this file would be
    /// published the moment it was committed — GitHub's own scanning would very
    /// likely revoke it within minutes, and the mistake is easy to make and
    /// hard to undo. So it lives in dev-update.token beside the executable,
    /// which is gitignored and packaged only into the DEV installer.
    ///
    /// It is still readable by anyone holding a dev build, and that is fine:
    /// the token is exactly as private as the people you hand dev builds to. It
    /// keeps the dev installer from being found by anyone else, and it must be
    /// fine-grained, read-only, and scoped to that one repository so the worst
    /// case is the dev installer leaking — which is what happens if a dev build
    /// leaks anyway.
    /// </remarks>
    private static string ReadDevUpdateToken()
    {
        try
        {
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "dev-update.token");

            return System.IO.File.Exists(path)
                ? System.IO.File.ReadAllText(path).Trim()
                : "";
        }
        catch
        {
            return "";
        }
    }

    partial void ShowDevToolsIfBuilt()
    {
        // Set before the update check runs, which is on Loaded.
        UpdateSource.Current = DevUpdateRepo.Length > 0
            ? new UpdateSource(DevUpdateOwner, DevUpdateRepo, ReadDevUpdateToken())
            : UpdateSource.None;

        if (NavSettings?.Parent is not Panel sidebar) return;
        if (PageSettings?.Parent is not Panel pages) return;

        _devPage = new StackPanel { Visibility = Visibility.Collapsed };
        _devPage.Children.Add(BuildDevPanel());
        _devPage.Children.Add(BuildBreakdownPanel());
        _devPage.Children.Add(BuildBuildInfoPanel());

        pages.Children.Add(_devPage);

        var nav = new Button
        {
            Content = "DEV",
            Style = (Style)FindResource("NavButton")
        };

        // "Code", from the same Segoe set the other tabs draw from. Written as
        // an escape so this file stays plain ASCII, and deliberately not E90F —
        // that is the wrench TWEAKS already uses.
        NavIcon.SetGlyph(nav, "\uE943");

        nav.Click += (_, _) =>
        {
            ShowPage(nav, _devPage, "Dev Tools", "Numbers this build can see");

            // Refreshed on arrival rather than only at startup, so the figures
            // are current whenever the tab is actually looked at.
            _ = RefreshDevStatsAsync();
            _ = LoadDownloadCountsAsync();
        };

        sidebar.Children.Add(nav);

        _extraNav.Add(nav);
        _extraPages.Add(_devPage);
    }

    /// <summary>
    /// Which build this is, since two now exist and they look identical.
    /// </summary>
    /// <remarks>
    /// The failure this prevents: shipping the dev build to everybody, or
    /// spending an hour wondering why the panel is missing from a copy that was
    /// never built with it. Seeing the version and the build kind together
    /// makes both obvious at a glance.
    /// </remarks>
    private Border BuildBuildInfoPanel()
    {
        var body = new StackPanel();

        body.Children.Add(new TextBlock
        {
            Text = "THIS BUILD",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextMuted")
        });

        string version = System.Reflection.Assembly
            .GetExecutingAssembly()
            .GetName().Version?.ToString() ?? "unknown";

        var line = Note($"Version {version}   ·   DEV build — not for public release."
                        + (UsageReporter.Configured
                            ? "   ·   Usage counter connected."
                            : "   ·   No usage counter configured.")
                        + (UpdateSource.Current.CanUpdate
                            ? $"   ·   Updates from {UpdateSource.Current.Owner}/{UpdateSource.Current.Repo}."
                            : "   ·   Auto-update off, so this build cannot replace itself with the public app. Rebuild with run-dev.cmd."));

        line.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(line);

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

    /// <summary>
    /// The same three numbers broken down over time, laid out like History.
    /// </summary>
    /// <remarks>
    /// Daily, monthly and all time, from one set of day buckets — the same
    /// shape the click history uses, so the two pages read the same way.
    ///
    /// Downloads behave differently from the other two and the column says so.
    /// GitHub only ever publishes a running total, so a span's downloads are
    /// the difference between the first and last reading inside it. That makes
    /// them unavailable for any day before the app started taking readings, and
    /// for a span holding only one — shown as a dash rather than a zero,
    /// because "not known" and "nobody downloaded it" are different claims.
    /// </remarks>
    private Border BuildBreakdownPanel()
    {
        // Title and switcher on one row, but the switcher is measured rather
        // than squeezed: a fixed height clipped the button labels, and docking
        // it against the title with no margin put it on top of the column
        // headings below.
        var header = new DockPanel { LastChildFill = false };

        var switcher = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        DockPanel.SetDock(switcher, Dock.Right);

        foreach ((string label, UsageSpan span) in new[]
                 {
                     ("Daily", UsageSpan.Daily),
                     ("Monthly", UsageSpan.Monthly),
                     ("All time", UsageSpan.AllTime)
                 })
        {
            var button = new Button
            {
                Content = label,
                // Padding rather than Height, so the button grows to whatever
                // the shared style needs and the label cannot be cut off.
                Padding = new Thickness(14, 6, 14, 6),
                MinWidth = 84,
                Margin = new Thickness(8, 0, 0, 0),
                Tag = span
            };

            button.Click += (_, _) =>
            {
                _devSpan = span;
                MarkSpanButtons();
                ShowRows();
            };

            _devSpanButtons.Add(button);
            switcher.Children.Add(button);
        }

        header.Children.Add(switcher);

        header.Children.Add(new TextBlock
        {
            Text = "OVER TIME",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("TextMuted")
        });

        // Docked the same way as a row, so the headings sit over their columns.
        // The generous top margin is what keeps them clear of the buttons.
        var columns = new DockPanel { Margin = new Thickness(0, 18, 0, 0), LastChildFill = true };

        columns.Children.Add(HeaderCell("DOWNLOADS"));
        columns.Children.Add(HeaderCell("HOURS"));
        columns.Children.Add(HeaderCell("OPENS"));
        columns.Children.Add(new TextBlock());

        _devRowsEmpty = Note("Nothing recorded yet. Days start counting from now.");
        _devRowsEmpty.Margin = new Thickness(0, 14, 0, 0);

        _devRows = new ItemsControl { Margin = new Thickness(0, 6, 0, 0) };

        var body = new StackPanel();
        body.Children.Add(header);
        body.Children.Add(columns);
        body.Children.Add(_devRows);
        body.Children.Add(_devRowsEmpty);

        MarkSpanButtons();

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

    private TextBlock HeaderCell(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Width = 110,
            Foreground = (Brush)FindResource("TextMuted")
        };

        DockPanel.SetDock(block, Dock.Right);

        return block;
    }

    /// <summary>Lights the span in use, so it is clear which is being shown.</summary>
    private void MarkSpanButtons()
    {
        foreach (Button button in _devSpanButtons)
        {
            bool selected = button.Tag is UsageSpan span && span == _devSpan;

            button.FontWeight = selected ? FontWeights.Bold : FontWeights.Normal;
            button.Opacity = selected ? 1.0 : 0.6;
        }
    }

    /// <summary>
    /// The days behind the breakdown: everybody's if there is a counter, this
    /// machine's otherwise.
    /// </summary>
    private List<UsageDay> _devDays = new();

    private void ShowRows()
    {
        if (_devRows == null || _devRowsEmpty == null) return;

        List<UsageRow> rows = UsagePeriod.Rows(_devDays, _devSpan);

        _devRows.ItemsSource = rows;
        _devRows.ItemTemplate = RowTemplate();

        _devRowsEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// One row of the breakdown, built in code like the rest of this page.
    /// </summary>
    /// <remarks>
    /// A DockPanel rather than a Grid: column definitions cannot be added
    /// through a FrameworkElementFactory, and three fixed-width cells docked
    /// right with the label filling what is left produces exactly the same
    /// geometry as the header's star-plus-three-110s.
    ///
    /// Docked in reverse, because DockPanel places in order and the last one
    /// docked sits nearest the middle.
    /// </remarks>
    private DataTemplate RowTemplate()
    {
        var row = new FrameworkElementFactory(typeof(DockPanel));
        row.SetValue(DockPanel.LastChildFillProperty, true);

        foreach ((string binding, bool accent) in new[]
                 {
                     (nameof(UsageRow.DownloadsText), false),
                     (nameof(UsageRow.HoursText), true),
                     (nameof(UsageRow.OpensText), false)
                 })
        {
            var cell = new FrameworkElementFactory(typeof(TextBlock));

            cell.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(binding));
            cell.SetValue(DockPanel.DockProperty, Dock.Right);
            cell.SetValue(FrameworkElement.WidthProperty, 110.0);
            cell.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);

            if (accent)
            {
                cell.SetValue(TextBlock.ForegroundProperty, (Brush)FindResource("Accent"));
                cell.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            }

            row.AppendChild(cell);
        }

        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(UsageRow.Label)));
        label.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        label.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        row.AppendChild(label);

        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
        border.SetValue(Border.BorderBrushProperty, (Brush)FindResource("Hairline"));
        border.SetValue(Border.PaddingProperty, new Thickness(0, 10, 0, 10));
        border.AppendChild(row);

        return new DataTemplate { VisualTree = border };
    }

    private Border BuildDevPanel()
    {
        _devDownloads = Figure();
        _devOpened = Figure();
        _devHours = Figure();

        _devOpenedLabel = Caption("TIMES OPENED");
        _devHoursLabel = Caption("TOTAL HOURS OPEN");

        var refresh = new Button
        {
            Content = "Refresh",
            Height = 28,
            Padding = new Thickness(14, 0, 14, 0)
        };

        refresh.Click += (_, _) =>
        {
            _ = RefreshDevStatsAsync();
            _ = LoadDownloadCountsAsync();
        };

        DockPanel.SetDock(refresh, Dock.Right);

        var header = new DockPanel { LastChildFill = false };
        header.Children.Add(refresh);
        header.Children.Add(new TextBlock
        {
            Text = "DEV TOOLS",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA7, 0x8B, 0xFA))
        });

        var figures = new Grid { Margin = new Thickness(0, 14, 0, 0) };

        for (int i = 0; i < 3; i++)
            figures.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        figures.Children.Add(Column(0, _devDownloads, Caption("TOTAL DOWNLOADS")));
        figures.Children.Add(Column(1, _devOpened, _devOpenedLabel));
        figures.Children.Add(Column(2, _devHours, _devHoursLabel));

        _devPerRelease = Note("");
        _devPerRelease.Margin = new Thickness(0, 14, 0, 0);

        _devFootnote = Note("");
        _devFootnote.Margin = new Thickness(0, 10, 0, 0);

        var body = new StackPanel();
        body.Children.Add(header);
        body.Children.Add(figures);
        body.Children.Add(_devPerRelease);
        body.Children.Add(_devFootnote);

        return new Border
        {
            Background = (Brush)FindResource("Panel"),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x3A, 0x76)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(18),
            Margin = new Thickness(0, 0, 0, 14),
            Child = body
        };
    }

    private TextBlock Figure() => new()
    {
        Text = "—",
        FontSize = 26,
        FontWeight = FontWeights.Black,
        Foreground = (Brush)FindResource("TextBright")
    };

    private TextBlock Caption(string text) => new()
    {
        Text = text,
        FontSize = 9,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, 4, 0, 0),
        Foreground = (Brush)FindResource("TextMuted")
    };

    private TextBlock Note(string text) => new()
    {
        Text = text,
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Foreground = (Brush)FindResource("TextMuted")
    };

    private static StackPanel Column(int column, UIElement figure, UIElement caption)
    {
        var panel = new StackPanel();
        panel.Children.Add(figure);
        panel.Children.Add(caption);

        Grid.SetColumn(panel, column);

        return panel;
    }

    /// <summary>
    /// Shows the shared totals when there is a counter, and this machine's own
    /// when there is not.
    /// </summary>
    /// <remarks>
    /// The labels change with the source rather than staying generic. "Total
    /// hours open" meaning everybody, and meaning only this PC, are different
    /// enough claims that one fixed label would be read wrong — and read wrong
    /// in the flattering direction.
    /// </remarks>
    private async Task RefreshDevStatsAsync()
    {
        if (_devOpened == null || _devHours == null) return;

        double localSeconds = _usage.OpenSeconds + _sessionClock.Elapsed.TotalSeconds;

        if (!UsageReporter.Configured)
        {
            ShowLocalUsage(localSeconds, "No counter set up yet — see Server/usage-worker.js.");
            return;
        }

        UsageTotals? totals = await UsageReporter
            .FetchAsync(CancellationToken.None).ConfigureAwait(true);

        if (totals == null)
        {
            ShowLocalUsage(localSeconds, "Could not reach the usage counter.");
            return;
        }

        _devOpenedLabel!.Text = "TIMES OPENED (EVERYONE)";
        _devHoursLabel!.Text = "TOTAL HOURS OPEN (EVERYONE)";

        _devOpened.Text = totals.Opens.ToString("N0");
        _devHours.Text = UsageStats.Format(totals.Seconds);

        _devDays = totals.Days;
        ShowRows();

        _devFootnote!.Text =
            "Downloads count every asset download, so one person taking three versions counts three times. "
            + "Opens and hours are totals across every copy of the app — two numbers and nothing else, "
            + $"with no way to tell whose they are. This PC has contributed {_usage.LaunchesText} "
            + $"opens and {UsageStats.Format(localSeconds)}.";
    }

    private void ShowLocalUsage(double localSeconds, string why)
    {
        _devOpenedLabel!.Text = "TIMES OPENED (THIS PC)";
        _devHoursLabel!.Text = "HOURS OPEN (THIS PC)";

        _devOpened!.Text = _usage.LaunchesText;
        _devHours!.Text = UsageStats.Format(localSeconds);

        _devDays = _usage.Days;
        ShowRows();

        _devFootnote!.Text = why + " Opens and hours are this computer only.";
    }

    private async Task LoadDownloadCountsAsync()
    {
        if (_devDownloads == null) return;

        _devDownloads.Text = "…";

        List<ReleaseDownloads>? releases =
            await ReleaseStats.FetchAsync(CancellationToken.None).ConfigureAwait(true);

        if (releases == null || releases.Count == 0)
        {
            // Rate limited, offline, or no releases yet. Says so rather than
            // showing a zero that reads as "nobody downloaded it".
            _devDownloads.Text = "—";
            _devPerRelease!.Text = "Could not reach GitHub. It allows 60 checks an hour.";
            return;
        }

        long total = ReleaseStats.Total(releases);

        _devDownloads.Text = total.ToString("N0");

        _devPerRelease!.Text = string.Join("     ",
            releases.Take(6).Select(r => $"{r.Tag} — {r.Downloads:N0}"));

        // Recorded so downloads can be broken down over time at all. Only a
        // build with this panel ever reads GitHub, so only this build can take
        // the reading — and a day's downloads is the gap between two of them.
        UsageReporter.ReportDownloads(total);

        // Today's reading counts towards this machine's own breakdown too, for
        // when there is no shared counter to hold it.
        UsagePeriod.RecordDownloads(
            _usage.Days,
            DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            total);

        _usage.Save();

        ShowRows();
    }
}

#endif
