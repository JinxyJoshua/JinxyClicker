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
        ShowPage(NavKeystrokes, PageKeystrokes, "Keystrokes", "On-screen keys and CPS overlay");
        ApplyKeystrokesPro();
    }

    private void ApplyKeystrokesPro()
    {
        bool pro = IsPro;

        // The whole tab is Pro-only: hidden from the sidebar unless licensed.
        if (NavKeystrokes is not null) NavKeystrokes.Visibility = pro ? Visibility.Visible : Visibility.Collapsed;
        if (KeystrokesLockNote is not null) KeystrokesLockNote.Visibility = Visibility.Collapsed;
        if (KeystrokesProPanel is not null) KeystrokesProPanel.IsEnabled = pro;

        if (!pro && PageKeystrokes is { Visibility: Visibility.Visible })
            ShowPage(NavClicker, PageClicker, "Clicker", "Configure your own click engine");

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
                if (_keystrokesX is { } x && _keystrokesY is { } y) { _keystrokes.Left = x; _keystrokes.Top = y; }
            }
            _keystrokes.SetAccent(AccentColorValue());
            _keystrokes.SetBackground(_keystrokesBackground);
            if (!_keystrokes.IsVisible) _keystrokes.Show();
        }
        else
        {
            _keystrokes?.Hide();
        }
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
