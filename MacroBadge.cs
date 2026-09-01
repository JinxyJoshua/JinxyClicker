using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace JinxyClicker;

/// <summary>
/// A badge in the corner of the screen for as long as a macro is running.
/// </summary>
/// <remarks>
/// Unlike the capture toast this does not fade. That is the point: a macro
/// holds keys down in a game that binds those same keys to its own actions, and
/// a macro left running on F while you are trying to play is indistinguishable
/// from the game misbehaving. The badge names the keys it is pressing, so the
/// answer to "why is my F doing that" is on screen rather than three tabs deep
/// in an app that is behind the game.
///
/// It never takes focus and is click-through, so it cannot steal a click or
/// pull the foreground away from Roblox.
///
/// Whether the capture stack can see it is the one thing that changes: by
/// default it is in recordings, because a viewer seeing it is honest. Streamer
/// mode hides it from OBS, Discord and the rest while leaving it on the monitor
/// — see <see cref="StreamerMode"/>.
/// </remarks>
public sealed class MacroBadge : Window
{
    private readonly TextBlock _label;
    private bool _streamerMode;

    public MacroBadge()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        IsHitTestVisible = false;

        _label = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };

        var dot = new Border
        {
            Width = 9,
            Height = 9,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 9, 0)
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(dot);
        row.Children.Add(_label);

        Content = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x10, 0x10, 0x16)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)),
            Padding = new Thickness(14, 8, 16, 8),
            Child = row
        };

        SourceInitialized += (_, _) =>
        {
            MakeClickThrough();
            ApplyCaptureVisibility();
        };
    }

    /// <summary>
    /// Hides the badge from screen capture while leaving it on the monitor.
    /// </summary>
    /// <remarks>
    /// The person at the keyboard still needs to know a macro is holding a key
    /// down; that need does not go away because they are streaming. So this
    /// hides it from the capture stack rather than turning it off — the warning
    /// is kept, the audience just does not see it.
    /// </remarks>
    public bool StreamerMode
    {
        get => _streamerMode;
        set
        {
            if (_streamerMode == value) return;

            _streamerMode = value;

            ApplyCaptureVisibility();
        }
    }

    /// <summary>Shows the badge naming what is running, or hides it if nothing is.</summary>
    /// <param name="keys">What each running macro is pressing, already trimmed.</param>
    public void Update(System.Collections.Generic.IReadOnlyList<string> keys)
    {
        if (keys.Count == 0)
        {
            if (IsVisible) Hide();
            return;
        }

        // The keys, not the macro names. A name is what its author called it;
        // the keys are what the game is about to receive, which is the thing
        // worth knowing before wondering why F is behaving oddly.
        string what = string.Join("  ", keys);

        _label.Text = keys.Count == 1 ? $"MACRO ON   {what}" : $"MACROS ON   {what}";

        if (!IsVisible) Show();

        UpdateLayout();
        PlaceTopLeft();
    }

    private void PlaceTopLeft()
    {
        const int margin = 24;

        Left = SystemParameters.WorkArea.Left + margin;
        Top = SystemParameters.WorkArea.Top + margin;
    }

    private void ApplyCaptureVisibility()
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            SetWindowDisplayAffinity(
                handle, _streamerMode ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
        }
        catch
        {
            // Older than Windows 10 2004. The badge simply appears in captures,
            // which is what it does by default anyway.
        }
    }

    private void MakeClickThrough()
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            int style = GetWindowLong(handle, GWL_EXSTYLE);

            SetWindowLong(handle, GWL_EXSTYLE,
                style | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }
        catch
        {
            // A badge that cannot be styled is still a usable badge.
        }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr window, int index, int value);
}
