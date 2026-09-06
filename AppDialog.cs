using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JinxyClicker;

/// <summary>
/// A short message, in the app's own colours.
/// </summary>
/// <remarks>
/// MessageBox draws itself from the system theme, so a dark app raising one
/// produces a white panel with a Windows 95 icon in the middle of it. It is
/// also the wrong shape for a sentence that has to name three things.
///
/// Deliberately small: a title, a body, one button. Anything that needs a
/// choice belongs on a page where the choice can be seen next to what it
/// affects, not in a box that has to be dismissed before the app is legible
/// again.
/// </remarks>
public sealed class AppDialog : Window
{
    private AppDialog(Window owner, string heading, string body)
    {
        Owner = owner;
        Title = heading;

        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 460;
        Background = Brushes.Transparent;
        AllowsTransparency = true;

        // Dragged by its body, since there is no title bar to take hold of.
        MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch { } };

        Content = Build(owner, heading, body);
    }

    /// <summary>Shows the message and returns when it is dismissed.</summary>
    public static void Show(Window owner, string heading, string body) =>
        new AppDialog(owner, heading, body).ShowDialog();

    private UIElement Build(Window owner, string heading, string body)
    {
        Brush Resource(string key, Color fallback) =>
            owner.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text = heading,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = Resource("Accent", Color.FromRgb(0x8B, 0x5C, 0xF6)),
            Margin = new Thickness(0, 0, 0, 10)
        });

        stack.Children.Add(new TextBlock
        {
            Text = body,
            FontSize = 12,
            LineHeight = 19,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Resource("TextMuted", Color.FromRgb(0x9A, 0x9A, 0xAA))
        });

        var ok = new Button
        {
            Content = "OK",
            MinWidth = 96,
            Height = 34,
            Margin = new Thickness(0, 18, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true,
            IsCancel = true
        };

        if (owner.TryFindResource("SegmentButton") is Style segment) ok.Style = segment;

        ok.Click += (_, _) => Close();
        stack.Children.Add(ok);

        return new Border
        {
            Background = Resource("Panel", Color.FromRgb(0x14, 0x14, 0x1C)),
            BorderBrush = Resource("Outline", Color.FromRgb(0x2A, 0x2A, 0x35)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(22),
            Child = stack
        };
    }
}
