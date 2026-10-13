using System.Windows;

namespace JinxyClicker;

/// <summary>
/// The keystrokes overlay (Pro): an on-screen WASD + mouse + CPS display. This
/// part owns the window and keeps it in step with the toggle, Pro state, theme
/// accent and streamer mode.
/// </summary>
public partial class MainWindow
{
    private KeystrokeOverlay? _keystrokes;
    private readonly RobloxFps _robloxFps = new();
    private bool _keystrokesEnabled;
    private bool _keystrokesMoving;
    private bool _keystrokesBackground = true;
    private double? _keystrokesX, _keystrokesY;

    private void LoadKeystrokes(AppSettings s)
    {
        _keystrokesEnabled = s.KeystrokesOverlay;
        _keystrokesBackground = s.KeystrokesBackground;
        _keystrokesX = s.KeystrokesX;
        _keystrokesY = s.KeystrokesY;
        if (KeystrokesCheck is not null) KeystrokesCheck.IsChecked = _keystrokesEnabled;
        if (KeystrokesBgCheck is not null) KeystrokesBgCheck.IsChecked = _keystrokesBackground;
        ApplyKeystrokesPro();
    }

    private void NavKeystrokes_Click(object sender, RoutedEventArgs e)
    {
        if (!IsPro) { NavPro_Click(sender, e); return; }   // gated: send to the key page
        ShowPage(NavKeystrokes, PageKeystrokes, "Keystrokes", "On-screen keys and CPS overlay");
        ApplyKeystrokesPro();
    }

    private void ApplyKeystrokesPro()
    {
        bool pro = IsPro;

        // The tab stays visible so Free users see the feature, but its content is
        // never shown — clicking it (or a pass expiring here) sends them to the key
        // page instead.
        if (KeystrokesLockNote is not null) KeystrokesLockNote.Visibility = Visibility.Collapsed;
        if (KeystrokesProPanel is not null) KeystrokesProPanel.IsEnabled = pro;

        if (!pro && PageKeystrokes is { Visibility: Visibility.Visible })
            ShowPage(NavProCorner, PagePro, "Plus", "Unlock the paid features with a key");

        ApplyKeystrokesState();
    }

    private void ApplyKeystrokesState()
    {
        bool show = IsPro && _keystrokesEnabled;
        if (show)
        {
            if (_keystrokes is null)
            {
                _keystrokes = new KeystrokeOverlay { StreamerMode = StreamerModeCheck?.IsChecked == true };
                // Show keys SOCD is swallowing: a blocked key doesn't register in
                // GetAsyncKeyState, so ask the SOCD hook for its physical state too.
                _keystrokes.KeyStateProbe = _socd.IsDown;
                // In-game FPS on the overlay (needs the app to run as admin; shows
                // "—" otherwise). Measured off a real-time ETW session.
                _keystrokes.FpsProbe = () => _robloxFps.Available ? _robloxFps.Current : (int?)null;
                if (_keystrokesX is { } x && _keystrokesY is { } y
                    && OnVirtualScreen(x, y, _keystrokes.Width, _keystrokes.Height))
                { _keystrokes.Left = x; _keystrokes.Top = y; }

                // Remember wherever it ends up. Dragging it (even without clicking
                // "Lock in place") and closing the app keeps it in the same spot
                // next launch, instead of only the last explicitly-locked position.
                _keystrokes.LocationChanged += (_, _) =>
                {
                    _keystrokesX = _keystrokes.Left;
                    _keystrokesY = _keystrokes.Top;
                    _settingsDirty = true;
                };
            }
            _keystrokes.SetAccent(AccentColorValue());
            _keystrokes.SetBackground(_keystrokesBackground);
            _robloxFps.Start();   // begin measuring FPS while the overlay is up
            if (!_keystrokes.IsVisible)
            {
                _keystrokes.Show();
                // A SizeToContent window can shift on its first layout, so pin it to
                // the saved spot once it's actually up — but only if that spot is on
                // a monitor that still exists.
                if (_keystrokesX is { } sx && _keystrokesY is { } sy
                    && OnVirtualScreen(sx, sy, _keystrokes.ActualWidth, _keystrokes.ActualHeight))
                { _keystrokes.Left = sx; _keystrokes.Top = sy; }
            }
        }
        else
        {
            _keystrokes?.Hide();
            _robloxFps.Stop();
        }
    }

    // True if a window of this size placed at (left, top) would land at least
    // partly on a monitor that currently exists. The overlay is click-through, so
    // a position saved on a monitor that's since been unplugged (or a resolution
    // that shrank) would otherwise strand it off-screen with no way to grab it —
    // same guard the main window uses in RestoreWindowPlacement.
    private static bool OnVirtualScreen(double left, double top, double width, double height)
    {
        if (double.IsNaN(width) || width <= 0) width = 160;
        if (double.IsNaN(height) || height <= 0) height = 90;

        double vl = SystemParameters.VirtualScreenLeft;
        double vt = SystemParameters.VirtualScreenTop;
        double vr = vl + SystemParameters.VirtualScreenWidth;
        double vb = vt + SystemParameters.VirtualScreenHeight;

        return left + width > vl && left < vr && top + height > vt && top < vb;
    }

    private void KeystrokesEnabled_Changed(object sender, RoutedEventArgs e)
    {
        _keystrokesEnabled = KeystrokesCheck.IsChecked == true;
        _settingsDirty = true;
        ApplyKeystrokesState();
    }

    private void KeystrokesBackground_Changed(object sender, RoutedEventArgs e)
    {
        _keystrokesBackground = KeystrokesBgCheck.IsChecked == true;
        _settingsDirty = true;
        _keystrokes?.SetBackground(_keystrokesBackground);
    }

    // Drops click-through so the overlay can be dragged, then saves where it lands.
    private void KeystrokesMove_Click(object sender, RoutedEventArgs e)
    {
        if (_keystrokes is null || !_keystrokes.IsVisible)
        {
            SetKeystrokesHint("Turn the overlay on first.");
            return;
        }

        _keystrokesMoving = !_keystrokesMoving;
        _keystrokes.SetMoveMode(_keystrokesMoving);

        if (KeystrokesMoveButton is not null)
            KeystrokesMoveButton.Content = _keystrokesMoving ? "Lock in place" : "Move overlay";

        if (!_keystrokesMoving)
        {
            _keystrokesX = _keystrokes.Left;
            _keystrokesY = _keystrokes.Top;
            _settingsDirty = true;
            SaveAppSettings();
            SetKeystrokesHint("Saved its position.");
        }
        else
        {
            SetKeystrokesHint("Drag the overlay where you want it, then click Lock in place.");
        }
    }

    private void SetKeystrokesHint(string text)
    {
        if (KeystrokesHint is not null) KeystrokesHint.Text = text;
    }
}
