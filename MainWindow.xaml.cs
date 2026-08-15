using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace JinxyClicker;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _clickCts;
    // Volatile: written on the UI thread, read by the hotkey poll thread for the
    // corner failsafe and by the click loop. Without it the read is free to
    // observe a cached value and miss that clicking has started or stopped.
    private volatile bool _running;
    private bool _holdMode;
    private double _shakeLeft = 8, _shakeRight = 20, _shakeUp = 40, _shakeDown = 8;

    /// <summary>Movements per second. Drives the interval the shake loop sleeps.</summary>
    private double _shakeSpeed = 33;
    private ShakeRange? _savedShake;
    private readonly CancellationTokenSource _shakeCts = new();
    private volatile bool _shakeActive;
    // Written by the shake thread, read by the UI thread for the status line.
    private volatile ShakeGate _shakeGate = ShakeGate.NotRoblox;
    private uint _cachedForegroundPid;
    private bool _cachedForegroundIsRoblox;
    private volatile int _lastPingMs = -1;
    private bool _pingInFlight;
    private long _clickCount;
    private long _lastClickCount;
    private long _lastRateTimestamp;
    private ulong _prevIdleTicks;
    private ulong _prevKernelTicks;
    private ulong _prevUserTicks;
    private bool _haveCpuSample;
    private readonly DispatcherTimer _statsTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly HotkeySettings _hotkeySettings = new();

    private enum RebindTarget { None, Click, Replay, Record, Combo }
    private RebindTarget _rebinding = RebindTarget.None;

    private const int HotkeyPollMs = 8;

    private readonly ObservableCollection<ClickPreset> _clickPresets = new();

    private readonly TweakState _tweakState = TweakState.Load();
    private readonly ClickHistory _history = ClickHistory.Load();
    private bool _historyDirty;

    private readonly ObservableCollection<PcTweak> _tweaks = new()
    {
        new HighPerformancePlanTweak(),
        new CoreParkingTweak(),
        new GameDvrTweak(),
        new TransparencyTweak(),
        new VisualEffectsTweak(),
        new GpuSchedulingTweak(),
        new SysMainTweak(),
        new PowerThrottlingTweak()
    };

    // Lives on Optimizations rather than Tweaks: it is about input fidelity,
    // not machine performance.
    private readonly ObservableCollection<PcTweak> _inputTweaks = new()
    {
        new TrackingHelperTweak()
    };

    private readonly ObservableCollection<PcTweak> _networkTweaks = new()
    {
        new QosPolicyTweak()
    };

    // Saving is deferred to the stats tick so dragging a slider does not write
    // the file on every pixel of movement.
    private bool _settingsLoaded;
    private bool _settingsDirty;

    public MainWindow()
    {
        try
        {
            InitializeComponent();

            // Load hotkey settings
            _hotkeySettings.Load();
            DropDuplicateHotkeys();
            ApplyHotkeyToUi();
            // The slider's own ValueChanged fires before its backing field is
            // assigned during parsing, so the label needs setting explicitly.
            UpdateShakeLabel();
            UpdateEngineSettings();

            _statsTimer.Tick += StatsTimer_Tick;
            _statsTimer.Start();
            UpdateStats();

            new Thread(() => HotkeyLoop(_shakeCts.Token))
            {
                IsBackground = true,
                Name = "HotkeyPoll"
            }.Start();

            foreach (ClickPreset p in PresetStore.Load()) _clickPresets.Add(p);
            PresetList.ItemsSource = _clickPresets;

            // Before the settings load, which needs the buttons to exist in
            // order to check the stored one.
            BuildDisplayButtons();

            ApplyAppSettings(AppSettings.Load());
            _settingsLoaded = true;

            RefreshAppliedPreset();

            // Runs for the whole session, but only moves the cursor while the
            // clicker is running — the loop gates on that. It stays alive even
            // when idle because stopping the clicker mid-offset has to put the
            // cursor back where it started, and only this thread knows where
            // that was.
            new Thread(() => ShakeLoop(_shakeCts.Token))
            {
                IsBackground = true,
                Name = "ShakeEngine"
            }.Start();

            TweakList.ItemsSource = _tweaks;
            InputTweakList.ItemsSource = _inputTweaks;
            NetworkTweakList.ItemsSource = _networkTweaks;
            RefreshTweaks();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error in MainWindow constructor: {ex.Message}\n\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }

    private void CpsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Skipped while masked, or the running value would replace the mask.
        if (CpsValueBox != null && !_valuesHidden)
            CpsValueBox.Text = FormatValue(CpsSlider.Value);

        RefreshAppliedPreset();
        UpdateEngineSettings();
    }

    private void CdcSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CdcValueBox != null && !_valuesHidden)
            CdcValueBox.Text = FormatValue(CdcSlider.Value);

        RefreshAppliedPreset();
        UpdateEngineSettings();
    }

    private static string FormatValue(double value) =>
        value.ToString("0.0", CultureInfo.CurrentCulture);

    // ---- hiding the rates ----

    private const string MaskedValue = "•••";

    private bool _valuesHidden;

    /// <summary>
    /// Masks the CPS and duty cycle so they cannot be read off the screen.
    /// </summary>
    /// <remarks>
    /// The sliders are hidden too, not just the numbers — a thumb position gives
    /// the value away just as plainly as the digits do. Hidden rather than
    /// collapsed, so the cards keep their height and the page does not jump.
    /// </remarks>
    private void HideValues_Click(object sender, RoutedEventArgs e)
    {
        _valuesHidden = !_valuesHidden;
        ApplyValueVisibility();
        _settingsDirty = true;
    }

    private void ApplyValueVisibility()
    {
        if (CpsValueBox == null || CdcValueBox == null) return;

        HideValuesButton.Content = _valuesHidden ? "Show values" : "Hide values";

        Visibility sliders = _valuesHidden ? Visibility.Hidden : Visibility.Visible;
        CpsSlider.Visibility = sliders;
        CdcSlider.Visibility = sliders;
        MeasuredCpsText.Visibility = sliders;

        // Read-only while masked, or typing would replace the mask with a value
        // that then gets committed on focus loss.
        CpsValueBox.IsReadOnly = _valuesHidden;
        CdcValueBox.IsReadOnly = _valuesHidden;

        CpsValueBox.Text = _valuesHidden ? MaskedValue : FormatValue(CpsSlider.Value);
        CdcValueBox.Text = _valuesHidden ? MaskedValue : FormatValue(CdcSlider.Value);
    }

    // Committing writes through the slider, which coerces to its own range. The
    // box is then reformatted from the slider so unparseable or out-of-range
    // text is visibly corrected rather than silently kept.
    private void CommitValue(System.Windows.Controls.TextBox box, Slider slider)
    {
        // A masked box holds bullets, not a number — committing it would parse
        // as nothing and rewrite the mask over itself.
        if (_valuesHidden) return;

        if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double parsed))
            slider.Value = Math.Clamp(parsed, slider.Minimum, slider.Maximum);

        box.Text = FormatValue(slider.Value);
    }

    private void CpsValueBox_LostFocus(object sender, RoutedEventArgs e) => CommitValue(CpsValueBox, CpsSlider);
    private void CdcValueBox_LostFocus(object sender, RoutedEventArgs e) => CommitValue(CdcValueBox, CdcSlider);

    private void CpsValueBox_KeyDown(object sender, KeyEventArgs e) => HandleValueBoxKey(e, CpsValueBox, CpsSlider);
    private void CdcValueBox_KeyDown(object sender, KeyEventArgs e) => HandleValueBoxKey(e, CdcValueBox, CdcSlider);

    private void HandleValueBoxKey(KeyEventArgs e, System.Windows.Controls.TextBox box, Slider slider)
    {
        if (_valuesHidden) return;

        if (e.Key == Key.Enter)
        {
            CommitValue(box, slider);
            // Hand focus back to the window, otherwise the hotkey has nowhere
            // to land and stops working after an edit.
            Window.GetWindow(box)?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Discard the edit, but let Escape keep bubbling to the emergency stop.
            box.Text = FormatValue(slider.Value);
        }
    }

    private void StartStop_Click(object sender, RoutedEventArgs e) => ToggleRunning();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Rebind capture happens in Window_PreviewKeyDown, ahead of this.
        // Hotkey activation is polled in HotkeyTimer_Tick, not handled here.

        if (e.Key != Key.Escape || e.IsRepeat) return;

        // While a confirmation is open Escape means "cancel that", not "stop
        // clicking" — the dialog is the thing in front of the user.
        if (IsConfirming)
        {
            CloseConfirm(false);
            return;
        }

        // Emergency stop stays a window event on purpose: polling it would make
        // Escape stop the clicker from inside any application.
        StopClicking();
    }

    /// <summary>
    /// Hotkeys are polled rather than driven by window events, on a dedicated
    /// thread rather than a timer.
    /// </summary>
    /// <remarks>
    /// Routed KeyDown only arrives while this window has focus, which is exactly
    /// when a game is not in front, and mouse side buttons never route here at
    /// all — hence polling GetAsyncKeyState.
    ///
    /// It runs on its own thread because a DispatcherTimer ticks on the UI
    /// thread at Background priority, below rendering and input. Under load its
    /// ticks stretch out, and a press and release that both land inside one gap
    /// cancel out — the edge is never seen and the key appears to do nothing.
    /// That is the "sometimes it does not stop" case. Detection now happens off
    /// the dispatcher; only the resulting action is marshalled back.
    /// </remarks>
    private void HotkeyLoop(CancellationToken token)
    {
        bool clickWasDown = false;
        bool replayWasDown = false;
        bool recordWasDown = false;
        bool comboWasDown = false;
        bool wasInCorner = false;
        bool wasArmed = false;

        while (!token.IsCancellationRequested)
        {
            ClickSettings s = _settings;

            // Deliberately outside the armed check. This is the way out of a
            // clicker that will not stop, and switching hotkeys off must not
            // take the escape hatch with them.
            //
            // Edge-triggered like the hotkeys are: parking in a corner would
            // otherwise post a stop every 8 ms for as long as the mouse sat there.
            bool inCorner = PointerInCorner();
            if (inCorner && !wasInCorner && _running)
                Dispatcher.InvokeAsync(StopClicking, DispatcherPriority.Send);

            wasInCorner = inCorner;

            bool clickDown = IsKeyDown(s.HotkeyVk);
            bool recordDown = IsKeyDown(s.RecordHotkeyVk);
            bool replayDown = IsKeyDown(s.ReplayHotkeyVk);
            bool comboDown = IsKeyDown(s.ComboHotkeyVk);

            // The first armed pass adopts whatever is held without acting on it.
            //
            // This is what stops a rebind from firing the action it just bound.
            // Choosing a hotkey publishes the new key and re-arms in the same
            // snapshot, and at that moment the key is still physically down —
            // the press that chose it. Without this the edge detector compares
            // that against state primed from the *previous* binding, reads a
            // fresh press, and starts the clicker the instant you pick its key.
            //
            // It covers the master switch too: turning hotkeys back on while
            // resting on one no longer triggers it.
            if (s.HotkeysArmed && wasArmed)
            {
                if (clickDown != clickWasDown)
                    Dispatcher.InvokeAsync(() => OnClickHotkey(clickDown), DispatcherPriority.Send);

                // Both edges, like the plain click key, so hold mode works.
                if (comboDown != comboWasDown)
                    Dispatcher.InvokeAsync(() => OnComboHotkey(comboDown), DispatcherPriority.Send);

                if (recordDown && !recordWasDown)
                    Dispatcher.InvokeAsync(OnRecordHotkey, DispatcherPriority.Send);

                if (replayDown && !replayWasDown)
                    Dispatcher.InvokeAsync(OnReplayHotkey, DispatcherPriority.Send);
            }

            // Updated every pass, armed or not, so the edge is always measured
            // against the poll before it rather than against whenever the last
            // action happened to fire.
            clickWasDown = clickDown;
            recordWasDown = recordDown;
            replayWasDown = replayDown;
            comboWasDown = comboDown;
            wasArmed = s.HotkeysArmed;

            Thread.Sleep(HotkeyPollMs);
        }
    }

    /// <summary>How close to a corner counts as being in it.</summary>
    private const int CornerStopMargin = 2;

    /// <summary>
    /// True while the pointer is sitting in a corner of the desktop.
    /// </summary>
    /// <remarks>
    /// Corners rather than edges. With two monitors side by side an edge gets
    /// crossed constantly in normal play, and the top edge is where menus and
    /// title bars live — a failsafe that fires by accident mid-game is worse
    /// than no failsafe at all. A corner takes deliberately throwing the mouse
    /// into it, which is exactly the gesture wanted.
    ///
    /// Measured with GetSystemMetrics rather than SystemParameters because this
    /// runs on the poll thread, and the WPF statics are not its to touch.
    /// </remarks>
    private static bool PointerInCorner()
    {
        if (!GetCursorPos(out POINT p)) return false;

        int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int right = left + GetSystemMetrics(SM_CXVIRTUALSCREEN) - 1;
        int bottom = top + GetSystemMetrics(SM_CYVIRTUALSCREEN) - 1;

        bool nearLeftOrRight = p.X - left <= CornerStopMargin || right - p.X <= CornerStopMargin;
        bool nearTopOrBottom = p.Y - top <= CornerStopMargin || bottom - p.Y <= CornerStopMargin;

        // Both, not either — either one of them is an edge.
        return nearLeftOrRight && nearTopOrBottom;
    }

    private void OnClickHotkey(bool pressed)
    {
        // A bound key being typed into a value box must not also fire the hotkey.
        if (IsActive && Keyboard.FocusedElement is TextBox) return;

        if (pressed)
        {
            if (_holdMode)
            {
                if (!_running) StartClicking();
            }
            else
            {
                ToggleRunning();
            }
        }
        else if (_holdMode && _running)
        {
            StopClicking();
        }
    }

    /// <summary>
    /// Saves what already happened. The buffer is always rolling while enabled,
    /// so this is a stream copy of segments that are already on disk — it takes
    /// tens of milliseconds, not the length of the clip.
    /// </summary>
    private async void OnReplayHotkey()
    {
        if (!_replay.IsRunning)
        {
            ShowReplayStatus("Instant replay is off — turn it on first.", isError: true);
            return;
        }

        ShowReplayStatus("Saving…", isError: false);

        string? path = await _replay.SaveLastAsync(ReplaySeconds, ClipFolder);

        if (path == null)
        {
            ShowReplayStatus("Nothing saved — the buffer had no usable footage yet.", isError: true);
            return;
        }

        var file = new FileInfo(path);
        _chosenClipPath = path;

        ChosenClipText.Text = $"{file.Name} — {file.Length / 1024.0 / 1024.0:0.0} MB";
        UploadClipButton.IsEnabled = file.Length > 0 && file.Length <= ClipUploader.MaxBytes;

        ShowReplayStatus($"Saved the last {ReplaySeconds}s — ready to upload.", isError: false);
    }

    // A second click on an armed button cancels rather than re-arming.
    private void Hotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_rebinding == RebindTarget.Click) CancelRebind();
        else BeginRebind(RebindTarget.Click);
    }

    private void ReplayHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_rebinding == RebindTarget.Replay) CancelRebind();
        else BeginRebind(RebindTarget.Replay);
    }

    // The button itself becomes the prompt, so no dialog interrupts the flow.
    private void BeginRebind(RebindTarget target)
    {
        _rebinding = target;
        RebindButtonFor(target).Content = "Select A Hotkey";

        // Load-bearing. The poll thread reads HotkeysArmed from the snapshot,
        // and only this republishes it — without the call the key being chosen
        // still fires its action while it is being chosen, so binding the click
        // key starts the clicker instead of binding anything.
        UpdateEngineSettings();
    }

    private void HotkeysEnabled_Changed(object sender, RoutedEventArgs e) => UpdateEngineSettings();

    private void RecordHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_rebinding == RebindTarget.Record) CancelRebind();
        else BeginRebind(RebindTarget.Record);
    }

    private void ComboHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_rebinding == RebindTarget.Combo) CancelRebind();
        else BeginRebind(RebindTarget.Combo);
    }

    /// <summary>
    /// Starts and stops the clicker and shake as one action.
    /// </summary>
    /// <remarks>
    /// Deliberately symmetric: whatever it turns on it turns off again. Leaving
    /// shake ticked after stopping would mean the plain click key silently
    /// started shaking too the next time it was used.
    ///
    /// Shake is set through the checkbox rather than a field because its handler
    /// is what republishes the engine snapshot the shake thread actually reads.
    /// </remarks>
    private void OnComboHotkey(bool pressed)
    {
        if (IsActive && Keyboard.FocusedElement is TextBox) return;

        if (pressed)
        {
            if (_holdMode)
            {
                if (!_running) SetShaky(true);
                if (!_running) StartClicking();
                return;
            }

            if (_running)
            {
                StopClicking();
                SetShaky(false);
            }
            else
            {
                SetShaky(true);
                StartClicking();
            }

            return;
        }

        if (_holdMode && _running)
        {
            StopClicking();
            SetShaky(false);
        }
    }

    private void SetShaky(bool on)
    {
        if (ShakyTracking != null) ShakyTracking.IsChecked = on;
    }

    private Button RebindButtonFor(RebindTarget target) => target switch
    {
        RebindTarget.Replay => ReplayHotkeyButton,
        RebindTarget.Record => RecordHotkeyButton,
        RebindTarget.Combo => ComboHotkeyButton,
        _ => HotkeyButton
    };

    /// <summary>
    /// Unbinds any hotkey repeating one an earlier action already claims.
    /// </summary>
    /// <remarks>
    /// The rebind path rejects collisions, but a settings file written before an
    /// action existed cannot know that action's default is already taken — and a
    /// key bound twice fires both, which looks like the app malfunctioning
    /// rather than like a clash. Earlier in the list wins, so the clicker keeps
    /// its key and the newer action is the one that loses.
    /// </remarks>
    private void DropDuplicateHotkeys()
    {
        var claimed = new HashSet<int>();

        foreach ((RebindTarget target, HotkeyBinding binding) in AllBindings())
        {
            if (!binding.IsValid || claimed.Add(binding.VirtualKey)) continue;

            switch (target)
            {
                case RebindTarget.Click: _hotkeySettings.Hotkey = HotkeyBinding.Unbound; break;
                case RebindTarget.Record: _hotkeySettings.RecordHotkey = HotkeyBinding.Unbound; break;
                case RebindTarget.Combo: _hotkeySettings.ComboHotkey = HotkeyBinding.Unbound; break;
                default: _hotkeySettings.ReplayHotkey = HotkeyBinding.Unbound; break;
            }
        }
    }

    /// <summary>
    /// Every action and its current binding. One place, so the collision check
    /// and the button labels cannot disagree about how many actions exist.
    /// </summary>
    private (RebindTarget Target, HotkeyBinding Binding)[] AllBindings() => new[]
    {
        (RebindTarget.Click, _hotkeySettings.Hotkey),
        (RebindTarget.Replay, _hotkeySettings.ReplayHotkey),
        (RebindTarget.Record, _hotkeySettings.RecordHotkey),
        (RebindTarget.Combo, _hotkeySettings.ComboHotkey)
    };

    /// <summary>How long a refused rebind sits on the button before reverting.</summary>
    private static readonly TimeSpan RebindNoticeDuration = TimeSpan.FromSeconds(1.6);

    private DispatcherTimer? _rebindNoticeTimer;

    /// <summary>
    /// Says no on the button that asked the question.
    /// </summary>
    /// <remarks>
    /// The button is already the prompt — it reads "Select A Hotkey" while it
    /// waits — so it is the honest place for the answer too. A modal dialog took
    /// focus and had to be dismissed before another key could be tried, which is
    /// a lot of ceremony for "pick a different one".
    ///
    /// The wording is kept short deliberately: every rebind button sizes to its
    /// content with a MinWidth floor, and a longer message would make the button
    /// jump wider and snap back.
    /// </remarks>
    private void ShowRebindRefused(RebindTarget target)
    {
        Button button = RebindButtonFor(target);

        button.Content = "In use";
        button.Foreground = (Brush)FindResource("Accent");

        // Restarted rather than stacked, so trying three keys in a row leaves
        // one pending revert instead of three fighting over the label.
        _rebindNoticeTimer?.Stop();
        _rebindNoticeTimer = new DispatcherTimer { Interval = RebindNoticeDuration };

        _rebindNoticeTimer.Tick += (_, _) =>
        {
            _rebindNoticeTimer?.Stop();
            _rebindNoticeTimer = null;

            button.ClearValue(Control.ForegroundProperty);
            ApplyHotkeyToUi();
        };

        _rebindNoticeTimer.Start();
    }

    private void CancelRebind()
    {
        _rebinding = RebindTarget.None;
        ApplyHotkeyToUi();

        // Re-arms the poll thread, which BeginRebind disarmed.
        UpdateEngineSettings();
    }

    /// <summary>
    /// Runs on PreviewKeyDown, ahead of the focused control. A Button treats
    /// Space and Enter as activation on KeyDown, so capturing there would let
    /// the rebind button re-trigger itself instead of binding the key.
    /// </summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_rebinding == RebindTarget.None) return;

        e.Handled = true;
        if (e.IsRepeat) return;

        // Escape is the emergency stop and is checked ahead of everything, so
        // binding it would produce a key that silently never fires.
        if (e.Key == Key.Escape) CancelRebind();
        else CaptureRebind(HotkeyBinding.FromKey(e.Key));
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_rebinding == RebindTarget.None) return;

        // Only the side buttons are bindable. Left in particular is the button
        // this app synthesises, and binding it would be self-triggering.
        HotkeyBinding? binding = HotkeyBinding.FromMouse(e.ChangedButton);
        if (binding == null) return;

        e.Handled = true;
        CaptureRebind(binding);
    }

    private void CaptureRebind(HotkeyBinding binding)
    {
        RebindTarget target = _rebinding;
        _rebinding = RebindTarget.None;

        if (!binding.IsValid)
        {
            CancelRebind();
            return;
        }

        // Every other binding, not just one — a key could collide with any of
        // the actions it is not replacing. Built from the same table that does
        // the assignment, so adding an action cannot leave a gap here.
        if (AllBindings().Any(b => b.Target != target && b.Binding.VirtualKey == binding.VirtualKey))
        {
            CancelRebind();
            ShowRebindRefused(target);
            return;
        }

        switch (target)
        {
            case RebindTarget.Click: _hotkeySettings.Hotkey = binding; break;
            case RebindTarget.Record: _hotkeySettings.RecordHotkey = binding; break;
            case RebindTarget.Combo: _hotkeySettings.ComboHotkey = binding; break;
            default: _hotkeySettings.ReplayHotkey = binding; break;
        }

        _hotkeySettings.Save();

        // The poll thread re-primes its own edge state while disarmed, so
        // releasing the just-bound key cannot read as a fresh press.
        // The badge and button now read the new binding, which is the confirmation.
        ApplyHotkeyToUi();
        UpdateEngineSettings();

        // Otherwise the rebind button keeps focus and swallows the next Space.
        Focus();
    }

    private void ToggleRunning()
    {
        if (_running) StopClicking();
        else StartClicking();
    }

    private void StartClicking()
    {
        if (_running) return;

        _running = true;
        RefreshStatus();
        UpdateEngineSettings();
        _clickCts = new CancellationTokenSource();

        // Its own thread, not the dispatcher: the spin-wait below would
        // otherwise block rendering and freeze the window while clicking.
        CancellationToken token = _clickCts.Token;
        new Thread(() => ClickLoop(token))
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
            Name = "ClickEngine"
        }.Start();

    }

    /// <summary>
    /// Travel allowed in each direction, in pixels. Windows mouse deltas run
    /// positive-right and positive-down, so Up maps to a negative dy.
    /// </summary>
    private sealed record ShakeRange(double Left, double Right, double Up, double Down)
    {
        public bool IsZero => Left <= 0 && Right <= 0 && Up <= 0 && Down <= 0;
    }

    /// <summary>
    /// Snapshot of everything the engine threads need. UI controls have thread
    /// affinity, so the engines read this instead of touching them.
    /// </summary>
    private sealed record ClickSettings(
        double Cps, double Duty, bool Shaky, ShakeRange Shake, double ShakeSpeed,
        bool UltraAccuracy, bool HoldMode,
        int HotkeyVk, int ReplayHotkeyVk, int RecordHotkeyVk,
        int ComboHotkeyVk, bool HotkeysArmed);

    private volatile ClickSettings _settings =
        new(10.0, 0.67, false, new ShakeRange(8, 20, 40, 8), 33, false, false, VK_F6, 0, 0, 0, false);

    private void ApplyAppSettings(AppSettings s)
    {
        CpsSlider.Value = Math.Clamp(s.Cps, CpsSlider.Minimum, CpsSlider.Maximum);
        CdcSlider.Value = Math.Clamp(s.Cdc, CdcSlider.Minimum, CdcSlider.Maximum);

        _shakeLeft = Math.Clamp(s.ShakeLeft, 0, MaxShakePixels);
        _shakeRight = Math.Clamp(s.ShakeRight, 0, MaxShakePixels);
        _shakeUp = Math.Clamp(s.ShakeUp, 0, MaxShakePixels);
        _shakeDown = Math.Clamp(s.ShakeDown, 0, MaxShakePixels);
        _shakeSpeed = Math.Clamp(s.ShakeSpeed, MinShakeSpeed, MaxShakeSpeed);
        WriteShakeBoxes();

        _savedShake = s.HasSavedShake
            ? new ShakeRange(
                Math.Clamp(s.SavedShakeLeft, 0, MaxShakePixels),
                Math.Clamp(s.SavedShakeRight, 0, MaxShakePixels),
                Math.Clamp(s.SavedShakeUp, 0, MaxShakePixels),
                Math.Clamp(s.SavedShakeDown, 0, MaxShakePixels))
            : null;

        UpdateShakeButtons();

        ShakyTracking.IsChecked = s.ShakyTracking;
        UltraAccuracy.IsChecked = s.UltraAccuracy;
        PingSync.IsChecked = s.PingSync;
        HitFix.IsChecked = s.HitFix;
        HotkeysEnabledToggle.IsChecked = s.HotkeysEnabled;

        SetClickMode(s.HoldMode);

        _valuesHidden = s.HideValues;
        ApplyValueVisibility();

        // Falls back rather than trusting the file: a value with no matching
        // button would leave the radio group and the stored length disagreeing.
        if (!SelectReplayLength(s.ReplaySeconds)) SelectReplayLength(30);

        // A monitor that has been unplugged since matches nothing, so the whole
        // desktop is the fallback rather than a crop that no longer exists.
        if (!SelectDisplay(s.RecordDisplay)) SelectDisplay(null);

        ClipFolderBox.Text = string.IsNullOrWhiteSpace(s.ClipFolder) ? DefaultClipFolder : s.ClipFolder;

        if (!SelectRecordFps(s.RecordFps)) SelectRecordFps(30);

        // Same posture toward the stored accent: a colour matching no swatch
        // would otherwise leave the row with nothing ringed.
        // Checking the button applies the mode. Dark is already checked in the
        // XAML, so a stored dark raises no event — and needs none, since the
        // palette declared there is the dark one.
        if (s.LightTheme) ThemeLight.IsChecked = true;

        if (!SelectAccent(s.AccentColor)) SelectAccent(DefaultAccent);

        // Clamped to the slider's own range rather than trusted: the floor is
        // what stops a stored zero from bringing back an invisible window.
        OpacitySlider.Value = Math.Clamp(s.WindowOpacity, OpacitySlider.Minimum, OpacitySlider.Maximum);

        // Deliberately not restored from the file. Ticking this box starts an
        // ffmpeg gdigrab capture of the whole desktop, and a GDI screen grab
        // makes the mouse cursor flicker for as long as it runs — so restoring
        // it meant the pointer blinked from the moment the app was opened.
        // Recording the screen is opt-in per run, not a remembered setting.

        RestoreWindowPlacement(s);

        UpdateShakeLabel();
        UpdatePingLabel();
        UpdateEngineSettings();
    }

    /// <summary>Checks the matching length button. False when none matches.</summary>
    private bool SelectReplayLength(int seconds)
    {
        foreach (object child in ReplayLengthPanel.Children)
        {
            if (child is RadioButton { Tag: string tag } button
                && int.TryParse(tag, out int value)
                && value == seconds)
            {
                // Checking it raises Checked, which sets ReplaySeconds — so the
                // property and the button can never drift apart.
                button.IsChecked = true;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Restores size and position, but only onto a screen that still exists —
    /// a window remembered on a monitor since unplugged would open off-screen
    /// with no way to drag it back.
    /// </summary>
    private void RestoreWindowPlacement(AppSettings s)
    {
        if (s.WindowWidth is > 0 and { } width) Width = Math.Max(MinWidth, width);
        if (s.WindowHeight is > 0 and { } height) Height = Math.Max(MinHeight, height);

        if (s.WindowLeft is { } left && s.WindowTop is { } top)
        {
            double virtualLeft = SystemParameters.VirtualScreenLeft;
            double virtualTop = SystemParameters.VirtualScreenTop;
            double virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
            double virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

            bool onScreen = left + Width > virtualLeft && left < virtualRight
                            && top + Height > virtualTop && top < virtualBottom;

            if (onScreen)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
            }
        }

        if (s.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void SaveAppSettings()
    {
        if (!_settingsLoaded) return;

        new AppSettings
        {
            Cps = CpsSlider.Value,
            Cdc = CdcSlider.Value,
            ShakeLeft = _shakeLeft,
            ShakeRight = _shakeRight,
            ShakeUp = _shakeUp,
            ShakeDown = _shakeDown,
            ShakeSpeed = _shakeSpeed,
            HasSavedShake = _savedShake != null,
            SavedShakeLeft = _savedShake?.Left ?? 0,
            SavedShakeRight = _savedShake?.Right ?? 0,
            SavedShakeUp = _savedShake?.Up ?? 0,
            SavedShakeDown = _savedShake?.Down ?? 0,
            ShakyTracking = ShakyTracking.IsChecked == true,
            UltraAccuracy = UltraAccuracy.IsChecked == true,
            PingSync = PingSync.IsChecked == true,
            HitFix = HitFix.IsChecked == true,
            HoldMode = _holdMode,
            HideValues = _valuesHidden,
            ReplayEnabled = ReplayEnabled.IsChecked == true,
            ReplaySeconds = ReplaySeconds,
            AccentColor = _accentHex,
            WindowOpacity = OpacitySlider.Value,
            LightTheme = ThemeLight.IsChecked == true,
            RecordDisplay = _captureDisplay?.DeviceName,
            HotkeysEnabled = HotkeysEnabledToggle.IsChecked == true,
            ClipFolder = ClipFolderBox.Text.Trim(),
            RecordFps = RecordFps,
            // RestoreBounds rather than Width/Height: while maximised those
            // report the maximised size, which would be restored as the
            // "normal" size next launch.
            WindowWidth = RestoreBounds.Width > 0 ? RestoreBounds.Width : Width,
            WindowHeight = RestoreBounds.Height > 0 ? RestoreBounds.Height : Height,
            WindowLeft = RestoreBounds.Width > 0 ? RestoreBounds.Left : Left,
            WindowTop = RestoreBounds.Width > 0 ? RestoreBounds.Top : Top,
            WindowMaximized = WindowState == WindowState.Maximized
        }.Save();

        _settingsDirty = false;
    }

    // Called on the UI thread whenever anything the engine reads changes.
    private void UpdateEngineSettings()
    {
        if (CpsSlider == null || CdcSlider == null) return;

        // Every change point already routes through here, so this is the one
        // place that needs to notice the configuration moved.
        _settingsDirty = true;

        // Ping Sync forces the spin-wait on while latency is high, so the only
        // variance left in the click stream is the network's, not ours.
        bool spin = UltraAccuracy?.IsChecked == true
                    || (PingSync?.IsChecked == true && _lastPingMs >= HighPingMs);

        _settings = new ClickSettings(
            CpsSlider.Value,
            Math.Clamp(CdcSlider.Value / 100.0, 0.0, 1.0),
            ShakyTracking?.IsChecked == true,
            new ShakeRange(_shakeLeft, _shakeRight, _shakeUp, _shakeDown),
            Math.Clamp(_shakeSpeed, MinShakeSpeed, MaxShakeSpeed),
            spin,
            _holdMode,
            _hotkeySettings.Hotkey.VirtualKey,
            _hotkeySettings.ReplayHotkey.VirtualKey,
            _hotkeySettings.RecordHotkey.VirtualKey,
            _hotkeySettings.ComboHotkey.VirtualKey,
            // Armed only when nothing is being rebound and the master switch is
            // on. Null-conditional because this runs once from the constructor,
            // before every control is necessarily built.
            _rebinding == RebindTarget.None && HotkeysEnabledToggle?.IsChecked != false);
    }

    private void EngineSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (PingSync?.IsChecked != true) _lastPingMs = -1;
        UpdatePingLabel();
        UpdateShakeLabel();
        UpdateEngineSettings();

        // The toggles are part of a preset now, so changing one can make the
        // applied preset stop matching.
        RefreshAppliedPreset();
    }

    /// <summary>
    /// Guards the sliders against the code that writes them.
    /// </summary>
    /// <remarks>
    /// Setting Slider.Value raises ValueChanged, which would write straight back
    /// into the field the write came from. Harmless for the value itself, but it
    /// would republish the engine snapshot and re-evaluate the applied preset on
    /// every step of a restore.
    /// </remarks>
    private bool _writingShakeUi;

    private void ShakeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Fires during parsing, as each slider's Value attribute is applied.
        // The test is the LAST of the group to be built, not the first: guarding
        // on an earlier one lets the speed slider through while the readouts
        // after it are still null, which is a crash inside WriteShakeBoxes.
        if (_writingShakeUi || ShakeSpeedValue == null) return;

        double value = Math.Round(e.NewValue);

        if (sender == ShakeLeftSlider) _shakeLeft = value;
        else if (sender == ShakeRightSlider) _shakeRight = value;
        else if (sender == ShakeUpSlider) _shakeUp = value;
        else if (sender == ShakeDownSlider) _shakeDown = value;
        else if (sender == ShakeSpeedSlider) _shakeSpeed = value;
        else return;

        WriteShakeBoxes();
        UpdateShakeLabel();
        UpdateEngineSettings();
        RefreshAppliedPreset();
    }

    /// <summary>Pushes the stored values back onto the sliders and readouts.</summary>
    /// <remarks>
    /// Guarded on the last control of the group, so a half-built tree during
    /// XAML parsing cannot reach the ones that do not exist yet.
    /// </remarks>
    private void WriteShakeBoxes()
    {
        if (ShakeSpeedValue == null) return;

        _writingShakeUi = true;

        try
        {
            ShakeLeftSlider.Value = _shakeLeft;
            ShakeRightSlider.Value = _shakeRight;
            ShakeUpSlider.Value = _shakeUp;
            ShakeDownSlider.Value = _shakeDown;
            ShakeSpeedSlider.Value = _shakeSpeed;
        }
        finally
        {
            _writingShakeUi = false;
        }

        ShakeLeftValue.Text = Format(_shakeLeft);
        ShakeRightValue.Text = Format(_shakeRight);
        ShakeUpValue.Text = Format(_shakeUp);
        ShakeDownValue.Text = Format(_shakeDown);
        ShakeSpeedValue.Text = Format(_shakeSpeed) + "/s";

        static string Format(double v) => ((int)Math.Round(v)).ToString(CultureInfo.CurrentCulture);
    }

    private void SaveShake_Click(object sender, RoutedEventArgs e)
    {
        _savedShake = new ShakeRange(_shakeLeft, _shakeRight, _shakeUp, _shakeDown);
        _settingsDirty = true;
        UpdateShakeButtons();
    }

    private void RestoreShake_Click(object sender, RoutedEventArgs e)
    {
        if (_savedShake == null) return;

        _shakeLeft = _savedShake.Left;
        _shakeRight = _savedShake.Right;
        _shakeUp = _savedShake.Up;
        _shakeDown = _savedShake.Down;

        WriteShakeBoxes();
        UpdateShakeLabel();
        UpdateEngineSettings();
    }

    /// <summary>
    /// Restore stays disabled until something has been saved, and its tooltip
    /// carries the stored numbers — the status line is rewritten every tick, so
    /// a transient "saved!" message there would vanish before it was read.
    /// </summary>
    private void UpdateShakeButtons()
    {
        if (RestoreShakeButton == null) return;

        RestoreShakeButton.IsEnabled = _savedShake != null;

        RestoreShakeButton.ToolTip = _savedShake == null
            ? "Nothing saved yet"
            : $"Restore L{_savedShake.Left:0} R{_savedShake.Right:0} " +
              $"U{_savedShake.Up:0} D{_savedShake.Down:0}";
    }

    private void UpdateShakeLabel()
    {
        if (ShakeStatusText == null) return;

        // The pixel values are not repeated here — the four fields below already
        // show them, and two copies of the same number can disagree.
        if (_shakeLeft <= 0 && _shakeRight <= 0 && _shakeUp <= 0 && _shakeDown <= 0)
        {
            ShakeStatusText.Text = "All four directions are zero — no camera movement";
            return;
        }

        if (ShakyTracking?.IsChecked != true)
        {
            ShakeStatusText.Text = "Only moves while the clicker runs and Roblox has the mouse locked";
            return;
        }

        // Naming the failing condition, rather than a single "waiting", is what
        // makes this diagnosable without attaching a debugger.
        ShakeStatusText.Text = _shakeGate switch
        {
            ShakeGate.Ready => $"Active — moving about {_shakeSpeed:0} times a second",
            ShakeGate.ClickerOff => "Waiting — the clicker is not running",
            ShakeGate.MouseFree => "Waiting — Roblox is in front but not in first person",
            _ => "Waiting — Roblox is not the front window"
        };
    }

    /// <summary>
    /// One ICMP probe per stats tick, only while Ping Sync is on. Skipped if the
    /// previous probe has not returned, so a slow link cannot queue them up.
    /// </summary>
    private async void ProbePingAsync()
    {
        if (_pingInFlight || PingSync?.IsChecked != true) return;

        _pingInFlight = true;
        try
        {
            using var ping = new Ping();
            PingReply reply = await ping.SendPingAsync(PingHost, 1000);
            _lastPingMs = reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : -1;
        }
        catch
        {
            _lastPingMs = -1;
        }
        finally
        {
            _pingInFlight = false;
        }

        UpdatePingLabel();
        UpdateEngineSettings();
    }

    private void UpdatePingLabel()
    {
        if (PingStatusText == null) return;

        if (PingSync?.IsChecked != true)
        {
            PingStatusText.Text = "Holds maximum precision while latency is high";
            return;
        }

        int ms = _lastPingMs;

        PingStatusText.Text = ms < 0
            ? $"No reply from {PingHost}"
            : ms >= HighPingMs
                ? $"Latency {ms} ms — holding maximum precision"
                : $"Latency {ms} ms";
    }

    private void StopClicking()
    {
        _running = false;
        _clickCts?.Cancel();
        _clickCts?.Dispose();
        _clickCts = null;
        RefreshStatus();
    }

    // The dot only carries the accent colour while clicking is actually live.
    private void SetStatus(string text, string dotBrushKey)
    {
        StatusText.Text = text;
        StatusDot.Fill = (System.Windows.Media.Brush)FindResource(dotBrushKey);
    }

    private void ClickLoop(CancellationToken token)
    {
        // Tracked so a stop between the press and the release can still send the
        // release. Without this, cancelling mid-click leaves the button logically
        // held down across the whole desktop.
        bool buttonDown = false;

        // Takes the system timer from ~15.6 ms to ~1 ms for this process. Without
        // it the loop cannot exceed ~32 clicks per second whatever the slider says.
        bool raisedTimer = TimeBeginPeriod(TimerResolutionMs) == 0;

        // Timestamp the current run of actual clicking started, or 0 when idle.
        // History counts only these runs, so being armed or open costs nothing.
        long activeSince = 0;

        try
        {
            long freq = Stopwatch.Frequency;
            long deadline = Stopwatch.GetTimestamp();

            while (!token.IsCancellationRequested)
            {
                ClickSettings s = _settings;

                if (s.HoldMode && !IsKeyDown(s.HotkeyVk))
                {
                    BankActiveTime(ref activeSince);
                    Thread.Sleep(5);
                    deadline = Stopwatch.GetTimestamp();
                    continue;
                }

                // The slider bottoms out at 0, which means "armed but not
                // clicking". Guarding here matters: 1000.0 / 0 is Infinity, and
                // casting that to int wraps to a 1 ms delay — the opposite of
                // what the user asked for.
                if (s.Cps < MinimumCps)
                {
                    BankActiveTime(ref activeSince);
                    Thread.Sleep(50);
                    deadline = Stopwatch.GetTimestamp();
                    continue;
                }

                if (activeSince == 0) activeSince = Stopwatch.GetTimestamp();

                // If the loop has fallen badly behind — a long stall, or the rate
                // was just raised — resync rather than bursting to catch up.
                long now = Stopwatch.GetTimestamp();
                if (deadline < now - freq / 10) deadline = now;

                double period = 1000.0 / s.Cps;

                // Click Duty Cycle: the share of each period the button is held.
                double downMs = period * s.Duty;

                SendLeftDown();
                buttonDown = true;
                deadline += (long)(downMs * freq / 1000.0);
                if (!WaitUntil(deadline, s.UltraAccuracy, token)) break;

                SendLeftUp();
                buttonDown = false;
                Interlocked.Increment(ref _clickCount);
                deadline += (long)((period - downMs) * freq / 1000.0);
                if (!WaitUntil(deadline, s.UltraAccuracy, token)) break;
            }
        }
        catch
        {
            Dispatcher.InvokeAsync(StopClicking);
        }
        finally
        {
            // Banked here too, or the final partial run would be lost on stop.
            BankActiveTime(ref activeSince);

            if (buttonDown) SendLeftUp();
            if (raisedTimer) TimeEndPeriod(TimerResolutionMs);
        }
    }

    private void BankActiveTime(ref long activeSince)
    {
        if (activeSince == 0) return;

        Interlocked.Add(ref _activeTicks, Stopwatch.GetTimestamp() - activeSince);
        activeSince = 0;
    }

    private static int NextOffset(double min, double max) =>
        (int)Math.Round(min + Random.Shared.NextDouble() * (max - min));

    private enum ShakeGate { Ready, NotRoblox, MouseFree, ClickerOff }

    /// <summary>
    /// Whether the shake engine is allowed to move the camera right now.
    /// </summary>
    /// <remarks>
    /// Detecting "first person" from outside the game is a heuristic. The
    /// obvious test — is the system cursor hidden — does not work: Roblox swaps
    /// the cursor bitmap (arrow, crosshair) rather than hiding the cursor, so
    /// CURSOR_SHOWING stays set the whole time and a gate on it never opens.
    ///
    /// What does distinguish a camera-locked mouse is that Roblox keeps
    /// recentring it, so the pointer sits on the window centre instead of
    /// wandering. Cursor-hidden is still accepted, for games that do hide it.
    ///
    /// This is as close to "first person" as the outside of the process gets,
    /// and it is not the same thing. Shift-lock and right-mouse look hold the
    /// cursor identically, and nothing observable from here — window, cursor,
    /// process — separates them from a first-person camera. The gate means the
    /// camera owns the mouse, which is the condition shake actually needs.
    /// </remarks>
    private ShakeGate ReadShakeGate()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return ShakeGate.NotRoblox;

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return ShakeGate.NotRoblox;

        // Resolving a pid to a name is comparatively expensive, and this runs
        // every 20-40 ms, so the answer is cached until the pid changes.
        if (pid != _cachedForegroundPid)
        {
            _cachedForegroundPid = pid;
            _cachedForegroundIsRoblox = false;

            try
            {
                using Process p = Process.GetProcessById((int)pid);
                _cachedForegroundIsRoblox =
                    p.ProcessName.Contains("roblox", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                // Process exited, or is not ours to inspect.
            }
        }

        if (!_cachedForegroundIsRoblox) return ShakeGate.NotRoblox;

        CURSORINFO info = default;
        info.cbSize = Marshal.SizeOf<CURSORINFO>();

        if (!GetCursorInfo(ref info)) return ShakeGate.MouseFree;

        // The load-bearing test. While the camera owns the mouse Roblox keeps
        // yanking the pointer back to the middle of its window; a free mouse
        // wanders. Without this a changed cursor bitmap alone opens the gate,
        // and Roblox changes it over buttons and the chat box too.
        if (!PointerHeldAtWindowCentre(hwnd, info.ptScreenPos)) return ShakeGate.MouseFree;

        // Some games hide the pointer outright.
        if ((info.flags & CURSOR_SHOWING) == 0) return ShakeGate.Ready;

        // Roblox does not. It swaps the cursor bitmap: the ordinary Windows
        // arrow while the mouse is free, its own crosshair once the camera owns
        // the mouse. So the test is which cursor is displayed, not whether one
        // is displayed at all.
        return info.hCursor != IntPtr.Zero && info.hCursor != SystemArrowCursor()
            ? ShakeGate.Ready
            : ShakeGate.MouseFree;
    }

    /// <summary>How far off centre still counts as pinned.</summary>
    /// <remarks>
    /// Small on purpose. Measured against a live client, a camera-locked pointer
    /// reads exactly on centre on every sample, while a free mouse crosses the
    /// middle of the window constantly on its way elsewhere. An earlier version
    /// allowed an eighth of the window and let a free mouse open the gate — the
    /// bug this replaces.
    /// </remarks>
    private const int CentreTolerancePx = 8;

    /// <summary>
    /// How long a centred reading keeps the gate open once it has been seen.
    /// </summary>
    /// <remarks>
    /// Shake displaces the pointer itself, and the client needs a frame to pull
    /// it back. Without this grace the gate would flicker shut on this app's own
    /// movement and stall the shake it exists to permit.
    /// </remarks>
    private static readonly TimeSpan CentreGrace = TimeSpan.FromMilliseconds(400);

    private long _lastCentredTimestamp;

    /// <summary>True while the pointer is being held at the centre of a window.</summary>
    private bool PointerHeldAtWindowCentre(IntPtr hwnd, POINT pointer)
    {
        // The client rectangle, not the window rectangle: borders and title bar
        // shift the window's midpoint away from the one the game recentres to.
        if (GetClientRect(hwnd, out RECT client))
        {
            POINT origin = default;

            if (ClientToScreen(hwnd, ref origin))
            {
                int centreX = origin.X + (client.Right - client.Left) / 2;
                int centreY = origin.Y + (client.Bottom - client.Top) / 2;

                if (Math.Abs(pointer.X - centreX) <= CentreTolerancePx
                    && Math.Abs(pointer.Y - centreY) <= CentreTolerancePx)
                {
                    _lastCentredTimestamp = Stopwatch.GetTimestamp();
                    return true;
                }
            }
        }

        long last = _lastCentredTimestamp;

        return last != 0 && Stopwatch.GetElapsedTime(last) <= CentreGrace;
    }

    private static IntPtr SystemArrowCursor()
    {
        // Shared handle, so this is a cheap lookup rather than an allocation.
        return LoadCursor(IntPtr.Zero, IDC_ARROW);
    }

    /// <summary>
    /// Nudges the cursor around its starting point while the clicker runs, so
    /// tracking practice has to fight a moving crosshair.
    /// </summary>
    /// <remarks>
    /// Each step moves to a fresh random offset within the amplitude rather than
    /// taking an independent random step. Independent steps are a random walk —
    /// they accumulate, so the aim drifts away from where it started and never
    /// comes back. This stays bounded.
    /// </remarks>
    private void ShakeLoop(CancellationToken token)
    {
        int offsetX = 0, offsetY = 0;

        try
        {
            while (!token.IsCancellationRequested)
            {
                ClickSettings s = _settings;
                ShakeRange range = s.Shake;

                // The clicker gates shake, checked before anything else. The
                // thread still runs the whole time — it is what returns the
                // cursor to its origin when the clicker stops mid-offset.
                ShakeGate gate =
                    !_running ? ShakeGate.ClickerOff
                    : s.Shaky && !range.IsZero ? ReadShakeGate()
                    : ShakeGate.NotRoblox;
                bool eligible = gate == ShakeGate.Ready;

                _shakeGate = gate;
                _shakeActive = eligible;

                if (!eligible)
                {
                    // Return to origin rather than freezing mid-offset.
                    if (offsetX != 0 || offsetY != 0)
                    {
                        SendMouseMove(-offsetX, -offsetY);
                        offsetX = 0;
                        offsetY = 0;
                    }

                    Thread.Sleep(25);
                    continue;
                }

                int targetX = NextOffset(-range.Left, range.Right);
                int targetY = NextOffset(-range.Up, range.Down);

                int dx = targetX - offsetX;
                int dy = targetY - offsetY;

                if (dx != 0 || dy != 0)
                {
                    SendMouseMove(dx, dy);
                    offsetX = targetX;
                    offsetY = targetY;
                }

                // Jittered around the chosen rate rather than sleeping it exactly.
                // A perfectly fixed interval is both unlike a hand and a clean
                // signature; a quarter either side keeps the average honest.
                int interval = (int)Math.Round(1000.0 / Math.Clamp(s.ShakeSpeed, MinShakeSpeed, MaxShakeSpeed));
                int spread = Math.Max(1, interval / 4);

                Thread.Sleep(Random.Shared.Next(Math.Max(1, interval - spread), interval + spread + 1));
            }
        }
        catch
        {
            // A failed move must never take the clicker down with it.
        }
        finally
        {
            // Undo the outstanding displacement so the crosshair ends where it began.
            if (offsetX != 0 || offsetY != 0) SendMouseMove(-offsetX, -offsetY);

            // Without this the label keeps reporting "Active" after the thread
            // is gone, because nothing else ever clears it.
            _shakeActive = false;
        }
    }

    /// <summary>
    /// Waits until an absolute timestamp. Deadlines accumulate rather than each
    /// cycle sleeping a relative span, so time spent in SendInput and loop
    /// overhead cannot compound into drift.
    /// </summary>
    /// <param name="spin">
    /// Ultra Accuracy. Sleeps down to the last 2 ms then spins, trading CPU for
    /// exact timing. When false a single coarse sleep is used and the overshoot
    /// is absorbed by the next cycle's deadline.
    /// </param>
    /// <returns>False if cancellation was observed.</returns>
    private static bool WaitUntil(long targetTimestamp, bool spin, CancellationToken token)
    {
        long freq = Stopwatch.Frequency;

        while (true)
        {
            if (token.IsCancellationRequested) return false;

            long remaining = targetTimestamp - Stopwatch.GetTimestamp();
            if (remaining <= 0) return true;

            double ms = remaining * 1000.0 / freq;

            if (!spin)
            {
                int whole = (int)ms;
                if (whole <= 0) return true;

                // Sliced rather than slept in one go. At low CPS a single hold
                // can run to seconds, and an uninterruptible sleep there would
                // keep the mouse button physically down that long after a stop.
                Thread.Sleep(Math.Min(whole, CoarseSleepSliceMs));
                continue;
            }

            if (ms > 2.0) Thread.Sleep(1);
            else Thread.SpinWait(40);
        }
    }

    private static bool IsKeyDown(int virtualKey) =>
        virtualKey != 0 && (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    // Mirrors the bound hotkey into the sidebar badge, which used to be the
    // literal string "F6" regardless of what was actually bound.
    private void ApplyHotkeyToUi()
    {
        HotkeyButton.Content = _hotkeySettings.Hotkey.Name;
        ReplayHotkeyButton.Content = _hotkeySettings.ReplayHotkey.Name;
        RecordHotkeyButton.Content = _hotkeySettings.RecordHotkey.Name;
        ComboHotkeyButton.Content = _hotkeySettings.ComboHotkey.Name;

        // The bindings live on three different pages, so Settings is the only
        // place they can all be read at once.
        if (HotkeySummaryText != null)
        {
            HotkeySummaryText.Text = string.Join("     ",
                $"{_hotkeySettings.Hotkey.Name} — click",
                $"{_hotkeySettings.ReplayHotkey.Name} — replay",
                $"{_hotkeySettings.RecordHotkey.Name} — record",
                $"{_hotkeySettings.ComboHotkey.Name} — click + shake");
        }

        RefreshStatus();
    }

    /// <summary>
    /// ARMED distinguishes hold mode's two live states: started but waiting on
    /// the key, versus actually clicking. Refreshed on every stats tick so the
    /// armed case cannot show a stale RUNNING.
    /// </summary>
    private void RefreshStatus()
    {
        string key = _hotkeySettings.Hotkey.Name;

        if (!_running)
        {
            SetStatus("IDLE", "TextMuted");
            StartStopButton.Content = $"{key}  START";
            return;
        }

        bool armed = _holdMode && !IsKeyDown(_hotkeySettings.Hotkey.VirtualKey);
        SetStatus(armed ? "ARMED" : "RUNNING", armed ? "TextMuted" : "Accent");
        StartStopButton.Content = $"{key}  STOP";
    }

    // Press and release are separate calls so the duty cycle can put real time
    // between them. Sending both at once always produced a zero-length hold.
    private static void SendLeftDown() => SendMouseEvent(MOUSEEVENTF_LEFTDOWN);
    private static void SendLeftUp() => SendMouseEvent(MOUSEEVENTF_LEFTUP);

    private static void SendMouseEvent(uint flags)
    {
        INPUT[] inputs =
        {
            new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT { dwFlags = flags }
                }
            }
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    // Relative move: MOUSEEVENTF_ABSOLUTE is deliberately absent, so dx/dy are
    // offsets from the current position rather than screen coordinates.
    private static void SendMouseMove(int dx, int dy)
    {
        INPUT[] inputs =
        {
            new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = MOUSEEVENTF_MOVE }
                }
            }
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private void ToggleMode_Checked(object sender, RoutedEventArgs e) => SetClickMode(hold: false);

    private void HoldMode_Checked(object sender, RoutedEventArgs e) => SetClickMode(hold: true);

    /// <summary>
    /// Selection lives in the radio group's IsChecked now, so this only records
    /// the mode — no more shadowing it in a Background brush.
    /// </summary>
    private void SetClickMode(bool hold)
    {
        _holdMode = hold;

        if (HoldModeButton != null && ToggleModeButton != null)
        {
            RadioButton wanted = hold ? HoldModeButton : ToggleModeButton;
            if (wanted.IsChecked != true) wanted.IsChecked = true;
        }

        UpdateEngineSettings();
    }

    /// <summary>The highlighted card. Null means nothing is highlighted.</summary>
    private ClickPreset? _selectedPreset;

    private void PresetCard_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ClickPreset preset) return;

        // Second click clears the highlight only. Settings stay exactly as they
        // are — this is deselecting a card, not undoing anything.
        if (ReferenceEquals(preset, _selectedPreset))
        {
            _selectedPreset = null;
            RefreshAppliedPreset();
            ShowPresetHint($"Unselected \"{preset.Name}\". Your settings are unchanged.", isError: false);
            return;
        }

        ApplyProfile(preset);
        _selectedPreset = preset;
        RefreshAppliedPreset();

        ShowPresetHint($"Applied \"{preset.Name}\". Click it again to unselect.", isError: false);
    }

    private void ApplyProfile(ClickPreset profile)
    {
        CpsSlider.Value = Math.Clamp(profile.Cps, CpsSlider.Minimum, CpsSlider.Maximum);
        CdcSlider.Value = Math.Clamp(profile.Cdc, CdcSlider.Minimum, CdcSlider.Maximum);

        _shakeLeft = Math.Clamp(profile.ShakeLeft, 0, MaxShakePixels);
        _shakeRight = Math.Clamp(profile.ShakeRight, 0, MaxShakePixels);
        _shakeUp = Math.Clamp(profile.ShakeUp, 0, MaxShakePixels);
        _shakeDown = Math.Clamp(profile.ShakeDown, 0, MaxShakePixels);
        WriteShakeBoxes();

        ShakyTracking.IsChecked = profile.Shaky;
        UltraAccuracy.IsChecked = profile.UltraAccuracy;
        SetClickMode(profile.HoldMode);

        UpdateShakeLabel();
        UpdateEngineSettings();
        RefreshAppliedPreset();
    }

    /// <summary>
    /// Loads a preset for editing: applies it first, then fills the form with
    /// its name and rates.
    /// </summary>
    /// <remarks>
    /// Applying first matters. Saving captures the app's whole current state,
    /// so editing a preset without loading it would quietly overwrite its mode,
    /// accuracy and shake with whatever happened to be set at the time.
    /// </remarks>
    private const string CustomPresetHintText = "Name it, set CPS and CDC, and it is saved between sessions";

    /// <summary>The preset loaded into the form, so the pencil can toggle.</summary>
    private ClickPreset? _editingPreset;

    private void EditPreset_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ClickPreset preset) return;

        // Second click on the same pencil backs out of editing.
        if (ReferenceEquals(preset, _editingPreset))
        {
            ClearPresetForm();
            ShowPresetHint(CustomPresetHintText, isError: false);
            return;
        }

        // Applied directly rather than through the card handler, which would
        // treat an already-selected preset as a request to unselect it.
        ApplyProfile(preset);
        _selectedPreset = preset;
        RefreshAppliedPreset();

        _editingPreset = preset;

        NewPresetName.Text = preset.Name;
        NewPresetCps.Text = preset.CpsText;
        NewPresetCdc.Text = preset.CdcText;

        NewPresetCps.Focus();
        NewPresetCps.SelectAll();

        ShowPresetHint($"Editing \"{preset.Name}\" — change the values, then Save to replace it.", isError: false);
    }

    private void ClearPresetForm()
    {
        _editingPreset = null;

        NewPresetName.Clear();
        NewPresetCps.Clear();
        NewPresetCdc.Clear();
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ClickPreset preset) return;

        // Leaving a deleted preset loaded in the form would let Save resurrect it.
        if (ReferenceEquals(preset, _editingPreset)) ClearPresetForm();

        _clickPresets.Remove(preset);
        PresetStore.Save(_clickPresets);
        RefreshAppliedPreset();
        ShowPresetHint($"Removed \"{preset.Name}\"", isError: false);
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        // Adds back only what is missing, so custom presets and edited defaults
        // are left alone.
        int added = 0;

        foreach (ClickPreset preset in PresetStore.Defaults())
        {
            if (_clickPresets.Any(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase)))
                continue;

            _clickPresets.Add(preset);
            added++;
        }

        if (added > 0) PresetStore.Save(_clickPresets);

        RefreshAppliedPreset();
        ShowPresetHint(added == 0 ? "All default presets are already here" : $"Restored {added} default preset(s)",
            isError: false);
    }

    private void NumericBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox box) return;
        e.Handled = !IsNumericCandidate(box, e.Text);
    }

    private void NumericBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox box)
        {
            e.CancelCommand();
            return;
        }

        string? pasted = e.DataObject.GetDataPresent(typeof(string))
            ? e.DataObject.GetData(typeof(string)) as string
            : null;

        if (pasted == null || !IsNumericCandidate(box, pasted)) e.CancelCommand();
    }

    /// <summary>
    /// Would the box still hold a number if this text were inserted? Checked
    /// against the resulting string rather than the keystroke, so a second
    /// decimal point is rejected while the first is allowed.
    /// </summary>
    private static bool IsNumericCandidate(TextBox box, string insert)
    {
        string candidate = box.Text
            .Remove(box.SelectionStart, box.SelectionLength)
            .Insert(box.SelectionStart, insert);

        if (candidate.Length == 0) return true;

        string separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;

        // Partial entries that are not yet parseable but are on their way.
        if (candidate == "-" || candidate == separator || candidate == "-" + separator) return true;

        // Deliberately not NumberStyles.Float: that also admits exponents and
        // whitespace, so "1e5" and " 5" would slip through.
        return double.TryParse(
            candidate,
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.CurrentCulture,
            out _);
    }

    private void NewPreset_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        SavePreset_Click(sender, e);
        e.Handled = true;
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        string name = NewPresetName.Text.Trim();

        if (name.Length == 0)
        {
            ShowPresetHint("Give the preset a name first", isError: true);
            NewPresetName.Focus();
            return;
        }

        if (!TryReadPresetField(NewPresetCps, CpsSlider, out double cps))
        {
            ShowPresetHint($"CPS must be a number between {CpsSlider.Minimum:0} and {CpsSlider.Maximum:0}", isError: true);
            NewPresetCps.Focus();
            return;
        }

        if (!TryReadPresetField(NewPresetCdc, CdcSlider, out double cdc))
        {
            ShowPresetHint($"CDC must be a number between {CdcSlider.Minimum:0} and {CdcSlider.Maximum:0}", isError: true);
            NewPresetCdc.Focus();
            return;
        }

        // Replacing rather than duplicating: two cards with the same name and
        // different numbers would be impossible to tell apart.
        ClickPreset? existing = _clickPresets
            .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (existing != null) _clickPresets.Remove(existing);

        // Captures the whole current setup, not just the two typed numbers.
        _clickPresets.Add(new ClickPreset(
            name, cps, cdc,
            _holdMode,
            UltraAccuracy.IsChecked == true,
            ShakyTracking.IsChecked == true,
            _shakeLeft, _shakeRight, _shakeUp, _shakeDown));

        PresetStore.Save(_clickPresets);

        ClearPresetForm();

        ShowPresetHint(existing != null ? $"Replaced \"{name}\"" : $"Saved \"{name}\"", isError: false);
        RefreshAppliedPreset();
    }

    /// <summary>
    /// Blank means "use whatever the sliders are set to now", which is the common
    /// case — dial it in on the Clicker page, then come here and name it.
    /// </summary>
    private static bool TryReadPresetField(TextBox box, Slider slider, out double value)
    {
        if (box.Text.Trim().Length == 0)
        {
            value = slider.Value;
            return true;
        }

        if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            return false;

        if (value < slider.Minimum || value > slider.Maximum)
            return false;

        return true;
    }

    private void ShowPresetHint(string message, bool isError)
    {
        CustomPresetHint.Text = message;
        CustomPresetHint.Foreground = (System.Windows.Media.Brush)FindResource(isError ? "Accent" : "TextMuted");
    }

    /// <summary>
    /// Marks whichever preset the sliders currently match. Driven from the slider
    /// values rather than from the last card clicked, so editing CPS by hand
    /// clears the highlight instead of leaving it stale.
    /// </summary>
    private void RefreshAppliedPreset()
    {
        if (PresetAppliedText == null) return;

        // The highlight is an explicit selection, but it still drops when the
        // settings move away from it — otherwise a card would claim to be
        // applied while a slider says otherwise.
        if (_selectedPreset != null
            && (!_clickPresets.Contains(_selectedPreset) || !MatchesCurrent(_selectedPreset)))
        {
            _selectedPreset = null;
        }

        foreach (ClickPreset p in _clickPresets) p.IsApplied = ReferenceEquals(p, _selectedPreset);

        PresetAppliedText.Text = _selectedPreset == null ? "No preset applied" : $"Applied — {_selectedPreset.Name}";
        PresetAppliedText.Foreground =
            (System.Windows.Media.Brush)FindResource(_selectedPreset == null ? "TextMuted" : "Accent");

        PresetEmptyText.Visibility = _clickPresets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool MatchesCurrent(ClickPreset p) =>
        Math.Abs(p.Cps - CpsSlider.Value) < 0.05
        && Math.Abs(p.Cdc - CdcSlider.Value) < 0.05
        && p.HoldMode == _holdMode
        && p.UltraAccuracy == (UltraAccuracy?.IsChecked == true)
        && p.Shaky == (ShakyTracking?.IsChecked == true)
        && (!p.Shaky || (Math.Abs(p.ShakeLeft - _shakeLeft) < 0.5
                         && Math.Abs(p.ShakeRight - _shakeRight) < 0.5
                         && Math.Abs(p.ShakeUp - _shakeUp) < 0.5
                         && Math.Abs(p.ShakeDown - _shakeDown) < 0.5));

    private void NavClicker_Click(object sender, RoutedEventArgs e) =>
        ShowPage(NavClicker, PageClicker, "Clicker", "Configure your own click engine");

    private void NavPresets_Click(object sender, RoutedEventArgs e) =>
        ShowPage(NavPresets, PagePresets, "Presets", "Saved click configurations");

    private void NavTweaks_Click(object sender, RoutedEventArgs e)
    {
        // Re-read on entry: a tweak may have been changed outside this app since
        // the page was last shown.
        RefreshTweaks();
        ShowPage(NavTweaks, PageTweaks, "Tweaks", "Windows performance tweaks");
    }

    private void NavOptimizations_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(NavOptimizations, PageOptimizations, "Optimizations", "Disk, input and memory");
        RefreshTweaks();
        RefreshMemoryLine();
        _ = ScanTempAsync();
    }

    // ---- temp cleaner ----

    private TempScan _lastTempScan;
    private bool _tempBusy;

    /// <summary>
    /// Scanning walks six figures of files on a normal machine, so it runs off
    /// the UI thread.
    /// </summary>
    private async Task ScanTempAsync()
    {
        if (_tempBusy) return;

        _tempBusy = true;
        CleanTempButton.IsEnabled = false;
        ReclaimableHint.Text = "Scanning…";

        try
        {
            DateTime cutoff = DateTime.UtcNow - TempCleaner.MinimumAge;
            _lastTempScan = await Task.Run(() => TempCleaner.Scan(cutoff));

            ReclaimableText.Text = FormatSize(_lastTempScan.Bytes);
            ReclaimableHint.Text = _lastTempScan.Files == 0
                ? "Nothing older than a day to clear"
                : $"{_lastTempScan.Files:N0} files older than {TempCleaner.MinimumAge.TotalHours:0} hours";
        }
        catch (Exception ex)
        {
            ReclaimableText.Text = "—";
            ReclaimableHint.Text = $"Could not scan: {ex.Message}";
        }
        finally
        {
            _tempBusy = false;
            CleanTempButton.IsEnabled = _lastTempScan.Files > 0;
        }
    }

    private void RescanTemp_Click(object sender, RoutedEventArgs e) => _ = ScanTempAsync();

    private async void CleanTemp_Click(object sender, RoutedEventArgs e)
    {
        if (_tempBusy || _lastTempScan.Files == 0) return;

        // Deleting files is not undoable, so this one keeps a confirmation.
        bool confirmed = await ConfirmAsync(
            "Delete temp files?",
            $"{_lastTempScan.Files:N0} files totalling {FormatSize(_lastTempScan.Bytes)} will be deleted.\n\n" +
            "Only files untouched for over a day are removed, and only from your temp folders.",
            "Delete");

        if (!confirmed) return;

        _tempBusy = true;
        CleanTempButton.IsEnabled = false;
        ShowCleanupStatus("Cleaning…", isError: false);

        try
        {
            DateTime cutoff = DateTime.UtcNow - TempCleaner.MinimumAge;
            CleanResult result = await Task.Run(() => TempCleaner.Clean(cutoff));

            ShowCleanupStatus(
                $"Deleted {result.Deleted:N0} files, freeing {FormatSize(result.Bytes)}. " +
                $"{result.Skipped:N0} were skipped as in use or too recent.",
                isError: false);
        }
        catch (Exception ex)
        {
            ShowCleanupStatus($"Clean-up failed: {ex.Message}", isError: true);
        }
        finally
        {
            _tempBusy = false;
        }

        await ScanTempAsync();
    }

    // ---- memory ----

    private void RefreshMemoryLine()
    {
        MemoryStatus memory = MemoryTools.Read();

        MemoryLine.Text = memory.TotalBytes == 0
            ? "Could not read memory status"
            : $"{memory.UsedGb:0.00} GB used of {memory.TotalGb:0.00} GB ({memory.LoadPercent}%)";
    }

    private void FreeRam_Click(object sender, RoutedEventArgs e)
    {
        MemoryStatus before = MemoryTools.Read();
        (int trimmed, int skipped) = MemoryTools.TrimWorkingSets();
        MemoryStatus after = MemoryTools.Read();

        RefreshMemoryLine();

        double delta = after.AvailableGb - before.AvailableGb;

        // Reported as a change in the number rather than as a benefit, because
        // that is all it is.
        ShowCleanupStatus(
            $"Trimmed {trimmed} processes ({skipped} were not ours to touch). " +
            $"Available went from {before.AvailableGb:0.00} GB to {after.AvailableGb:0.00} GB " +
            $"({delta:+0.00;-0.00;0.00} GB). Expect it to drift back as those apps carry on.",
            isError: false);
    }

    private void ShowCleanupStatus(string message, bool isError)
    {
        CleanupStatusText.Text = message;
        CleanupStatusText.Foreground =
            (System.Windows.Media.Brush)FindResource(isError ? "Accent" : "TextMuted");
    }

    private static string FormatSize(long bytes)
    {
        double mb = bytes / 1024.0 / 1024.0;
        return mb >= 1024 ? $"{mb / 1024.0:0.00} GB" : $"{mb:0} MB";
    }

    private void NavMod_Click(object sender, RoutedEventArgs e)
    {
        RefreshFlags();
        ShowPage(NavMod, PageMod, "Mod", "Roblox client settings");
    }

    // ---- FastFlags ----

    private void RefreshFlags()
    {
        FlagList.ItemsSource = FastFlagStore.FpsBoost;

        string? path = FastFlagStore.SettingsPath();
        bool installed = path != null;

        ApplyFlagsButton.IsEnabled = installed;
        ResetFlagsButton.IsEnabled = installed;

        if (!installed)
        {
            FlagPathText.Text = "";
            FlagStateText.Text = "Roblox not found";
            FlagStatusText.Text = "No Roblox client is installed on this machine, so there is nothing to write to.";
            return;
        }

        FlagPathText.Text = path;

        bool applied = FastFlagStore.IsApplied(FastFlagStore.FpsBoost);
        FlagStateText.Text = applied ? "Applied" : "Not applied";
        FlagStateText.Foreground =
            (System.Windows.Media.Brush)FindResource(applied ? "Accent" : "TextMuted");
    }

    private void ApplyFlags_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            FastFlagStore.Apply(FastFlagStore.FpsBoost);
            RefreshFlags();
            ShowFlagStatus("Applied. Restart Roblox for it to take effect.", isError: false);
        }
        catch (Exception ex)
        {
            ShowFlagStatus(ex.Message, isError: true);
        }
    }

    private void ResetFlags_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Removes only what this app wrote, so hand-added flags survive.
            FastFlagStore.Reset(FastFlagStore.FpsBoost);
            RefreshFlags();
            ShowFlagStatus("Reset. Restart Roblox for it to take effect.", isError: false);
        }
        catch (Exception ex)
        {
            ShowFlagStatus(ex.Message, isError: true);
        }
    }

    private void ShowFlagStatus(string message, bool isError)
    {
        FlagStatusText.Text = message;
        FlagStatusText.Foreground =
            (System.Windows.Media.Brush)FindResource(isError ? "Accent" : "TextMuted");
    }

    private void NavHistory_Click(object sender, RoutedEventArgs e)
    {
        FlushHistory();
        RefreshHistoryUi();
        ShowPage(NavHistory, PageHistory, "History", "Time spent clicking");
    }

    // ---- history ----

    private long _activeTicks;
    private long _bankedTicks;
    private long _bankedClicks;
    private double _sessionSeconds;
    private long _sessionClicks;
    private int _historyTicksSinceSave;

    /// <summary>
    /// Moves whatever the engine has accumulated since the last call into the
    /// history totals. Deltas are taken against a running baseline, so a flush
    /// mid-session cannot double-count.
    /// </summary>
    private void FlushHistory()
    {
        long ticks = Interlocked.Read(ref _activeTicks);
        long clicks = Interlocked.Read(ref _clickCount);

        double seconds = (ticks - _bankedTicks) / (double)Stopwatch.Frequency;
        long newClicks = clicks - _bankedClicks;

        _bankedTicks = ticks;
        _bankedClicks = clicks;

        if (seconds <= 0 && newClicks <= 0) return;

        _sessionSeconds += seconds;
        _sessionClicks += newClicks;

        _history.Add(DateTime.Now, seconds, newClicks);
        _historyDirty = true;
    }

    private void RefreshHistoryUi()
    {
        if (HistoryTotalText == null) return;

        HistoryTotalText.Text = ClickHistory.FormatDuration(TimeSpan.FromSeconds(_history.TotalSeconds));
        HistoryClicksText.Text = _history.TotalClicks.ToString("N0", CultureInfo.CurrentCulture);
        HistoryAverageText.Text = _history.AverageRateText;

        HistoryDay? busiest = _history.BusiestDay;
        HistoryBusiestText.Text = busiest?.DurationText ?? "—";
        HistoryBusiestDateText.Text = busiest?.DateText ?? "";

        HistoryDaysText.Text = _history.DaysRecorded.ToString(CultureInfo.CurrentCulture);
        HistorySinceText.Text = _history.DaysRecorded == 0 ? "" : $"since {_history.EarliestDayText}";

        HistorySessionTimeText.Text = ClickHistory.FormatDuration(TimeSpan.FromSeconds(_sessionSeconds));
        HistorySessionClicksText.Text = _sessionClicks.ToString("N0", CultureInfo.CurrentCulture);
        HistorySessionRateText.Text = _sessionSeconds > 0.5
            ? $"{_sessionClicks / _sessionSeconds:0.0} /s"
            : "—";

        List<HistoryDay> days = _history.RecentDays();
        HistoryDayList.ItemsSource = days;
        HistoryEmptyText.Visibility = days.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ResetHistory_Click(object sender, RoutedEventArgs e)
    {
        bool confirmed = await ConfirmAsync(
            "Reset history?",
            "Every recorded total and the whole day-by-day breakdown will be cleared. This cannot be undone.",
            "Reset");

        if (!confirmed) return;

        // Re-baseline instead of zeroing the counters, so activity already in
        // flight on the engine thread is discarded rather than counted later.
        _bankedTicks = Interlocked.Read(ref _activeTicks);
        _bankedClicks = Interlocked.Read(ref _clickCount);
        _sessionSeconds = 0;
        _sessionClicks = 0;

        _history.Reset();
        _history.Save();
        _historyDirty = false;

        RefreshHistoryUi();
    }

    private void NavRecorder_Click(object sender, RoutedEventArgs e)
    {
        RefreshRecorderEngineText();
        ShowPage(NavRecorder, PageRecorder, "Recorder", "Record a clip and share it as a link");
    }

    // ---- recording ----

    private readonly ScreenRecorder _recorder = new();
    private readonly ReplayBuffer _replay = new();

    /// <summary>Always buffers the longest offered length, so switching is instant.</summary>
    private const int ReplayCapacitySeconds = 60;

    // ---- capture target ----

    private readonly List<DisplayInfo> _displays = Displays.All();

    /// <summary>The monitor being captured, or null for the whole desktop.</summary>
    private DisplayInfo? _captureDisplay;

    /// <summary>
    /// One button per monitor, plus "All displays".
    /// </summary>
    /// <remarks>
    /// Built in code rather than declared: the number of monitors is not known
    /// until the app runs. Reuses the segment style the replay length buttons
    /// already use, so it needs no template of its own.
    /// </remarks>
    private void BuildDisplayButtons()
    {
        DisplayPanel.Children.Clear();

        var options = new List<(string Label, string? Device)> { ("All displays", null) };
        foreach (DisplayInfo display in _displays) options.Add((display.Label, display.DeviceName));

        foreach ((string label, string? device) in options)
        {
            var button = new RadioButton
            {
                Content = label,
                GroupName = "CaptureDisplay",
                Style = (Style)FindResource("SegmentButton"),
                Tag = device,
                Margin = new Thickness(0, 0, 6, 6)
            };

            button.Checked += CaptureDisplay_Checked;
            DisplayPanel.Children.Add(button);
        }
    }

    private void CaptureDisplay_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton button) return;

        string? device = button.Tag as string;
        _captureDisplay = device == null ? null : Displays.Match(_displays, device);
        _settingsDirty = true;
        RestartReplayIfRunning();
    }

    /// <summary>Checks the button for this monitor. False when none matches.</summary>
    /// <remarks>
    /// A stored monitor that has since been unplugged matches nothing, which the
    /// caller turns into "all displays" — better than recording a black crop of
    /// coordinates that no longer exist.
    /// </remarks>
    private bool SelectDisplay(string? deviceName)
    {
        foreach (object child in DisplayPanel.Children)
        {
            if (child is RadioButton button
                && string.Equals(button.Tag as string, deviceName, StringComparison.OrdinalIgnoreCase))
            {
                button.IsChecked = true;
                return true;
            }
        }

        return false;
    }

    /// <summary>Starts or stops recording, from the bound hotkey.</summary>
    /// <remarks>
    /// Guarded on IsEnabled because the button is disabled while ffmpeg
    /// finalises the file, and a second press during that window would start a
    /// new recording on top of the one still closing.
    /// </remarks>
    private void OnRecordHotkey()
    {
        if (RecordButton.IsEnabled) Record_Click(RecordButton, new RoutedEventArgs());
    }

    private int ReplaySeconds { get; set; } = 30;

    private void ReplayLength_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } || !int.TryParse(tag, out int seconds)) return;

        ReplaySeconds = seconds;
        _settingsDirty = true;
    }

    private void ReplayEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (ReplayEnabled.IsChecked == true)
        {
            try
            {
                _replay.Start(ReplayCapacitySeconds, RecordFps, _captureDisplay);
                ShowReplayStatus($"Rolling. The last {ReplayCapacitySeconds}s is always available.", isError: false);
            }
            catch (Exception ex)
            {
                ReplayEnabled.IsChecked = false;
                ShowReplayStatus(ex.Message, isError: true);
            }

            _settingsDirty = true;
            return;
        }

        _replay.Stop();
        ShowReplayStatus("Instant replay is off. Nothing is being buffered.", isError: false);
        _settingsDirty = true;
    }

    private void SaveReplay_Click(object sender, RoutedEventArgs e) => OnReplayHotkey();

    private void ShowReplayStatus(string message, bool isError)
    {
        ReplayStatusText.Text = message;
        ReplayStatusText.Foreground =
            (System.Windows.Media.Brush)FindResource(isError ? "Accent" : "TextMuted");
    }

    private static string DefaultClipFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "JinxyClicker");

    /// <summary>
    /// Where clips and saved replays are written. An empty box means the
    /// default rather than nothing, so clearing the field cannot leave the
    /// recorder with nowhere to put a file.
    /// </summary>
    private string ClipFolder
    {
        get
        {
            string typed = ClipFolderBox?.Text.Trim() ?? string.Empty;
            return typed.Length == 0 ? DefaultClipFolder : typed;
        }
    }

    private void ClipFolder_TextChanged(object sender, TextChangedEventArgs e) => _settingsDirty = true;

    private void ResetClipFolder_Click(object sender, RoutedEventArgs e) =>
        ClipFolderBox.Text = DefaultClipFolder;

    /// <summary>Frames per second for both the recorder and the replay buffer.</summary>
    private int RecordFps { get; set; } = 30;

    private void RecordFps_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } || !int.TryParse(tag, out int fps)) return;

        RecordFps = fps;
        _settingsDirty = true;
        RestartReplayIfRunning();
    }

    /// <summary>Checks the matching framerate button. False when none matches.</summary>
    private bool SelectRecordFps(int fps)
    {
        foreach (object child in RecordFpsPanel.Children)
        {
            if (child is RadioButton { Tag: string tag } button
                && int.TryParse(tag, out int value) && value == fps)
            {
                button.IsChecked = true;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Rebuilds the rolling buffer so a changed framerate or monitor takes
    /// effect now.
    /// </summary>
    /// <remarks>
    /// ffmpeg fixes both at launch, so without this the buffer would quietly
    /// keep capturing the old screen at the old rate while the page claimed
    /// otherwise — and the mismatch would only surface in a saved clip.
    /// </remarks>
    private void RestartReplayIfRunning()
    {
        if (!_replay.IsRunning) return;

        try
        {
            _replay.Stop();
            _replay.Start(ReplayCapacitySeconds, RecordFps, _captureDisplay);
        }
        catch (Exception ex)
        {
            ReplayEnabled.IsChecked = false;
            ShowReplayStatus(ex.Message, isError: true);
        }
    }

    private void RefreshRecorderEngineText()
    {
        string? ffmpeg = ScreenRecorder.FindFfmpeg();

        RecorderEngineText.Text = ffmpeg == null
            ? "No recorder found. ffmpeg.exe needs to sit in an 'ffmpeg' folder next to the app."
            : "Records the whole screen to your Videos folder, then uploads on request.";

        RecordButton.IsEnabled = ffmpeg != null;
    }

    private async void Record_Click(object sender, RoutedEventArgs e)
    {
        if (_recorder.IsRecording)
        {
            RecordButton.IsEnabled = false;
            ShowRecordStatus("Finishing the file…", isError: false);

            // Never a kill: ffmpeg writes the MP4 index on exit, and a killed
            // process leaves a file that no player will open.
            string? path = await _recorder.StopAsync();

            RecordButton.IsEnabled = true;
            RecordButton.Content = "Start recording";
            RecordElapsedText.Text = "00:00";

            if (path == null)
            {
                ShowRecordStatus("Recording stopped, but no usable file was produced.", isError: true);
                return;
            }

            var file = new FileInfo(path);
            _chosenClipPath = path;

            ChosenClipText.Text = $"{file.Name} — {file.Length / 1024.0 / 1024.0:0.0} MB";
            UploadClipButton.IsEnabled = file.Length > 0 && file.Length <= ClipUploader.MaxBytes;

            ShowRecordStatus($"Saved to {path}", isError: false);
            return;
        }

        try
        {
            _recorder.Start(ClipFolder, RecordFps, _captureDisplay);

            RecordButton.Content = "Stop recording";
            ShowRecordStatus("Recording the whole screen — everything visible is captured.", isError: false);
        }
        catch (Exception ex)
        {
            ShowRecordStatus(ex.Message, isError: true);
        }
    }

    private void OpenClipFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(ClipFolder);
            Process.Start(new ProcessStartInfo(ClipFolder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowRecordStatus($"Could not open the folder: {ex.Message}", isError: true);
        }
    }

    private void RefreshRecordElapsed()
    {
        if (!_recorder.IsRecording) return;

        TimeSpan elapsed = DateTime.UtcNow - _recorder.StartedUtc;
        RecordElapsedText.Text = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
    }

    private void ShowRecordStatus(string message, bool isError)
    {
        RecordStatusText.Text = message;
        RecordStatusText.Foreground =
            (System.Windows.Media.Brush)FindResource(isError ? "Accent" : "TextMuted");
    }

    private void NavTheme_Click(object sender, RoutedEventArgs e) =>
        ShowPage(NavTheme, PageTheme, "Theme", "Appearance");

    // ---- theme ----

    private const string DefaultAccent = "#FF4B52";

    /// <summary>Both palettes, one row per themed colour.</summary>
    /// <remarks>
    /// A table rather than two blocks of assignments, so the modes cannot drift
    /// apart: every colour is defined for both by construction, and adding one
    /// means adding a row rather than remembering a second place to edit.
    ///
    /// The keys are the Color resources, not the brushes. The brushes take their
    /// Color from these by DynamicResource, which is what lets one assignment
    /// repaint every StaticResource consumer of the brush.
    /// </remarks>
    private static readonly (string Key, string Dark, string Light)[] Palette =
    {
        ("BgColor",           "#0F141D", "#EEF1F6"),
        ("PanelColor",        "#1A2230", "#FFFFFF"),
        ("Panel2Color",       "#151C27", "#E7ECF3"),
        ("SidebarColor",      "#151B25", "#E3E9F1"),
        ("SunkenColor",       "#101620", "#E7ECF3"),
        ("ControlColor",      "#202A39", "#E4E9F1"),
        ("ControlHoverColor", "#26313F", "#D5DDE9"),
        ("ControlAltColor",   "#1E2634", "#DCE3EC"),
        ("TrackColor",        "#2A3445", "#C6D0DE"),
        ("DividerColor",      "#232E3E", "#DCE2EA"),
        ("OutlineColor",      "#3A465A", "#B0BCCC"),
        // Alpha overlays, so they invert rather than lighten in both modes.
        ("HairlineColor",     "#1AFFFFFF", "#14000000"),
        ("TextColor",         "#E9EDF4", "#16202E"),
        ("TextBrightColor",   "#F5F7FA", "#0A1119"),
        ("TextSoftColor",     "#AAB4C5", "#4E5B6C"),
        ("TextDimColor",      "#C3CDDD", "#333E4C"),
        ("TextMutedColor",    "#8D98AA", "#63707F"),
        ("SheenColor",        "#FFFFFF", "#000000")
    };

    /// <summary>
    /// Repaints the window's neutrals. The accent is untouched, so a colour
    /// chosen on one mode survives switching to the other.
    /// </summary>
    private void ApplyTheme(bool light)
    {
        foreach ((string key, string dark, string bright) in Palette)
        {
            if (TryParseColor(light ? bright : dark, out Color color)) Resources[key] = color;
        }
    }

    private void ThemeMode_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;

        ApplyTheme(tag == "Light");
        _settingsDirty = true;
    }

    /// <summary>The chosen accent, in the hex form the settings file stores.</summary>
    private string _accentHex = DefaultAccent;

    private void AccentSwatch_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string hex } || !TryParseColor(hex, out Color accent))
            return;

        ApplyAccent(accent);
        _accentHex = hex;
        _settingsDirty = true;
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Fires while the XAML is still being parsed, before the label exists.
        if (OpacityValueText == null) return;

        Opacity = e.NewValue;
        OpacityValueText.Text = $"{e.NewValue * 100:0}%";
        _settingsDirty = true;
    }

    /// <summary>Checks the swatch holding this colour. False when none matches.</summary>
    /// <remarks>
    /// Checking it raises Checked, which both applies the colour and records it,
    /// so the ringed chip and the stored value cannot drift apart.
    /// </remarks>
    private bool SelectAccent(string? hex)
    {
        foreach (object child in AccentSwatchPanel.Children)
        {
            if (child is RadioButton { Tag: string tag } button
                && string.Equals(tag, hex, StringComparison.OrdinalIgnoreCase))
            {
                button.IsChecked = true;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Repaints every accent-coloured element in the window, without a restart.
    /// </summary>
    /// <remarks>
    /// Moves the Colors the accent brushes point at, not the brushes themselves.
    /// The brushes cannot be touched: WPF freezes resource Freezables wherever it
    /// can, and assigning to a frozen brush's Color throws. Shifting the Color
    /// underneath them avoids that, and still reaches every consumer at once —
    /// they all hold the same brush, so none of the StaticResource references
    /// need to become dynamic.
    /// </remarks>
    private void ApplyAccent(Color accent)
    {
        Resources["AccentColor"] = accent;

        // The tints are the accent at reduced alpha, so they are wrong the moment
        // it moves. Recomputed here rather than stored, so there is one source.
        Resources["AccentStrongColor"] = Color.FromArgb(0x66, accent.R, accent.G, accent.B);
        Resources["AccentSoftColor"] = Color.FromArgb(0x33, accent.R, accent.G, accent.B);
        Resources["AccentWashColor"] = Color.FromArgb(0x22, accent.R, accent.G, accent.B);
    }

    private static bool TryParseColor(string? hex, out Color color)
    {
        color = default;

        try
        {
            if (ColorConverter.ConvertFromString(hex) is not Color parsed) return false;

            color = parsed;
            return true;
        }
        catch
        {
            // A hand-edited settings file. The caller falls back to the default.
            return false;
        }
    }

    // ---- clip sharing ----

    private string? _chosenClipPath;

    private void ChooseClip_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a clip",
            Filter = "Video files|*.mp4;*.mkv;*.mov;*.webm;*.avi|All files|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true) return;

        var file = new FileInfo(dialog.FileName);
        _chosenClipPath = file.FullName;

        ChosenClipText.Text = $"{file.Name} — {file.Length / 1024.0 / 1024.0:0.0} MB";
        UploadClipButton.IsEnabled = file.Length > 0 && file.Length <= ClipUploader.MaxBytes;

        if (file.Length > ClipUploader.MaxBytes)
        {
            ShowUploadStatus(
                $"Too large to upload — the limit is {ClipUploader.MaxBytes / 1024 / 1024} MB.", isError: true);
        }
        else
        {
            ShowUploadStatus("", isError: false);
        }
    }

    private async void UploadClip_Click(object sender, RoutedEventArgs e)
    {
        if (_chosenClipPath == null) return;

        // Publishing is never implicit: this is the confirmation, and it names
        // what leaves the machine.
        bool confirmed = await ConfirmAsync(
            "Upload this clip?",
            $"{Path.GetFileName(_chosenClipPath)} will be uploaded to catbox.moe and given a public link.\n\n" +
            "Anyone with the link can watch it, and an anonymous upload cannot reliably be deleted afterwards.",
            "Upload");

        if (!confirmed) return;

        UploadClipButton.IsEnabled = false;
        ChooseClipButton.IsEnabled = false;
        ShowUploadStatus("Uploading…", isError: false);

        UploadResult result = await ClipUploader.UploadAsync(_chosenClipPath, CancellationToken.None);

        ChooseClipButton.IsEnabled = true;
        UploadClipButton.IsEnabled = true;

        if (!result.Success || result.Url == null)
        {
            ShowUploadStatus(result.Message, isError: true);
            return;
        }

        ClipUrlBox.Text = result.Url;
        CopyLinkButton.IsEnabled = true;
        ShowUploadStatus("Uploaded. The link is ready to share.", isError: false);
    }

    private void CopyLink_Click(object sender, RoutedEventArgs e)
    {
        if (ClipUrlBox.Text.Length == 0) return;

        try
        {
            Clipboard.SetText(ClipUrlBox.Text);
            ShowUploadStatus("Link copied.", isError: false);
        }
        catch (Exception ex)
        {
            // The clipboard can be locked by another process.
            ShowUploadStatus($"Could not copy: {ex.Message}", isError: true);
        }
    }

    // ---- in-app confirmation ----

    private TaskCompletionSource<bool>? _confirmResult;

    /// <summary>
    /// Replaces MessageBox for confirmations, so a deliberate choice does not
    /// arrive as a stock Win32 dialog in the middle of a dark themed app.
    /// </summary>
    private Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        // A second prompt while one is open would orphan the first task.
        _confirmResult?.TrySetResult(false);

        ConfirmTitleText.Text = title;
        ConfirmMessageText.Text = message;
        ConfirmYesButton.Content = confirmText;
        ConfirmOverlay.Visibility = Visibility.Visible;
        ConfirmNoButton.Focus();

        _confirmResult = new TaskCompletionSource<bool>();
        return _confirmResult.Task;
    }

    private bool IsConfirming => ConfirmOverlay.Visibility == Visibility.Visible;

    private void ConfirmYes_Click(object sender, RoutedEventArgs e) => CloseConfirm(true);

    private void ConfirmNo_Click(object sender, RoutedEventArgs e) => CloseConfirm(false);

    private void CloseConfirm(bool result)
    {
        ConfirmOverlay.Visibility = Visibility.Collapsed;

        TaskCompletionSource<bool>? pending = _confirmResult;
        _confirmResult = null;
        pending?.TrySetResult(result);
    }

    private void ShowUploadStatus(string message, bool isError)
    {
        UploadStatusText.Text = message;
        UploadStatusText.Foreground =
            (System.Windows.Media.Brush)FindResource(isError ? "Accent" : "TextMuted");
    }

    private void NavSettings_Click(object sender, RoutedEventArgs e) =>
        ShowPage(NavSettings, PageSettings, "Settings", "Application preferences");

    /// <summary>
    /// Swaps the visible page and moves the sidebar highlight. The highlight is
    /// driven by Tag rather than by which button was clicked last, so it can
    /// never point at a page that failed to open.
    /// </summary>
    private void ShowPage(Button navButton, UIElement page, string title, string subtitle)
    {
        foreach (Button b in NavButtons) b.ClearValue(TagProperty);
        navButton.Tag = "Selected";

        foreach (UIElement p in Pages) p.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;

        // The status pill and start button belong to the clicker, not the shell.
        StatusBlock.Visibility = ReferenceEquals(page, PageClicker)
            ? Visibility.Visible
            : Visibility.Collapsed;

        PageTitleText.Text = title;
        PageSubtitleText.Text = subtitle;
    }

    private Button[] NavButtons => new[]
    {
        NavClicker, NavPresets, NavTweaks, NavOptimizations,
        NavMod, NavRecorder, NavHistory, NavTheme, NavSettings
    };

    private UIElement[] Pages => new UIElement[]
    {
        PageClicker, PagePresets, PageTweaks, PageOptimizations,
        PageMod, PageRecorder, PageHistory, PageTheme, PageSettings
    };

    // ---- Windows tweaks ----

    private void RefreshTweaks()
    {
        foreach (PcTweak tweak in _tweaks) tweak.Refresh();
        foreach (PcTweak tweak in _inputTweaks) tweak.Refresh();
        foreach (PcTweak tweak in _networkTweaks) tweak.Refresh();

        RestartAdminButton.Visibility = TweakEnvironment.IsElevated
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (TweakStatusText.Text.Length == 0)
        {
            // Counted rather than written out, so it cannot drift when a tweak's
            // requirement changes.
            int needAdmin = _tweaks.Count(t => t.RequiresAdmin);

            ShowTweakStatus(TweakEnvironment.IsElevated
                ? "Running as administrator — every tweak is available."
                : $"Running as a normal user. {needAdmin} of these need you to restart as admin first.",
                isError: false);
        }
    }

    private void Tweak_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PcTweak tweak) return;

        bool wasApplied = tweak.IsApplied == true;
        string? error = tweak.Toggle(_tweakState);

        if (error != null)
        {
            ShowTweakStatus(error, isError: true);
            return;
        }

        ShowTweakStatus($"{tweak.Name} — {(wasApplied ? "reverted" : "applied")}.", isError: false);
    }

    /// <summary>
    /// The switch has already flipped visually by the time this runs. Toggle()
    /// ends in Refresh(), which raises PropertyChanged and pulls the one-way
    /// binding back to whatever the system actually reports — so a refused
    /// change snaps the switch back rather than leaving it wrong.
    /// </summary>
    private void TweakToggle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PcTweak tweak) return;

        bool wasApplied = tweak.IsApplied == true;
        string? error = tweak.Toggle(_tweakState);

        ShowCleanupStatus(
            error ?? $"{tweak.Name} — {(wasApplied ? "turned off" : "turned on")}.",
            isError: error != null);
    }

    private void RevertAll_Click(object sender, RoutedEventArgs e)
    {
        var errors = new List<string>();
        int reverted = 0;

        foreach (PcTweak tweak in _tweaks)
        {
            if (tweak.IsApplied != true || !tweak.CanAct) continue;

            string? error = tweak.Toggle(_tweakState);
            if (error != null) errors.Add(error);
            else reverted++;
        }

        RefreshTweaks();

        if (errors.Count > 0)
        {
            ShowTweakStatus(string.Join("  ", errors), isError: true);
            return;
        }

        ShowTweakStatus(reverted == 0 ? "Nothing to revert." : $"Reverted {reverted} tweak(s).", isError: false);
    }

    private void RestartAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (!TweakEnvironment.RestartElevated())
        {
            ShowTweakStatus("Could not restart as administrator — the prompt was declined.", isError: true);
            return;
        }

        Close();
    }

    private void ShowTweakStatus(string message, bool isError)
    {
        TweakStatusText.Text = message;
        TweakStatusText.Foreground =
            (System.Windows.Media.Brush)FindResource(isError ? "Accent" : "TextMuted");
    }


    private void StatsTimer_Tick(object? sender, EventArgs e) => UpdateStats();

    private void UpdateStats()
    {
        try
        {
            using Process p = Process.GetCurrentProcess();
            RamText.Text = $"{p.WorkingSet64 / 1024.0 / 1024.0:0} MB";
        }
        catch
        {
            RamText.Text = "-- MB";
        }

        double? cpu = ReadSystemCpuPercent();
        CpuText.Text = cpu.HasValue ? $"{cpu.Value:0}%" : "--%";

        UpdateMeasuredRate();
        RefreshStatus();
        UpdateShakeLabel();
        ProbePingAsync();

        if (_settingsDirty) SaveAppSettings();

        FlushHistory();

        if (PageHistory.Visibility == Visibility.Visible) RefreshHistoryUi();

        RefreshRecordElapsed();

        // Written every 30 s rather than every tick: a crash costs at most half
        // a minute, and the file is not worth 60 writes a minute.
        if (_historyDirty && ++_historyTicksSinceSave >= 30)
        {
            _history.Save();
            _historyDirty = false;
            _historyTicksSinceSave = 0;
        }
    }

    /// <summary>
    /// Clicks actually delivered per second. Timed off the Stopwatch rather than
    /// the timer's nominal 1 s interval, because a DispatcherTimer tick is only
    /// as punctual as the dispatcher — the same reason the measurement is worth
    /// having in the first place.
    /// </summary>
    private void UpdateMeasuredRate()
    {
        long now = Stopwatch.GetTimestamp();
        long clicks = Interlocked.Read(ref _clickCount);

        if (_lastRateTimestamp != 0)
        {
            double seconds = (now - _lastRateTimestamp) / (double)Stopwatch.Frequency;
            if (seconds > 0)
            {
                double rate = (clicks - _lastClickCount) / seconds;
                MeasuredCpsText.Text = _running
                    ? $"Measured {rate:0.0} /s"
                    : "Measured — /s";
            }
        }

        _lastRateTimestamp = now;
        _lastClickCount = clicks;
    }

    /// <summary>
    /// Machine-wide CPU load since the previous call, from the kernel's own
    /// counters. Returns null on the first call, which has no prior sample to
    /// difference against.
    /// </summary>
    private double? ReadSystemCpuPercent()
    {
        if (!GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user))
            return null;

        ulong idleTicks = idle.Ticks;
        ulong kernelTicks = kernel.Ticks;
        ulong userTicks = user.Ticks;

        ulong idleDelta = idleTicks - _prevIdleTicks;
        ulong totalDelta = (kernelTicks - _prevKernelTicks) + (userTicks - _prevUserTicks);

        _prevIdleTicks = idleTicks;
        _prevKernelTicks = kernelTicks;
        _prevUserTicks = userTicks;

        if (!_haveCpuSample)
        {
            _haveCpuSample = true;
            return null;
        }

        if (totalDelta == 0)
            return null;

        // Kernel time already includes idle time, so the busy share is simply
        // whatever of the total was not spent idle.
        double busy = (double)(totalDelta - idleDelta) / totalDelta * 100.0;
        return Math.Clamp(busy, 0.0, 100.0);
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        StopClicking();
        _shakeCts.Cancel();
        _statsTimer.Stop();

        // Everything that is not already written the moment it changes gets
        // written here. Presets, hotkeys and tweak state save on edit, so this
        // covers the app settings and the running history totals.
        SaveAppSettings();

        FlushHistory();
        _history.Save();

        PresetStore.Save(_clickPresets);
        _hotkeySettings.Save();
        _tweakState.Save();

        // A recording still running at this point would leave an unplayable
        // file, so it gets a chance to finalise before the process goes.
        // Blocking variant, not the async one: awaiting on the thread we are
        // blocking is how an app hangs on exit.
        _recorder.StopBlocking();
        _recorder.Dispose();
        _replay.Dispose();
    }

    // Below this the interval exceeds a minute and the app is effectively idle.
    private const double MinimumCps = 0.05;

    private const uint TimerResolutionMs = 1;

    // ICMP round trip toward Roblox's front end. This is not the in-game ping
    // readout — that is measured application-side inside a process we do not
    // touch — but it tracks the same connection getting worse.
    private const string PingHost = "www.roblox.com";
    private const int HighPingMs = 60;

    private const double MinShakeSpeed = 5.0;
    private const double MaxShakeSpeed = 50.0;

    private const double MaxShakePixels = 100.0;
    private const int CoarseSleepSliceMs = 20;

    private const int VK_F6 = 0x75;
    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorInfo(ref CURSORINFO pci);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    // The virtual screen: the bounding box of every monitor together, which is
    // what "a corner of the desktop" has to mean on a multi-monitor setup.
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    private const int IDC_ARROW = 32512;

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private const int CURSOR_SHOWING = 0x0001;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime lpIdleTime, out FileTime lpKernelTime, out FileTime lpUserTime);

    // Per-process since Windows 10 2004, so raising this no longer penalises the
    // whole system. Must be balanced by TimeEndPeriod.
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint uPeriod);

    // Named FileTime rather than FILETIME to keep it distinct from the one in
    // System.Runtime.InteropServices.ComTypes.
    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;

        public readonly ulong Ticks => ((ulong)dwHighDateTime << 32) | dwLowDateTime;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

}
