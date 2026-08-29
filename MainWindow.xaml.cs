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
using System.Windows.Media.Imaging;
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

    private enum RebindTarget { None, Click, Replay, Record, Combo, Build, Switcher, Master, ClickSwitch, Macro }
    private RebindTarget _rebinding = RebindTarget.None;

    /// <summary>
    /// The macro being rebound when <see cref="_rebinding"/> is <see cref="RebindTarget.Macro"/>.
    /// Null means the NEW MACRO form's toggle field, whose captured binding is
    /// held in <see cref="_pendingNewMacroHotkey"/> until Save creates the macro.
    /// </summary>
    /// <remarks>
    /// The fixed hotkeys have one slot each and the enum names them. Macros are
    /// dynamic, so their identity has to be carried alongside — same shape as
    /// the fixed rebind, one extra field to say which macro.
    /// </remarks>
    private KeyMacro? _rebindingMacro;

    /// <summary>The rebind button that shows "Select A Hotkey" while capturing.</summary>
    private Button? _rebindingMacroButton;

    /// <summary>Toggle key picked in the NEW MACRO form, before Save turns it into a macro.</summary>
    private HotkeyBinding _pendingNewMacroHotkey = HotkeyBinding.Unbound;

    /// <summary>
    /// True while the building hotkey is driving the clicker, which substitutes
    /// its own fixed rate for both sliders. Cleared by any stop, so the ordinary
    /// keys never inherit it.
    /// </summary>
    private bool _buildMode;

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
        new TrackingHelperTweak(),
        new FullscreenOptimizationsTweak()
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

            // After the window exists, since the handle does not before that.
            SourceInitialized += (_, _) => ArmMacroSuppression();
            StartMacroTicker();

            // After the window is up, not during construction. A prompt owned by
            // a window that is not shown yet has nothing to sit in front of, and
            // the check is a network call nobody should wait behind to see the
            // app open.
            Loaded += async (_, _) => await UpdateCheck.RunAsync(this);

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

    // Two decimals: at the top of a 0-1000 range one tenth of a click per second
    // is far below what the slider can resolve by dragging, so the typed box is
    // the only way to reach a precise rate and it needs the room to show one.
    private static string FormatValue(double value) =>
        value.ToString("0.00", CultureInfo.CurrentCulture);

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

        // Carries the rate in its text, so it hides with everything else.
        UpdateHitFixClamp();
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
        bool buildWasDown = false;
        bool switcherWasDown = false;
        bool clickSwitchWasDown = false;
        bool masterWasDown = false;
        bool wasInCorner = false;
        bool wasArmed = false;
        bool wasRebindIdle = false;

        // Per-macro edge state, keyed by name because the KeyMacro reference
        // changes whenever a macro is rebound (KeyMacro is immutable — see
        // AssignMacroHotkey). Names are unique — the store's Upsert enforces it.
        var macroWasDown = new Dictionary<string, bool>();

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

            // Outside the armed check, like the corner escape hatch above and
            // for the same reason. This key is what switches the others off, so
            // gating it on their being on would make it a one-way door: press
            // once to disable, and there is no press that brings them back.
            bool masterDown = IsKeyDown(s.MasterHotkeyVk);

            // Gated on the rebind half only, never on the on/off switch. Both
            // halves matter for different reasons: skipping the switch is the
            // whole point of a master key, but skipping the rebind guard would
            // fire the action on the very press that binds it — the key is
            // still physically down at the moment it becomes the binding.
            if (s.RebindIdle && wasRebindIdle && masterDown && !masterWasDown)
                Dispatcher.InvokeAsync(OnMasterHotkey, DispatcherPriority.Send);

            masterWasDown = masterDown;
            wasRebindIdle = s.RebindIdle;

            bool clickDown = IsKeyDown(s.HotkeyVk);
            bool recordDown = IsKeyDown(s.RecordHotkeyVk);
            bool replayDown = IsKeyDown(s.ReplayHotkeyVk);
            bool comboDown = IsKeyDown(s.ComboHotkeyVk);
            bool buildDown = IsKeyDown(s.BuildHotkeyVk);
            bool switcherDown = IsKeyDown(s.SwitcherHotkeyVk);
            bool clickSwitchDown = IsKeyDown(s.ClickSwitchHotkeyVk);

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

                if (buildDown != buildWasDown)
                    Dispatcher.InvokeAsync(() => OnBuildHotkey(buildDown), DispatcherPriority.Send);

                if (recordDown && !recordWasDown)
                    Dispatcher.InvokeAsync(OnRecordHotkey, DispatcherPriority.Send);

                if (replayDown && !replayWasDown)
                    Dispatcher.InvokeAsync(OnReplayHotkey, DispatcherPriority.Send);

                // Press only, not both edges. The switcher latches — holding
                // the key down is not a request to keep swapping.
                if (switcherDown && !switcherWasDown)
                    Dispatcher.InvokeAsync(OnSwitcherHotkey, DispatcherPriority.Send);

                // Both edges, so hold mode works the same as the plain click
                // key. The switcher half only acts on the press.
                if (clickSwitchDown != clickSwitchWasDown)
                    Dispatcher.InvokeAsync(() => OnClickSwitchHotkey(clickSwitchDown), DispatcherPriority.Send);
            }

            // Updated every pass, armed or not, so the edge is always measured
            // against the poll before it rather than against whenever the last
            // action happened to fire.
            clickWasDown = clickDown;
            recordWasDown = recordDown;
            replayWasDown = replayDown;
            comboWasDown = comboDown;
            buildWasDown = buildDown;
            switcherWasDown = switcherDown;
            clickSwitchWasDown = clickSwitchDown;

            // Per-macro toggles, same edge-triggered rule as the fixed hotkeys.
            // Snapshotted per pass — a rebind that swaps a KeyMacro out from
            // under this loop just changes the VK on the next tick, and the was
            // -down entry keyed by name carries over.
            //
            // The armed check is honoured (the master switch turns these off
            // with everything else), and the wasArmed prime-once rule prevents
            // rebind or master-on from firing on a still-held key. Bindings not
            // present this pass are dropped from macroWasDown so a rebind to
            // Unbound cannot rearm on a stale entry after re-binding to a key.
            KeyMacro[] macrosNow = _macroList.ToArray();
            var namesSeen = new HashSet<string>();

            foreach (KeyMacro m in macrosNow)
            {
                if (!m.Hotkey.IsValid) continue;

                namesSeen.Add(m.Name);
                bool down = IsKeyDown(m.Hotkey.VirtualKey);
                macroWasDown.TryGetValue(m.Name, out bool wasDown);

                if (s.HotkeysArmed && wasArmed && down && !wasDown)
                {
                    // Copy for the closure — the array reference above is fine,
                    // but the loop variable itself is captured by the lambda.
                    KeyMacro macroForDispatch = m;
                    Dispatcher.InvokeAsync(() => OnMacroHotkey(macroForDispatch), DispatcherPriority.Send);
                }

                macroWasDown[m.Name] = down;
            }

            // Prune entries whose macro is gone or unbound, so re-adding a macro
            // with the same name later cannot inherit a wasDown from before.
            if (macroWasDown.Count > namesSeen.Count)
            {
                foreach (string stale in macroWasDown.Keys.Where(n => !namesSeen.Contains(n)).ToArray())
                    macroWasDown.Remove(stale);
            }

            wasArmed = s.HotkeysArmed;

            Thread.Sleep(HotkeyPollMs);
        }
    }

    /// <summary>
    /// Toggles a macro on and off, keyed by name so a rebind that replaced the
    /// KeyMacro object between poll and dispatch still finds it.
    /// </summary>
    /// <remarks>
    /// Focus-in-a-textbox guard, same as the fixed hotkeys — a key typed into
    /// one of the form fields must not also toggle a macro. Not repeat-guarded
    /// because HotkeyLoop only dispatches on the down edge.
    /// </remarks>
    private void OnMacroHotkey(KeyMacro macro)
    {
        if (IsActive && Keyboard.FocusedElement is TextBox) return;

        // Lookup by name so a replacement (rebind, interval change) is picked
        // up rather than an orphaned old object being restarted.
        KeyMacro? current = _macroList.FirstOrDefault(m =>
            string.Equals(m.Name, macro.Name, StringComparison.OrdinalIgnoreCase));

        if (current == null) return;

        if (_macros.IsRunning(current.Name)) _macros.Stop(current.Name);
        else _macros.Start(current);

        // Keeps the toggle switch's IsChecked in step with the runner without
        // rebuilding the whole card set on every tick.
        BuildMacroCards();
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
        NotifySaved();
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

    private void MasterHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_rebinding == RebindTarget.Master) CancelRebind();
        else BeginRebind(RebindTarget.Master);
    }

    private void ClickSwitchHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_rebinding == RebindTarget.ClickSwitch) CancelRebind();
        else BeginRebind(RebindTarget.ClickSwitch);
    }

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

    /// <summary>Which button the click engine presses.</summary>
    private ClickButton _clickButton = ClickButton.Left;

    private void ClickButton_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;

        _clickButton = ClickButtons.Parse(tag);

        // Published to the click loop rather than read by it, the same as every
        // other setting — the loop takes one snapshot per cycle.
        UpdateEngineSettings();

        _settingsDirty = true;
    }

    /// <summary>
    /// Starts the clicker and the auto switcher on one press, and stops both.
    /// </summary>
    /// <remarks>
    /// Shaped like the shake combo above rather than like two hotkeys fired at
    /// once, because the switcher is a latch and the clicker is not. Driving the
    /// switcher from the clicker's state — on when it starts, off when it stops
    /// — is what keeps the two from drifting apart after a few presses, which is
    /// exactly what happens when one key is simply bound to both actions.
    /// </remarks>
    private void OnClickSwitchHotkey(bool pressed)
    {
        if (IsActive && Keyboard.FocusedElement is TextBox) return;

        if (pressed)
        {
            if (_holdMode)
            {
                if (!_running)
                {
                    SetSwitcher(true);
                    StartClicking();
                }

                return;
            }

            if (_running)
            {
                StopClicking();
                SetSwitcher(false);
            }
            else
            {
                SetSwitcher(true);
                StartClicking();
            }

            return;
        }

        if (_holdMode && _running)
        {
            StopClicking();
            SetSwitcher(false);
        }
    }

    private void SetSwitcher(bool on)
    {
        if (SwitcherEnabled != null) SwitcherEnabled.IsChecked = on;
    }

    private void BuildHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_rebinding == RebindTarget.Build) CancelRebind();
        else BeginRebind(RebindTarget.Build);
    }

    /// <summary>
    /// Clicks at the fixed building rate for as long as it is on.
    /// </summary>
    /// <remarks>
    /// Symmetric with the other start keys, and it clears its own mode on the
    /// way out. Leaving build mode set after stopping would make the ordinary
    /// click key silently run at 35/s the next time it was pressed.
    /// </remarks>
    private void OnBuildHotkey(bool pressed)
    {
        if (IsActive && Keyboard.FocusedElement is TextBox) return;

        if (pressed)
        {
            if (_holdMode)
            {
                if (!_running) SetBuildMode(true);
                if (!_running) StartClicking();
                return;
            }

            if (_running)
            {
                StopClicking();
            }
            else
            {
                SetBuildMode(true);
                StartClicking();
            }

            return;
        }

        if (_holdMode && _running) StopClicking();
    }

    /// <summary>
    /// Turns the fixed building rate on or off and republishes the snapshot the
    /// click thread reads, since the rate lives there rather than on a slider.
    /// </summary>
    private void SetBuildMode(bool on)
    {
        if (_buildMode == on) return;

        _buildMode = on;
        UpdateEngineSettings();
        RefreshStatus();
    }

    private Button RebindButtonFor(RebindTarget target) => target switch
    {
        RebindTarget.Replay => ReplayHotkeyButton,
        RebindTarget.Record => RecordHotkeyButton,
        RebindTarget.Combo => ComboHotkeyButton,
        RebindTarget.Build => BuildHotkeyButton,
        RebindTarget.Switcher => SwitcherHotkeyButton,
        RebindTarget.Master => MasterHotkeyButton,
        RebindTarget.ClickSwitch => ClickSwitchHotkeyButton,
        // For a macro, the button is per-card (or the NEW MACRO form's button)
        // and is stashed when the rebind begins. Fall back to HotkeyButton only
        // if the stash is empty, which should not happen in practice.
        RebindTarget.Macro => _rebindingMacroButton ?? HotkeyButton,
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

        // Who claimed each key, not merely that it is claimed. The clicker and
        // the switcher are allowed to share one, so "already taken" is not
        // enough to decide — it matters who took it.
        var owners = new Dictionary<int, RebindTarget>();

        foreach ((RebindTarget target, HotkeyBinding binding) in AllBindings())
        {
            if (!binding.IsValid) continue;

            if (claimed.Add(binding.VirtualKey))
            {
                owners[binding.VirtualKey] = target;
                continue;
            }

            if (owners.TryGetValue(binding.VirtualKey, out RebindTarget owner)
                && MayShareKey(owner, target))
            {
                continue;
            }

            switch (target)
            {
                case RebindTarget.Click: _hotkeySettings.Hotkey = HotkeyBinding.Unbound; break;
                case RebindTarget.Record: _hotkeySettings.RecordHotkey = HotkeyBinding.Unbound; break;
                case RebindTarget.Combo: _hotkeySettings.ComboHotkey = HotkeyBinding.Unbound; break;
                case RebindTarget.Build: _hotkeySettings.BuildHotkey = HotkeyBinding.Unbound; break;
                case RebindTarget.Switcher: _hotkeySettings.SwitcherHotkey = HotkeyBinding.Unbound; break;
                case RebindTarget.Master: _hotkeySettings.MasterHotkey = HotkeyBinding.Unbound; break;
                case RebindTarget.ClickSwitch: _hotkeySettings.ClickSwitchHotkey = HotkeyBinding.Unbound; break;
                default: _hotkeySettings.ReplayHotkey = HotkeyBinding.Unbound; break;
            }
        }

        // Named actions above win over macros, and earlier macros win over later
        // ones. A macro that repeats a key already taken is dropped in place —
        // same rule the fixed slots use, so the list has one owner per key.
        bool anyMacroChanged = false;
        for (int i = 0; i < _macroList.Count; i++)
        {
            KeyMacro m = _macroList[i];
            if (!m.Hotkey.IsValid) continue;

            if (claimed.Add(m.Hotkey.VirtualKey)) continue;

            _macroList[i] = new KeyMacro(m.Name, m.Keys, m.KeysText, m.IntervalMs, hotkey: HotkeyBinding.Unbound);
            anyMacroChanged = true;
        }

        if (anyMacroChanged) MacroStore.Save(_macroList);
    }

    /// <summary>
    /// Whether two actions are allowed to answer to the same key.
    /// </summary>
    /// <remarks>
    /// Only the clicker and the switcher, and only because that pairing is the
    /// point rather than an accident: one key that starts clicking and starts
    /// rotating is how the technique is actually played, and binding them apart
    /// means pressing two keys at the moment there is least time to.
    ///
    /// Every other pair stays refused. Two actions on one key is normally a
    /// mistake, and firing both looks like the app malfunctioning.
    /// </remarks>
    private static bool MayShareKey(RebindTarget a, RebindTarget b) =>
        (a == RebindTarget.Click && b == RebindTarget.Switcher)
        || (a == RebindTarget.Switcher && b == RebindTarget.Click);

    /// <summary>What an action is called when it has to be named in a refusal.</summary>
    private static string ActionName(RebindTarget target) => target switch
    {
        RebindTarget.Click => "Clicker",
        RebindTarget.Replay => "Replay",
        RebindTarget.Record => "Record",
        RebindTarget.Combo => "Combo",
        RebindTarget.Build => "Building",
        RebindTarget.Switcher => "Switcher",
        RebindTarget.Master => "Master",
        RebindTarget.ClickSwitch => "Click+switch",
        _ => "A macro"
    };

    /// <summary>
    /// Every action and its current binding. One place, so the collision check
    /// and the button labels cannot disagree about how many actions exist.
    /// </summary>
    private (RebindTarget Target, HotkeyBinding Binding)[] AllBindings() => new[]
    {
        (RebindTarget.Click, _hotkeySettings.Hotkey),
        (RebindTarget.Replay, _hotkeySettings.ReplayHotkey),
        (RebindTarget.Record, _hotkeySettings.RecordHotkey),
        (RebindTarget.Combo, _hotkeySettings.ComboHotkey),
        (RebindTarget.Build, _hotkeySettings.BuildHotkey),
        (RebindTarget.Switcher, _hotkeySettings.SwitcherHotkey),
        (RebindTarget.Master, _hotkeySettings.MasterHotkey),
        (RebindTarget.ClickSwitch, _hotkeySettings.ClickSwitchHotkey)
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
    /// <summary>
    /// Names whatever already answers to a key.
    /// </summary>
    /// <remarks>
    /// "In use" on its own sent someone hunting for a binding they could not
    /// find, because the key they picked was F8 and F8 is the replay default —
    /// a binding that lives on a different page from the one they were on. The
    /// refusal has to say which action holds the key, or it reads as the app
    /// being wrong about a key nobody set.
    /// </remarks>
    private string HolderOf(int virtualKey, RebindTarget target, KeyMacro? macroTarget)
    {
        foreach ((RebindTarget other, HotkeyBinding binding) in AllBindings())
        {
            if (other == target || MayShareKey(target, other)) continue;
            if (binding.IsValid && binding.VirtualKey == virtualKey) return ActionName(other);
        }

        foreach (KeyMacro macro in _macroList)
        {
            if (ReferenceEquals(macro, macroTarget)) continue;
            if (macro.Hotkey.IsValid && macro.Hotkey.VirtualKey == virtualKey) return macro.Name;
        }

        // The unsaved NEW MACRO pick is the only holder left it could be.
        return "A macro";
    }

    private void ShowRebindRefused(RebindTarget target, string holder)
    {
        Button button = RebindButtonFor(target);

        // Trimmed so a long macro name cannot stretch the button across the row.
        string name = holder.Length > 14 ? holder[..13].TrimEnd() + "…" : holder;

        button.Content = name + " has it";
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
        _rebindingMacro = null;
        _rebindingMacroButton = null;
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
        KeyMacro? macroTarget = _rebindingMacro;
        _rebinding = RebindTarget.None;
        _rebindingMacro = null;
        _rebindingMacroButton = null;

        if (!binding.IsValid)
        {
            CancelRebind();
            return;
        }

        // Every other binding, not just one — a key could collide with any of
        // the actions it is not replacing. Fixed slots come from AllBindings so
        // adding a fixed action cannot leave a gap; macro slots come from the
        // list, so adding a macro cannot either. The current slot is skipped by
        // (target, macroTarget) — same rule as the fixed rebind, extended.
        bool CollidesWithFixed(RebindTarget t) => t != target && !MayShareKey(target, t);
        bool CollidesWithMacro(KeyMacro m) =>
            target != RebindTarget.Macro || !ReferenceEquals(m, macroTarget);

        bool clash =
            AllBindings().Any(b => CollidesWithFixed(b.Target) && b.Binding.VirtualKey == binding.VirtualKey)
            || _macroList.Any(m => m.Hotkey.IsValid && CollidesWithMacro(m) && m.Hotkey.VirtualKey == binding.VirtualKey)
            // The NEW MACRO form's pending pick counts too, unless it is the
            // slot being rebound now.
            || (!(target == RebindTarget.Macro && macroTarget == null)
                && _pendingNewMacroHotkey.IsValid
                && _pendingNewMacroHotkey.VirtualKey == binding.VirtualKey);

        if (clash)
        {
            CancelRebind();
            ShowRebindRefused(target, HolderOf(binding.VirtualKey, target, macroTarget));
            return;
        }

        if (target == RebindTarget.Macro)
        {
            AssignMacroHotkey(macroTarget, binding);
        }
        else
        {
            switch (target)
            {
                case RebindTarget.Click: _hotkeySettings.Hotkey = binding; break;
                case RebindTarget.Record: _hotkeySettings.RecordHotkey = binding; break;
                case RebindTarget.Combo: _hotkeySettings.ComboHotkey = binding; break;
                case RebindTarget.Build: _hotkeySettings.BuildHotkey = binding; break;
                case RebindTarget.Switcher: _hotkeySettings.SwitcherHotkey = binding; break;
                case RebindTarget.Master: _hotkeySettings.MasterHotkey = binding; break;
                case RebindTarget.ClickSwitch: _hotkeySettings.ClickSwitchHotkey = binding; break;
                default: _hotkeySettings.ReplayHotkey = binding; break;
            }

            _hotkeySettings.Save();
        }

        // The poll thread re-primes its own edge state while disarmed, so
        // releasing the just-bound key cannot read as a fresh press.
        // The badge and button now read the new binding, which is the confirmation.
        ApplyHotkeyToUi();
        UpdateEngineSettings();

        // Otherwise the rebind button keeps focus and swallows the next Space.
        Focus();
    }

    /// <summary>
    /// Sets the new binding on either an existing macro or the NEW MACRO form.
    /// </summary>
    /// <remarks>
    /// KeyMacro is immutable, so an existing macro is replaced with an otherwise
    /// identical copy that carries the new toggle. Stopping the runner first
    /// matches SaveMacro_Click's rule — otherwise the old KeyMacro's thread
    /// keeps typing after its card is gone.
    /// </remarks>
    private void AssignMacroHotkey(KeyMacro? macro, HotkeyBinding binding)
    {
        if (macro == null)
        {
            // NEW MACRO form: hold the pick until Save creates the macro.
            _pendingNewMacroHotkey = binding;
            if (NewMacroHotkeyButton != null) NewMacroHotkeyButton.Content = binding.Name;
            return;
        }

        _macros.Stop(macro.Name);

        var replacement = new KeyMacro(
            macro.Name, macro.Keys, macro.KeysText, macro.IntervalMs,
            hotkey: binding);

        MacroStore.Upsert(_macroList, replacement);
        MacroStore.Save(_macroList);

        BuildMacroCards();
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
        bool UltraAccuracy, bool HitFix, bool HoldMode,
        int HotkeyVk, int ReplayHotkeyVk, int RecordHotkeyVk,
        int ComboHotkeyVk, int BuildHotkeyVk, int SwitcherHotkeyVk,
        int MasterHotkeyVk, int ClickSwitchHotkeyVk,
        bool HotkeysArmed, bool RebindIdle, bool BuildMode,
        ClickButton Button = ClickButton.Left)
    {
        /// <summary>
        /// The timing actually sent, which is the fixed building rate whenever
        /// build mode is on and the sliders otherwise.
        /// </summary>
        /// <remarks>
        /// Building deliberately ignores HitFix as well as the sliders. HitFix
        /// floors the press at 15ms, which against a 28.6ms cycle would drag the
        /// duty cycle from 1% to over 50% and turn a tap into a held button —
        /// the opposite of what placing blocks needs.
        /// </remarks>
        public ClickTiming Timing => BuildMode
            ? ClickTimings.Resolve(BuildCps, BuildDuty, hitFix: false)
            : ClickTimings.Resolve(Cps, Duty, HitFix);
    }

    /// <summary>
    /// Fixed rate for the building hotkey. Not adjustable by design: it is a
    /// separate action with one job, and a slider that silently applied to it
    /// would make the key mean something different from one session to the next.
    /// </summary>
    private const double BuildCps = 35.0;

    /// <summary>1% duty — a 0.29ms tap inside a 28.6ms cycle.</summary>
    private const double BuildDuty = 0.01;

    private volatile ClickSettings _settings =
        new(10.0, 0.67, false, new ShakeRange(8, 20, 40, 8), 33, false, true, false, VK_F6, 0, 0, 0, 0, 0, 0, 0, false, true, false);

    private void ApplyAppSettings(AppSettings s)
    {
        ApplySwitcherToUi(s);

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
        RobloxPriority.IsChecked = s.RobloxPriority;

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

        // Checking the button raises Checked, which publishes it to the engine.
        _clickButton = ClickButtons.Parse(s.ClickButton);
        (_clickButton switch
        {
            JinxyClicker.ClickButton.Right => RightButtonMode,
            JinxyClicker.ClickButton.Middle => MiddleButtonMode,
            _ => LeftButtonMode
        }).IsChecked = true;

        // Name first, then the slider — the slider's changed event repaints the
        // layers, so the picture it repaints with has to be known by then.
        _wallpaperFile = Wallpaper.Resolve(s.WallpaperFile) != null ? s.WallpaperFile : "";
        WallpaperDimmingSlider.Value = Wallpaper.ClampDimming(s.WallpaperDimming);

        ApplyWallpaper();

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
            ClickButton = _clickButton.ToString(),
            HideValues = _valuesHidden,
            ReplayEnabled = ReplayEnabled.IsChecked == true,
            ReplaySeconds = ReplaySeconds,
            AccentColor = _accentHex,
            WindowOpacity = OpacitySlider.Value,
            LightTheme = ThemeLight.IsChecked == true,
            WallpaperFile = _wallpaperFile,
            WallpaperDimming = (int)WallpaperDimmingSlider.Value,
            RecordDisplay = _captureDisplay?.DeviceName,
            HotkeysEnabled = HotkeysEnabledToggle.IsChecked == true,
            RobloxPriority = RobloxPriority.IsChecked == true,
            ClipFolder = ClipFolderBox.Text.Trim(),
            SwitcherSlotA = SlotABox.Text.Trim(),
            SwitcherSlotB = SlotBBox.Text.Trim(),
            SwitcherIntervalMs = MacroStore.ParseInterval(SwitcherIntervalBox.Text) ?? 500,
            SwitcherIntervalBMs = MacroStore.ParseInterval(SwitcherIntervalBBox.Text) ?? 40,
            SwitcherEquipMs = MacroStore.ParseInterval(SwitcherEquipBox.Text) ?? KeyMacro.DefaultEquipMs,
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
            HitFix?.IsChecked == true,
            _holdMode,
            _hotkeySettings.Hotkey.VirtualKey,
            _hotkeySettings.ReplayHotkey.VirtualKey,
            _hotkeySettings.RecordHotkey.VirtualKey,
            _hotkeySettings.ComboHotkey.VirtualKey,
            _hotkeySettings.BuildHotkey.VirtualKey,
            _hotkeySettings.SwitcherHotkey.VirtualKey,
            _hotkeySettings.MasterHotkey.VirtualKey,
            _hotkeySettings.ClickSwitchHotkey.VirtualKey,
            // Armed only when nothing is being rebound and the master switch is
            // on. Null-conditional because this runs once from the constructor,
            // before every control is necessarily built.
            _rebinding == RebindTarget.None && HotkeysEnabledToggle?.IsChecked != false,
            // Half of the condition above, on its own. The master key ignores
            // the on/off switch — that is its job — but must still not fire
            // while a rebind is capturing the very key being pressed.
            _rebinding == RebindTarget.None,
            _buildMode,
            _clickButton);

        UpdateHitFixClamp();
    }

    /// <summary>
    /// Says so on the clicker page when HitFix is overriding the sliders.
    /// </summary>
    /// <remarks>
    /// Without this the two controls silently go flat: at 102 CPS on a 99% duty
    /// cycle the floors rewrite it to 20 /s at 50%, and every higher setting
    /// produces byte-identical output. The Measured readout showed the truth,
    /// but nothing connected it to the sliders being ignored.
    /// </remarks>
    private void UpdateHitFixClamp()
    {
        if (HitFixClampText == null) return;

        ClickSettings s = _settings;

        if (_valuesHidden || !ClickTimings.IsClamped(s.Cps, s.Duty, s.HitFix))
        {
            HitFixClampText.Visibility = Visibility.Collapsed;
            return;
        }

        ClickTiming timing = ClickTimings.Resolve(s.Cps, s.Duty, s.HitFix);

        HitFixClampText.Text =
            $"HitFix is sending {timing.Cps:0.0} /s at {timing.DutyPercent:0} %, "
            + "not what the sliders say. Lower them until this matches.";
        HitFixClampText.Visibility = Visibility.Visible;
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
        ShakeSpeedValue.Text = Format(_shakeSpeed);

        static string Format(double v) => ((int)Math.Round(v)).ToString(CultureInfo.CurrentCulture);
    }

    private void ShakeValue_LostFocus(object sender, RoutedEventArgs e) => CommitShakeValue(sender as TextBox);

    private void ShakeValue_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitShakeValue(sender as TextBox);
            Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Put the committed figure back; Escape still reaches the stop.
            WriteShakeBoxes();
        }
    }

    /// <summary>
    /// Applies a typed figure to the slider on the same row.
    /// </summary>
    /// <remarks>
    /// Clamped to that slider's own range rather than to a shared constant, so a
    /// box can never hold a value its slider is unable to show. The sign carries
    /// no information — each row is a distance in one direction — and text that
    /// will not parse leaves the committed value alone.
    ///
    /// Assigning Value is enough on its own: it raises ValueChanged, which
    /// stores the field, rewrites the row and republishes the engine snapshot.
    /// </remarks>
    private void CommitShakeValue(TextBox? box)
    {
        if (box == null || ShakeSpeedValue == null) return;

        Slider slider =
            box == ShakeLeftValue ? ShakeLeftSlider
            : box == ShakeRightValue ? ShakeRightSlider
            : box == ShakeUpValue ? ShakeUpSlider
            : box == ShakeDownValue ? ShakeDownSlider
            : ShakeSpeedSlider;

        if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double typed))
            slider.Value = Math.Round(Math.Clamp(Math.Abs(typed), slider.Minimum, slider.Maximum));

        // Unconditional: a rejected or clamped entry has to be replaced by what
        // was actually stored, or the box would keep showing something untrue.
        WriteShakeBoxes();
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

        // Republished only when the reply changes the spin decision, not on
        // every reply. A probe lands once a second and UpdateEngineSettings
        // marks the settings dirty, so doing this unconditionally rewrote the
        // settings file every second for as long as Ping Sync was on — tens of
        // thousands of writes a day to store a value that had not changed.
        bool high = _lastPingMs >= HighPingMs;

        if (high != _pingWasHigh)
        {
            _pingWasHigh = high;
            UpdateEngineSettings();
        }
    }

    /// <summary>Whether the last probe was above the threshold.</summary>
    private bool _pingWasHigh;

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

        // Cleared on every stop, whichever key caused it. Otherwise stopping a
        // building run with the ordinary key would leave the fixed rate armed,
        // and the next ordinary start would silently run at 35/s.
        _buildMode = false;

        // Same rule, and for a sharper reason: the switcher is a latch on its
        // own thread, so every stop that is not the combined hotkey used to
        // leave it running. The rotation then kept swapping weapons with
        // nothing clicking — worse than useless mid-fight, and it happened
        // whichever way the clicker was stopped: its own key, the Stop button,
        // or the corner escape hatch.
        //
        // Unconditional rather than only undoing what the combined hotkey
        // started. A rotation exists to feed the clicking; one running without
        // it is never what was wanted, and tracking who switched it on would
        // make the same key behave differently depending on history.
        SetSwitcher(false);

        UpdateEngineSettings();

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

        // Which button the press in flight used, so the release matches it.
        ClickButton held = ClickButton.Left;

        // Windows 11 throttles timer resolution for processes that are not in
        // the foreground, which silently undoes the TimeBeginPeriod below at
        // exactly the moment it matters — the clicker is in the background
        // whenever the game is being played. Measured against a competing
        // clicker: matching median timing (31.13ms vs 31.15ms period) but a
        // period standard deviation of 37.95ms against its 2.21ms, with single
        // gaps stretching to 617ms. Opting out is what closes that gap.
        KeepTimerResolutionInBackground();

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

                // Shared with the readout on the clicker page, so what is shown
                // and what is sent cannot drift apart. Build mode substitutes
                // its own fixed rate inside this.
                ClickTiming timing = s.Timing;
                double period = timing.PeriodMs;
                double downMs = timing.DownMs;

                // Windows stalls this loop for hundreds of milliseconds at a
                // time — measured at up to 1.4s with a low-level hook, and a
                // competing clicker stalls just as badly, so the stalls are not
                // something this app can avoid.
                //
                // What it can avoid is compounding them. Resetting the deadline
                // to now discarded every click the stall cost, so a 32/s setting
                // delivered 27.1/s while the competitor delivered 31.1/s from
                // the same target and the same stalls. Letting the schedule run
                // a bounded amount behind makes those clicks up instead; the
                // bound is what stops a long stall becoming one long burst.
                long now = Stopwatch.GetTimestamp();
                long maxLag = (long)(period * CatchUpPeriods * freq / 1000.0);
                if (deadline < now - maxLag) deadline = now - maxLag;

                // The press and its release are held together against the shake
                // thread, which injects mouse movement on its own schedule. A
                // move landing between them turns the click into a drag, and a
                // drag is not a click as far as the game is concerned. On a 50%
                // duty cycle the button is down half the time, so roughly half
                // of all shake movement was landing inside a press.
                bool cancelled;

                lock (_inputGate)
                {
                    // Captured, not re-read on release. Switching button while
                    // clicking would otherwise press one and release another,
                    // leaving the first held down across the whole desktop with
                    // nothing left to let it go.
                    held = s.Button;

                    SendButtonDown(held);
                    buttonDown = true;
                    deadline += (long)(downMs * freq / 1000.0);

                    cancelled = !WaitUntil(deadline, s.UltraAccuracy, token);

                    if (!cancelled)
                    {
                        SendButtonUp(held);
                        buttonDown = false;
                        Interlocked.Increment(ref _clickCount);
                    }
                }

                if (cancelled) break;

                // Deliberately outside the lock: the gap between clicks is when
                // the shake is free to move, which is most of the cycle.
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

            // The one that was pressed, whatever is selected now.
            if (buttonDown) SendButtonUp(held);
            if (raisedTimer) TimeEndPeriod(TimerResolutionMs);
        }
    }

    private void BankActiveTime(ref long activeSince)
    {
        if (activeSince == 0) return;

        Interlocked.Add(ref _activeTicks, Stopwatch.GetTimestamp() - activeSince);
        activeSince = 0;
    }

    /// <summary>
    /// Held while the left button is down, so nothing else injects input in the
    /// middle of a click.
    /// </summary>
    private readonly object _inputGate = new();

    /// <summary>
    /// Longest the shake will wait for a press to finish before giving up on
    /// that movement. Bounded because a low rate on a high duty cycle can hold
    /// the button for most of a second, and stalling the shake thread outright
    /// would be worse than dropping one step of it.
    /// </summary>
    private const int InputGateWaitMs = 120;

    /// <summary>
    /// Sends a relative mouse move, but never between a press and its release.
    /// </summary>
    /// <returns>False if the click stream was busy and the move was skipped.</returns>
    private bool MoveWithoutSplittingAClick(int dx, int dy)
    {
        if (!Monitor.TryEnter(_inputGate, InputGateWaitMs)) return false;

        try
        {
            SendMouseMove(dx, dy);
            return true;
        }
        finally
        {
            Monitor.Exit(_inputGate);
        }
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
                        if (MoveWithoutSplittingAClick(-offsetX, -offsetY))
                        {
                            offsetX = 0;
                            offsetY = 0;
                        }
                    }

                    Thread.Sleep(25);
                    continue;
                }

                int targetX = NextOffset(-range.Left, range.Right);
                int targetY = NextOffset(-range.Up, range.Down);

                int dx = targetX - offsetX;
                int dy = targetY - offsetY;

                // The offsets only advance if the move actually went out, or the
                // cursor's real position and what this thread believes it to be
                // would drift apart and the return-to-origin would be wrong.
                if ((dx != 0 || dy != 0) && MoveWithoutSplittingAClick(dx, dy))
                {
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
            // Undo the outstanding displacement so the crosshair ends where it
            // began. Through the gate like every other move: the click thread may
            // still be mid-press while this one is shutting down.
            if (offsetX != 0 || offsetY != 0) MoveWithoutSplittingAClick(-offsetX, -offsetY);

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

    /// <summary>
    /// Asks Windows not to ignore this process's timer resolution requests when
    /// it is in the background.
    /// </summary>
    /// <remarks>
    /// Clearing the state bit while setting the control bit is the documented
    /// way to say "manage this explicitly, and do not throttle me" — setting
    /// both would ask for the opposite. Best effort: on a build without the
    /// behaviour the call simply fails, and the loop still works, just with the
    /// coarser sleeps it had before.
    /// </remarks>
    private static void KeepTimerResolutionInBackground()
    {
        try
        {
            var state = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = ProcessPowerThrottlingCurrentVersion,
                ControlMask = ProcessPowerThrottlingIgnoreTimerResolution,
                StateMask = 0
            };

            SetProcessInformation(
                GetCurrentProcess(),
                ProcessPowerThrottlingInformation,
                ref state,
                (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
        }
        catch
        {
            // Older Windows, or a policy that forbids it. Not worth a crash.
        }
    }

    private const int ProcessPowerThrottlingInformation = 4;
    private const uint ProcessPowerThrottlingCurrentVersion = 1;
    private const uint ProcessPowerThrottlingIgnoreTimerResolution = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(
        IntPtr process, int informationClass,
        ref PROCESS_POWER_THROTTLING_STATE info, uint size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

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
        BuildHotkeyButton.Content = _hotkeySettings.BuildHotkey.Name;
        SwitcherHotkeyButton.Content = _hotkeySettings.SwitcherHotkey.Name;
        MasterHotkeyButton.Content = _hotkeySettings.MasterHotkey.Name;
        ClickSwitchHotkeyButton.Content = _hotkeySettings.ClickSwitchHotkey.Name;

        // The bindings live on three different pages, so Settings is the only
        // place they can all be read at once.
        if (HotkeySummaryText != null)
        {
            HotkeySummaryText.Text = string.Join("     ",
                $"{_hotkeySettings.Hotkey.Name} — click",
                $"{_hotkeySettings.ReplayHotkey.Name} — replay",
                $"{_hotkeySettings.RecordHotkey.Name} — record",
                $"{_hotkeySettings.ComboHotkey.Name} — click + shake",
                $"{_hotkeySettings.BuildHotkey.Name} — building",
                $"{_hotkeySettings.ClickSwitchHotkey.Name} — click + switcher",
                $"{_hotkeySettings.MasterHotkey.Name} — hotkeys on/off");
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
    private static void SendButtonDown(ClickButton button) =>
        SendMouseEvent(ClickButtons.DownFlag(button));

    private static void SendButtonUp(ClickButton button) =>
        SendMouseEvent(ClickButtons.UpFlag(button));

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

        RestoreFlagsButton.IsEnabled = FastFlagStore.HasBackup();

        // Read from the file rather than from what was last clicked, so the row
        // reflects the client's actual state even if it was edited elsewhere.
        string? api = FastFlagStore.CurrentGraphicsApi();
        SelectGraphicsApi(api);

        if (GraphicsApiStateText != null)
        {
            GraphicsApiStateText.Text = api ?? "Client default";
            GraphicsApiStateText.Foreground =
                (System.Windows.Media.Brush)FindResource(api == null ? "TextMuted" : "Accent");
        }
    }

    private void ApplyFlags_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Before the first write, not after, or the "backup" would be a copy
            // of this app's own output.
            FastFlagStore.Backup();
            FastFlagStore.Apply(FastFlagStore.FpsBoost);
            RefreshFlags();
            ShowFlagStatus("Applied. Restart Roblox for it to take effect.", isError: false);
        }
        catch (Exception ex)
        {
            ShowFlagStatus(ex.Message, isError: true);
        }
    }

    private void RestoreFlags_Click(object sender, RoutedEventArgs e)
    {
        if (FastFlagStore.RestoreBackup())
        {
            RefreshFlags();
            ShowFlagStatus("Client settings put back as they were. Restart Roblox.", isError: false);
        }
        else
        {
            ShowFlagStatus("No backup to restore.", isError: true);
        }
    }

    /// <summary>
    /// Detects the adapter and applies the backend that suits it.
    /// </summary>
    /// <remarks>
    /// Deliberately not called "best". It cannot know that — which backend wins
    /// depends on the driver revision and the scene, and only running each and
    /// comparing settles it. What it does is avoid the wrong answers and give a
    /// sensible starting point, and it says so on screen rather than presenting
    /// a guess as a measurement.
    /// </remarks>
    private void DetectApi_Click(object sender, RoutedEventArgs e)
    {
        ApiSuggestion suggestion = GpuInfo.Recommend();

        try
        {
            FastFlagStore.Backup();
            FastFlagStore.ApplyGraphicsApi(suggestion.Api);
            RefreshFlags();

            ApiAdviceText.Text = suggestion.Reason;
            ShowFlagStatus(
                suggestion.Api == null
                    ? "Left to the client. Restart Roblox."
                    : $"Set to {suggestion.Api}. Restart Roblox.",
                isError: false);
        }
        catch (Exception ex)
        {
            ShowFlagStatus(ex.Message, isError: true);
        }
    }

    /// <summary>Guards the API buttons against the code that sets them.</summary>
    private bool _writingGraphicsApi;

    private void GraphicsApi_Checked(object sender, RoutedEventArgs e)
    {
        if (_writingGraphicsApi || sender is not RadioButton { Tag: string tag }) return;

        try
        {
            FastFlagStore.Backup();

            // Empty tag is "Default", which means no preference set at all.
            FastFlagStore.ApplyGraphicsApi(string.IsNullOrEmpty(tag) ? null : tag);

            RefreshFlags();
            ShowFlagStatus(
                string.IsNullOrEmpty(tag)
                    ? "Graphics API left to the client. Restart Roblox."
                    : $"Graphics API set to {tag}. Restart Roblox.",
                isError: false);
        }
        catch (Exception ex)
        {
            ShowFlagStatus(ex.Message, isError: true);
        }
    }

    /// <summary>Checks the button matching the file, without reapplying it.</summary>
    private void SelectGraphicsApi(string? api)
    {
        if (GraphicsApiPanel == null) return;

        _writingGraphicsApi = true;

        try
        {
            foreach (object child in GraphicsApiPanel.Children)
            {
                if (child is RadioButton { Tag: string tag } button)
                    button.IsChecked = string.Equals(tag, api ?? string.Empty, StringComparison.Ordinal);
            }
        }
        finally
        {
            _writingGraphicsApi = false;
        }
    }

    private void RobloxPriority_Changed(object sender, RoutedEventArgs e)
    {
        _settingsDirty = true;

        if (RobloxPriority?.IsChecked == true) ApplyRobloxPriority();
        else RestoreRobloxPriority();
    }

    /// <summary>
    /// Nudges the Roblox process above normal priority.
    /// </summary>
    /// <remarks>
    /// Called from the stats tick as well as on toggle, so it reapplies after
    /// Roblox restarts — priority is a property of the process, and a new one
    /// starts at normal. Above normal rather than high: high can starve input
    /// handling on a weak machine, which would cost more than it gained.
    /// </remarks>
    private void ApplyRobloxPriority()
    {
        int changed = 0, found = 0;

        foreach (Process process in Process.GetProcessesByName("RobloxPlayerBeta"))
        {
            try
            {
                found++;

                if (process.PriorityClass != ProcessPriorityClass.AboveNormal)
                {
                    process.PriorityClass = ProcessPriorityClass.AboveNormal;
                    changed++;
                }
            }
            catch
            {
                // Exited between listing and setting, or not ours to touch.
            }
            finally
            {
                process.Dispose();
            }
        }

        if (RobloxPriorityStateText == null) return;

        RobloxPriorityStateText.Text = found == 0
            ? "Roblox is not running — this applies as soon as it starts."
            : $"Roblox is running at above normal priority.{(changed > 0 ? " Just raised it." : "")}";
    }

    private void RestoreRobloxPriority()
    {
        foreach (Process process in Process.GetProcessesByName("RobloxPlayerBeta"))
        {
            try
            {
                if (process.PriorityClass == ProcessPriorityClass.AboveNormal)
                    process.PriorityClass = ProcessPriorityClass.Normal;
            }
            catch
            {
                // Nothing worth reporting; the process owns its own priority.
            }
            finally
            {
                process.Dispose();
            }
        }

        if (RobloxPriorityStateText != null) RobloxPriorityStateText.Text = "";
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
            NotifySaved();
            return;
        }

        try
        {
            _recorder.Start(ClipFolder, RecordFps, _captureDisplay);

            RecordButton.Content = "Stop recording";
            ShowRecordStatus("Recording the whole screen — everything visible is captured.", isError: false);
            NotifyRecording();
        }
        catch (Exception ex)
        {
            ShowRecordStatus(ex.Message, isError: true);
        }
    }

    /// <summary>
    /// The on-screen notice, built on first use.
    /// </summary>
    /// <remarks>
    /// Lazy because most sessions never record, and a window that is never shown
    /// still costs a handle and a render target.
    /// </remarks>
    private CaptureToast? _toast;

    private CaptureToast Toast => _toast ??= new CaptureToast { Owner = this };

    private void NotifyRecording() =>
        Toast.Notify("● Recording", Color.FromRgb(0xFF, 0x4B, 0x52), _captureDisplay);

    private void NotifySaved() =>
        Toast.Notify("✓ Saved", Color.FromRgb(0x5B, 0xE5, 0x8B), _captureDisplay);

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

        // The sidebar's brush is thinned in code when a wallpaper is set, and
        // repainting the palette hands it back an opaque one. Reapplied here,
        // or switching mode quietly hides the picture behind the sidebar.
        ApplyWallpaper();

        _settingsDirty = true;
    }

    /// <summary>Stored wallpaper file name, or empty when none is set.</summary>
    private string _wallpaperFile = "";

    /// <summary>
    /// How much of the sidebar's own colour survives over a wallpaper.
    /// </summary>
    /// <remarks>
    /// Not fully transparent. The nav buttons need something behind them to
    /// read against, and a photograph alone is not reliably darker than the
    /// text sitting on it.
    /// </remarks>
    private const byte SidebarOverWallpaperAlpha = 0xB0;

    private void ChooseWallpaper_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a wallpaper",
            Filter = Wallpaper.FileFilter,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true) return;

        string? stored = Wallpaper.Store(dialog.FileName);

        if (stored == null)
        {
            MessageBox.Show(this, "That image could not be used. It may be open in another program, or in a format this app cannot read.",
                "Wallpaper", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _wallpaperFile = stored;
        _settingsDirty = true;

        ApplyWallpaper();
    }

    private void RemoveWallpaper_Click(object sender, RoutedEventArgs e)
    {
        Wallpaper.Clear();

        _wallpaperFile = "";
        _settingsDirty = true;

        ApplyWallpaper();
    }

    private void WallpaperDimming_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Fires while the XAML is still being parsed, before the layers exist.
        if (WallpaperDim == null) return;

        ApplyWallpaper();

        _settingsDirty = true;
    }

    /// <summary>
    /// Paints the wallpaper layers, or takes them away.
    /// </summary>
    /// <remarks>
    /// One place decides all of it — image, dimming, the preview on the Theme
    /// page and the sidebar's transparency — so the four cannot drift into
    /// disagreeing about whether a wallpaper is set.
    /// </remarks>
    private void ApplyWallpaper()
    {
        int percent = Wallpaper.ClampDimming((int)WallpaperDimmingSlider.Value);
        WallpaperDimmingText.Text = percent + "%";

        string? path = Wallpaper.Resolve(_wallpaperFile);
        ImageSource? image = path == null ? null : LoadWallpaper(path);

        // A file that will not decode is not a wallpaper. Forgetting it here
        // stops a broken picture being reloaded on every launch.
        if (image == null) _wallpaperFile = "";

        WallpaperImage.Source = image;
        WallpaperPreview.Source = image;

        Visibility shown = image == null ? Visibility.Collapsed : Visibility.Visible;

        WallpaperImage.Visibility = shown;
        WallpaperDim.Visibility = shown;
        WallpaperPreview.Visibility = shown;
        WallpaperEmptyText.Visibility = image == null ? Visibility.Visible : Visibility.Collapsed;
        WallpaperDim.Opacity = Wallpaper.DimmingOpacity(percent);

        WallpaperNameText.Text = image == null ? "No wallpaper set" : _wallpaperFile;
        RemoveWallpaperButton.IsEnabled = image != null;

        ApplySidebarOverWallpaper(image != null);
    }

    /// <summary>Thins the sidebar so the picture reaches behind it.</summary>
    private void ApplySidebarOverWallpaper(bool wallpaperSet)
    {
        if (Resources["Sidebar"] is not SolidColorBrush sidebar) return;

        Color color = sidebar.Color;

        SidebarPanel.Background = wallpaperSet
            ? new SolidColorBrush(Color.FromArgb(SidebarOverWallpaperAlpha, color.R, color.G, color.B))
            : sidebar;
    }

    /// <summary>
    /// Decodes a wallpaper, detached from the file on disk.
    /// </summary>
    /// <remarks>
    /// OnLoad caching and a stream that is closed straight after: without it
    /// WPF holds the file open for as long as the image is shown, and choosing
    /// a replacement fails because the copy it is overwriting is locked by the
    /// window displaying it.
    /// </remarks>
    private static ImageSource? LoadWallpaper(string path)
    {
        try
        {
            var image = new BitmapImage();

            using (var stream = File.OpenRead(path))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
            }

            image.Freeze();

            return image;
        }
        catch
        {
            return null;
        }
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

        // Uppercased here rather than at every call site, so a page added later
        // cannot arrive in sentence case and break the header's rhythm.
        PageTitleText.Text = title.ToUpperInvariant();
        PageSubtitleText.Text = subtitle;
    }

    private Button[] NavButtons => new[]
    {
        NavClicker, NavPresets, NavTweaks, NavOptimizations,
        NavMacros, NavSwitcher, NavMod, NavRecorder, NavHistory, NavTheme, NavSettings
    };

    private UIElement[] Pages => new UIElement[]
    {
        PageClicker, PagePresets, PageTweaks, PageOptimizations,
        PageMacros, PageSwitcher, PageMod, PageRecorder, PageHistory, PageTheme, PageSettings
    };

    // ---- macros ----

    private readonly MacroRunner _macros = new();
    private readonly List<KeyMacro> _macroList = MacroStore.Load();

    /// <summary>
    /// The switcher's own macro name.
    /// </summary>
    /// <remarks>
    /// Leading space so it can never collide with something typed on the Macros
    /// page — a name is trimmed before it is saved there, so no user macro can
    /// ever be called this.
    /// </remarks>
    private const string SwitcherName = " AutoSwitcher";

    /// <summary>This window's handle, captured once for the macro threads.</summary>
    /// <remarks>
    /// Read on the UI thread and stored, because WindowInteropHelper cannot be
    /// touched from anywhere else. GetForegroundWindow can, so the comparison
    /// itself is safe on a macro thread.
    /// </remarks>
    private IntPtr _ownWindow;

    /// <summary>The send count when the switcher was last turned on.</summary>
    private long _switcherStartedAt;

    /// <summary>How long the game takes to put a weapon in hand. Tunable, because
    /// it is a guess that should be corrected against the hit counter.</summary>
    private int _switcherEquipMs = KeyMacro.DefaultEquipMs;

    /// <summary>Clicks the first slot must receive before the cycle moves on.</summary>
    /// <remarks>
    /// Two, so a click landing on the same instant the equip finishes cannot be
    /// the only one counted.
    /// </remarks>
    private const int SwitcherShots = 2;

    /// <summary>
    /// Updates the switcher's live count while it runs.
    /// </summary>
    /// <remarks>
    /// A number that climbs is the difference between "it is not working" and
    /// "it is working and you are looking at the wrong window" — which is
    /// exactly the confusion this feature produces on first use, because the
    /// keys deliberately go somewhere you are not looking.
    /// </remarks>
    private readonly DispatcherTimer _macroTicker =
        new() { Interval = TimeSpan.FromMilliseconds(400) };

    private void StartMacroTicker()
    {
        _macroTicker.Tick += (_, _) =>
        {
            if (!_macros.IsRunning(SwitcherName))
            {
                _macroTicker.Stop();
                return;
            }

            long swaps = _macros.Sent - _switcherStartedAt;

            SwitcherCountText.Text = swaps == 0
                ? "Waiting — nothing sent yet. Switch to the game and it starts."
                : $"{swaps:N0} presses sent.";
        };
    }

    /// <summary>
    /// Stops macros typing into this application.
    /// </summary>
    /// <remarks>
    /// While a macro is being set up the focused window is this one, so its
    /// keys land in the very fields being edited — and their change handler
    /// restarts the macro, so it fights itself and never reaches the game.
    /// Suppressing while we are in front makes "switch to the game" something
    /// the app does rather than something it asks for.
    /// </remarks>
    private void ArmMacroSuppression()
    {
        _ownWindow = new System.Windows.Interop.WindowInteropHelper(this).Handle;

        _macros.Suppressed = () => _ownWindow != IntPtr.Zero
                                   && GetForegroundWindow() == _ownWindow;

        // The same lock the shake engine takes. Without it a slot switch lands
        // between a mouse-down and its release and the game sees an interrupted
        // click — which is exactly why switching by hand fires fast and
        // switching on a timer does not.
        _macros.InputGate = _inputGate;

        // Lets a weapon dip end on the click that fires it rather than on a
        // timer generous enough to be safe. Read only.
        _macros.Clicks = () => Interlocked.Read(ref _clickCount);
    }

    private void NavMacros_Click(object sender, RoutedEventArgs e)
    {
        BuildMacroCards();
        ShowPage(NavMacros, PageMacros, "Macros", "Spam a key, or cycle a few");
    }

    private void NavSwitcher_Click(object sender, RoutedEventArgs e) =>
        ShowPage(NavSwitcher, PageSwitcher, "Auto Switcher", "Swap between two hotbar slots");

    private void BuildMacroCards()
    {
        MacroList.Items.Clear();

        foreach (KeyMacro macro in _macroList) MacroList.Items.Add(MacroCard(macro));

        RefreshMacroRunning();
    }

    /// <summary>
    /// One macro card: what it sends, how often, and a switch.
    /// </summary>
    /// <remarks>
    /// Built in code rather than as a DataTemplate because the toggle reflects
    /// the runner, and whether a macro is running is a fact about the process
    /// rather than a property of the macro.
    /// </remarks>
    private Border MacroCard(KeyMacro macro)
    {
        var name = new TextBlock
        {
            Text = macro.Name,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var summary = new TextBlock
        {
            Text = macro.SummaryText,
            FontSize = 11,
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = (Brush)FindResource("Accent")
        };

        var toggle = new CheckBox
        {
            IsChecked = _macros.IsRunning(macro.Name),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0)
        };

        toggle.Checked += (_, _) => { _macros.Start(macro); RefreshMacroRunning(); };
        toggle.Unchecked += (_, _) => { _macros.Stop(macro.Name); RefreshMacroRunning(); };

        // Rebind button for this macro's toggle key. Same "click, press a key"
        // flow as the fixed hotkeys — the button IS the prompt while it waits.
        var hotkeyLabel = new TextBlock
        {
            Text = "TOGGLE HOTKEY",
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextMuted"),
            Margin = new Thickness(0, 10, 0, 4)
        };

        var hotkeyButton = new Button
        {
            Content = macro.Hotkey.Name,
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        hotkeyButton.Click += (_, _) =>
        {
            if (_rebinding == RebindTarget.Macro && ReferenceEquals(_rebindingMacro, macro))
            {
                CancelRebind();
                return;
            }

            _rebindingMacro = macro;
            _rebindingMacroButton = hotkeyButton;
            BeginRebind(RebindTarget.Macro);
        };

        var remove = new Button
        {
            Content = "Delete",
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 11,
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        remove.Click += (_, _) =>
        {
            // Stopped before it is forgotten. Dropping a running macro from the
            // list without cancelling leaves a thread typing a key with nothing
            // on screen left to switch it off.
            _macros.Stop(macro.Name);

            // The rebind flow holds a reference to the macro being rebound —
            // deleting that macro mid-rebind would otherwise leave the button
            // waiting for a key that can never land anywhere useful.
            if (_rebinding == RebindTarget.Macro && ReferenceEquals(_rebindingMacro, macro))
                CancelRebind();

            _macroList.Remove(macro);
            MacroStore.Save(_macroList);

            BuildMacroCards();
        };

        var header = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(toggle, Dock.Right);
        header.Children.Add(toggle);
        header.Children.Add(name);

        var body = new StackPanel();
        body.Children.Add(header);
        body.Children.Add(summary);
        body.Children.Add(hotkeyLabel);
        body.Children.Add(hotkeyButton);
        body.Children.Add(remove);

        return new Border
        {
            Background = (Brush)FindResource("Control"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 10, 10),
            Width = 210,
            Child = body
        };
    }

    private void RefreshMacroRunning()
    {
        // The switcher runs on the same engine but belongs to its own page, so
        // it must not be counted here.
        int count = _macros.RunningCount - (_macros.IsRunning(SwitcherName) ? 1 : 0);

        MacroRunningText.Text = count switch
        {
            0 => "Nothing running.",
            1 => "1 macro running.",
            _ => $"{count} macros running."
        };
    }

    private void StopAllMacros_Click(object sender, RoutedEventArgs e)
    {
        foreach (KeyMacro macro in _macroList) _macros.Stop(macro.Name);

        BuildMacroCards();
    }

    private void SaveMacro_Click(object sender, RoutedEventArgs e)
    {
        MacroErrorText.Visibility = Visibility.Collapsed;

        string name = MacroNameBox.Text.Trim();

        if (name.Length == 0)
        {
            ShowMacroError("Give it a name.");
            return;
        }

        // The second box is optional, so it is joined only when it holds
        // something. Passing an empty one through would produce a trailing
        // separator and a parse that refuses a perfectly good single key.
        string typed = MacroKey1Box.Text.Trim();
        string second = MacroKey2Box.Text.Trim();

        if (second.Length > 0) typed += "," + second;

        (int[] Keys, string Text)? keys = MacroStore.ParseKeys(typed);

        if (keys == null)
        {
            ShowMacroError("Each key box takes one letter or digit — R, or 1, or Q.");
            return;
        }

        int? interval = MacroStore.ParseInterval(MacroIntervalBox.Text);

        if (interval == null)
        {
            ShowMacroError($"Interval must be between {KeyMacro.MinIntervalMs} and {KeyMacro.MaxIntervalMs} ms.");
            return;
        }

        // Replacing a running macro would otherwise leave the old thread going
        // with the old keys, invisibly, its card having been rebuilt.
        _macros.Stop(name);

        MacroStore.Upsert(_macroList, new KeyMacro(
            name, keys.Value.Keys, keys.Value.Text, interval.Value,
            hotkey: _pendingNewMacroHotkey));
        MacroStore.Save(_macroList);

        MacroNameBox.Clear();
        MacroKey1Box.Clear();
        MacroKey2Box.Clear();
        MacroIntervalBox.Clear();

        // The pending pick has been baked into the macro; the form's toggle
        // slot goes back to Unbound so the next macro starts fresh.
        _pendingNewMacroHotkey = HotkeyBinding.Unbound;
        if (NewMacroHotkeyButton != null) NewMacroHotkeyButton.Content = "Not set";

        BuildMacroCards();
    }

    /// <summary>Rebind entry point for the NEW MACRO form's toggle-hotkey slot.</summary>
    private void NewMacroHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_rebinding == RebindTarget.Macro && _rebindingMacro == null)
        {
            CancelRebind();
            return;
        }

        _rebindingMacro = null;                       // null == form slot
        _rebindingMacroButton = NewMacroHotkeyButton; // shows "Select A Hotkey"
        BeginRebind(RebindTarget.Macro);
    }

    private void ShowMacroError(string message)
    {
        MacroErrorText.Text = message;
        MacroErrorText.Visibility = Visibility.Visible;
    }

    // ---- auto switcher ----

    private void Switcher_Changed(object sender, RoutedEventArgs e) => RefreshSwitcher();

    private void SwitcherHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_rebinding == RebindTarget.Switcher) CancelRebind();
        else BeginRebind(RebindTarget.Switcher);
    }

    /// <summary>
    /// Flips the switcher from its hotkey.
    /// </summary>
    /// <remarks>
    /// Goes through the checkbox rather than starting the macro directly, so
    /// the page and the engine cannot disagree about whether it is running —
    /// the toggle is the single source of that answer.
    /// </remarks>
    private void OnSwitcherHotkey()
    {
        // Same guard the click keys carry. Bound to a letter or a digit, this
        // otherwise fires while its own slot and interval boxes are being
        // typed into — the fields sit on the page the key belongs to, so it is
        // the likeliest place for it to happen.
        if (IsActive && Keyboard.FocusedElement is TextBox) return;

        SwitcherEnabled.IsChecked = SwitcherEnabled.IsChecked != true;
    }

    /// <summary>
    /// Turns every other hotkey off, and back on again.
    /// </summary>
    /// <remarks>
    /// Flips the same switch the Settings checkbox does rather than keeping a
    /// second notion of whether hotkeys are on, so the box always shows the
    /// truth however it was last changed.
    ///
    /// Note this leaves a running clicker running — the same as unticking the
    /// box by hand — and its stop key is off along with the rest. Dropping the
    /// pointer into a screen corner still stops it.
    /// </remarks>
    private void OnMasterHotkey()
    {
        if (IsActive && Keyboard.FocusedElement is TextBox) return;

        HotkeysEnabledToggle.IsChecked = HotkeysEnabledToggle.IsChecked != true;
    }

    private void SwitcherField_Changed(object sender, TextChangedEventArgs e) => RefreshSwitcher();

    /// <summary>
    /// Rebuilds the switcher from its fields and starts or stops it.
    /// </summary>
    /// <remarks>
    /// Always stopped first. The keys and the rate can change while it runs, and
    /// a running thread holds the values it was started with — so the only way
    /// to apply an edit is to replace the thread.
    /// </remarks>
    private void RefreshSwitcher()
    {
        if (SlotABox == null || SlotBBox == null || SwitcherIntervalBox == null) return;

        _macros.Stop(SwitcherName);

        (int[] Keys, string Text)? keys =
            MacroStore.ParseKeys(SlotABox.Text.Trim() + "," + SlotBBox.Text.Trim());

        int? holdA = MacroStore.ParseInterval(SwitcherIntervalBox.Text);
        int? holdB = MacroStore.ParseInterval(SwitcherIntervalBBox.Text);

        _switcherEquipMs = MacroStore.ParseInterval(SwitcherEquipBox.Text) ?? KeyMacro.DefaultEquipMs;

        if (keys == null || keys.Value.Keys.Length != 2 || holdA == null || holdB == null)
        {
            SwitcherStatusText.Text =
                "Both slots need one letter or digit, and both hold times must be between "
                + $"{KeyMacro.MinIntervalMs} and {KeyMacro.MaxIntervalMs} ms.";

            return;
        }

        int? interval = holdA;

        _settingsDirty = true;

        if (SwitcherEnabled.IsChecked != true)
        {
            SwitcherStatusText.Text = "Off.";
            SwitcherCountText.Text = "";
            _macroTicker.Stop();
            return;
        }

        // Raised to whatever actually guarantees a shot, rather than trusting
        // the number in the box. The click period comes from the clicker's own
        // live settings, so changing CPS changes this without anyone noticing
        // they had to.
        double clickPeriod = _settings.Timing.PeriodMs;
        int floor = KeyMacro.MinimumDwellMs(clickPeriod, _switcherEquipMs);

        int firstHold = Math.Max(holdA.Value, floor);
        bool raised = firstHold > holdA.Value;

        _macros.Start(new KeyMacro(
            SwitcherName, keys.Value.Keys, keys.Value.Text, firstHold,
            new[] { firstHold, holdB.Value },
            clicksWanted: SwitcherShots,
            equipMs: _switcherEquipMs));

        _switcherStartedAt = _macros.Sent;

        SwitcherStatusText.Text =
            $"On — {SlotABox.Text.Trim()} for {firstHold} ms, then {SlotBBox.Text.Trim()} for {holdB.Value} ms."
            + (raised
                ? $"  Raised from {holdA.Value} to {firstHold} ms: at {1000.0 / clickPeriod:0} clicks a second "
                  + $"that is the shortest hold that still lands two clicks after the {_switcherEquipMs} ms equip. "
                  + "Anything shorter draws the weapon and swaps away before it fires."
                : "")
            + "  Nothing is sent while this window is in front, so switch to the game.";

        _macroTicker.Start();
    }

    /// <remarks>
    /// Never restored as running. A switcher that starts typing into whatever
    /// happens to be focused the moment the app opens is a nasty surprise, and
    /// the app opens long before the game does.
    /// </remarks>
    private void ApplySwitcherToUi(AppSettings s)
    {
        SlotABox.Text = s.SwitcherSlotA;
        SlotBBox.Text = s.SwitcherSlotB;
        SwitcherIntervalBox.Text = s.SwitcherIntervalMs.ToString(CultureInfo.CurrentCulture);
        SwitcherIntervalBBox.Text = s.SwitcherIntervalBMs.ToString(CultureInfo.CurrentCulture);
        SwitcherEquipBox.Text = s.SwitcherEquipMs.ToString(CultureInfo.CurrentCulture);

        SwitcherEnabled.IsChecked = false;
        SwitcherStatusText.Text = "Off.";
    }
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
        // Machine-wide, like the CPU tile beside it.
        double? used = MemoryUsage.GigabytesInUse();
        RamText.Text = used.HasValue ? $"{used.Value:0.0} GB" : "-- GB";

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

        // Reapplied every tick rather than once: priority belongs to the
        // process, so a restarted Roblox comes back at normal.
        if (RobloxPriority?.IsChecked == true) ApplyRobloxPriority();

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

                UpdateOutputPanel(rate);
            }
        }

        _lastRateTimestamp = now;
        _lastClickCount = clicks;
    }

    /// <summary>
    /// Says what is actually being delivered, and whether it matches the ask.
    /// </summary>
    /// <remarks>
    /// The decision lives in <see cref="ClickOutput"/> so it can be tested
    /// without a window; this only paints what it decided.
    /// </remarks>
    private void UpdateOutputPanel(double deliveredCps)
    {
        if (OutputRateText == null) return;

        // The rate that was asked for, not the one HitFix settled on. Timing.Cps
        // is derived from a period HitFix has already raised, so comparing
        // against it compares the clamp with itself — always agreeing, and
        // reporting a "set" figure the sliders never showed. The gap between
        // the ask and the delivery is the entire point of this panel.
        double set = _buildMode ? BuildCps : _settings.Cps;

        // Whether HitFix is the thing holding the rate down, asked by resolving
        // the same request both ways and seeing if the floors moved it. Cheap,
        // and it cannot drift from what the click loop actually runs, because
        // it is the same Resolve.
        double duty = _buildMode ? BuildDuty : _settings.Duty;
        bool hitFixClamping = _settings.HitFix && !_buildMode
            && ClickTimings.Resolve(set, duty, hitFix: true).PeriodMs
             > ClickTimings.Resolve(set, duty, hitFix: false).PeriodMs;

        OutputState state = ClickOutput.Classify(_running, set, deliveredCps, hitFixClamping);

        OutputSetText.Text = $"set {set:0.0} /s";
        OutputRateText.Text = state == OutputState.Idle ? "—" : deliveredCps.ToString("0.0");
        OutputVerdictText.Text = ClickOutput.Verdict(state, set, deliveredCps);

        // The rate itself only turns accent when it is short of the ask, so the
        // colour means "these two numbers disagree" rather than "look here".
        OutputRateText.SetResourceReference(ForegroundProperty,
            state == OutputState.Shortfall ? "Accent" : "TextBright");

        OutputVerdictText.SetResourceReference(ForegroundProperty,
            ClickOutput.IsWarning(state) ? "Accent" : "TextMuted");
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

        // Every macro thread stopped before the window goes. A background
        // thread outliving the UI would keep typing into whatever is focused
        // with nothing left on screen to stop it.
        _macros.Dispose();

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

    /// <summary>
    /// How many click periods the schedule may run behind before the backlog is
    /// dropped. Four is enough to absorb an ordinary Windows scheduling hiccup
    /// without the recovery being visible as a burst.
    /// </summary>
    private const double CatchUpPeriods = 4.0;

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
