#if DEVTOOLS

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JinxyClicker;

/// <summary>
/// Dev-only Pro-gate simulator. Lets the developer force Free or Pro without a
/// real key, to verify every feature locks and unlocks. Behind DEVTOOLS, so it is
/// compiled into no public build.
/// </summary>
public partial class MainWindow
{
    // null = use the real licence; true/false = force that state. Read in
    // RefreshLicense (also under DEVTOOLS).
    private bool? _devForceProOverride;

    // Persist the choice so a dev build stays on the forced gate across launches
    // (otherwise it defaults to the real/unkeyed licence = Free every start).
    private static string DevForceProPath => SettingsPath.For("devforcepro.txt");

    partial void LoadDevForcePro()
    {
        try
        {
            if (!System.IO.File.Exists(DevForceProPath)) return;
            _devForceProOverride = System.IO.File.ReadAllText(DevForceProPath).Trim() switch
            {
                "pro" => true,
                "free" => false,
                _ => null,
            };
        }
        catch { }
    }

    private void SaveDevForcePro()
    {
        try
        {
            System.IO.File.WriteAllText(DevForceProPath,
                _devForceProOverride == true ? "pro" : _devForceProOverride == false ? "free" : "real");
        }
        catch { }
    }

    private Border BuildProSimPanel()
    {
        var body = new StackPanel();

        body.Children.Add(new TextBlock
        {
            Text = "PRO GATE (DEV)",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextMuted")
        });

        TextBlock note = Note("Flip the Pro gate without a real key to check every feature locks and unlocks. "
                              + "“Real licence” goes back to the actual key. Only in this dev build.");
        note.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(note);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        foreach ((string label, bool? value) in new (string, bool?)[]
                 {
                     ("Real licence", null),
                     ("Force Free", false),
                     ("Force Pro", true),
                 })
        {
            var btn = new Button
            {
                Content = label,
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(0, 0, 8, 0),
                Tag = value
            };
            btn.Click += (_, _) =>
            {
                _devForceProOverride = value;
                SaveDevForcePro();  // remember across launches
                RefreshLicense();   // re-applies every page's Pro gating live
                MarkProSimButtons(row);
            };
            row.Children.Add(btn);
        }
        body.Children.Add(row);

        MarkProSimButtons(row);

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

    private void MarkProSimButtons(StackPanel row)
    {
        foreach (object child in row.Children)
            if (child is Button b)
            {
                bool selected = Equals(b.Tag as bool?, _devForceProOverride);
                b.FontWeight = selected ? FontWeights.Bold : FontWeights.Normal;
                b.Opacity = selected ? 1.0 : 0.6;
            }
    }
}

#endif
