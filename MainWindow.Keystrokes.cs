using System;
using System.Windows;
using System.Windows.Threading;

namespace JinxyClicker;

/// <summary>
/// The keystrokes overlay (Pro): an on-screen WASD + mouse + CPS display. This
/// part owns the window and keeps it in step with the toggle, Pro state, theme
/// accent and streamer mode.
/// </summary>
public partial class MainWindow
{
    private KeystrokeOverlay? _keystrokes;
    private readonly DispatcherTimer _fpsHeartbeat = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _fpsHeartbeatWired;
    private bool _keystrokesEnabled;
    private bool _keystrokesMoving;
    private bool _keystrokesBackground = true;
    private bool _keystrokesFps;
    private bool _loadingKeystrokes;
    private double? _keystrokesX, _keystrokesY;

    private void LoadKeystrokes(AppSettings s)
    {
        _loadingKeystrokes = true;
        _keystrokesEnabled = s.KeystrokesOverlay;
        _keystrokesBackground = s.KeystrokesBackground;
        _keystrokesFps = s.KeystrokesFps;
        _keystrokesX = s.KeystrokesX;
        _keystrokesY = s.KeystrokesY;
        if (KeystrokesCheck is not null) KeystrokesCheck.IsChecked = _keystrokesEnabled;
        if (KeystrokesBgCheck is not null) KeystrokesBgCheck.IsChecked = _keystrokesBackground;
        if (KeystrokesFpsCheck is not null) KeystrokesFpsCheck.IsChecked = _keystrokesFps;
        _loadingKeystrokes = false;
        ApplyKeystrokesPro();
    }

    private void KeystrokesFps_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingKeystrokes || KeystrokesFpsCheck is null) return;
        _keystrokesFps = KeystrokesFpsCheck.IsChecked == true;
        _settingsDirty = true;

        if (_keystrokesFps)
        {
            // One-time admin prompt to install the helper's scheduled task. After
            // this the FPS helper runs elevated with no UAC prompt, ever.
            if (!FpsTask.EnsureCreated())
            {
                _keystrokesFps = false;
                KeystrokesFpsCheck.IsChecked = false;
                _settingsDirty = true;
                if (KeystrokesHint is not null)
                    KeystrokesHint.Text = "FPS setup was cancelled — leave it off, or try again to allow it once.";
                return;
            }
            StartFpsHelper();
        }
        else
        {
            StopFpsHelper();
        }

        ApplyKeystrokesState();
    }

    // Kicks the elevated helper (via its scheduled task) and keeps a heartbeat so it
    // stays alive while we want FPS and exits on its own when we stop.
    private void StartFpsHelper()
    {
        FpsShared.Ping();
        FpsTask.Start();
        if (!_fpsHeartbeatWired)
        {
            _fpsHeartbeat.Tick += (_, _) => FpsShared.Ping();
            _fpsHeartbeatWired = true;
        }
        _fpsHeartbeat.Start();
    }

    private void StopFpsHelper()
    {
        _fpsHeartbeat.Stop();
        FpsTask.Stop();
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
                _keystrokes.FpsProbe = () => _keystrokesFps ? FpsShared.ReadFps() : (int?)null;
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
            if (_keystrokesFps && FpsTask.Exists()) StartFpsHelper(); else StopFpsHelper();
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
            StopFpsHelper();
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
