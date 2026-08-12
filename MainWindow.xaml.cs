using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace MyBlinkStyleClicker;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _clickCts;
    private bool _running;
    private bool _holdMode;
    private double _shakeLeft = 8, _shakeRight = 20, _shakeUp = 40, _shakeDown = 8;
    private volatile bool _shakeActive;
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

    private enum RebindTarget { None, Click, Shake }
    private RebindTarget _rebinding = RebindTarget.None;

    // Polled activation needs edge detection, or a held key would re-fire at
    // the timer's rate rather than once per press.
    private readonly DispatcherTimer _hotkeyTimer = new() { Interval = TimeSpan.FromMilliseconds(15) };
    private bool _clickKeyWasDown;
    private bool _shakeKeyWasDown;

    private readonly List<Preset> _presets = new()
    {
        new("Low", 8, 50),
        new("Normal", 10, 67),
        new("High", 16, 75),
        new("Fast", 20, 80),
        new("Max", 100, 100)
    };

    public MainWindow()
    {
        try
        {
            InitializeComponent();

            // Load hotkey settings
            _hotkeySettings.Load();
            ApplyHotkeyToUi();
            // The slider's own ValueChanged fires before its backing field is
            // assigned during parsing, so the label needs setting explicitly.
            UpdateShakeLabel();
            UpdateEngineSettings();

            _statsTimer.Tick += StatsTimer_Tick;
            _statsTimer.Start();
            UpdateStats();

            _hotkeyTimer.Tick += HotkeyTimer_Tick;
            _hotkeyTimer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error in MainWindow constructor: {ex.Message}\n\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }

    private void CpsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CpsValueBox != null)
            CpsValueBox.Text = FormatValue(CpsSlider.Value);

        UpdateEngineSettings();
    }

    private void CdcSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CdcValueBox != null)
            CdcValueBox.Text = FormatValue(CdcSlider.Value);

        UpdateEngineSettings();
    }

    private static string FormatValue(double value) =>
        value.ToString("0.0", CultureInfo.CurrentCulture);

    // Committing writes through the slider, which coerces to its own range. The
    // box is then reformatted from the slider so unparseable or out-of-range
    // text is visibly corrected rather than silently kept.
    private static void CommitValue(System.Windows.Controls.TextBox box, Slider slider)
    {
        if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double parsed))
            slider.Value = Math.Clamp(parsed, slider.Minimum, slider.Maximum);

        box.Text = FormatValue(slider.Value);
    }

    private void CpsValueBox_LostFocus(object sender, RoutedEventArgs e) => CommitValue(CpsValueBox, CpsSlider);
    private void CdcValueBox_LostFocus(object sender, RoutedEventArgs e) => CommitValue(CdcValueBox, CdcSlider);

    private void CpsValueBox_KeyDown(object sender, KeyEventArgs e) => HandleValueBoxKey(e, CpsValueBox, CpsSlider);
    private void CdcValueBox_KeyDown(object sender, KeyEventArgs e) => HandleValueBoxKey(e, CdcValueBox, CdcSlider);

    private static void HandleValueBoxKey(KeyEventArgs e, System.Windows.Controls.TextBox box, Slider slider)
    {
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

        // Emergency stop stays a window event on purpose: polling it would make
        // Escape stop the clicker from inside any application.
        if (e.Key == Key.Escape && !e.IsRepeat) StopClicking();
    }

    /// <summary>
    /// Hotkeys are polled rather than driven by window events. A routed KeyDown
    /// only arrives while this window has focus, which is exactly when a game is
    /// not in front; and mouse side buttons never route here at all. Polling
    /// GetAsyncKeyState reads the same global state for keys and mouse buttons
    /// alike, without hooks or injection.
    /// </summary>
    private void HotkeyTimer_Tick(object? sender, EventArgs e)
    {
        if (_rebinding != RebindTarget.None) return;

        // A bound key being typed into a value box must not also fire the hotkey.
        bool typing = IsActive && Keyboard.FocusedElement is TextBox;

        bool clickDown = IsKeyDown(_hotkeySettings.Hotkey.VirtualKey);
        if (clickDown != _clickKeyWasDown)
        {
            _clickKeyWasDown = clickDown;

            if (!typing)
            {
                if (clickDown)
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
        }

        bool shakeDown = IsKeyDown(_hotkeySettings.ShakeHotkey.VirtualKey);
        if (shakeDown != _shakeKeyWasDown)
        {
            _shakeKeyWasDown = shakeDown;

            // Toggling IsChecked raises Checked/Unchecked, which republishes the
            // engine snapshot — no separate plumbing needed.
            if (shakeDown && !typing && ShakyTracking != null)
                ShakyTracking.IsChecked = ShakyTracking.IsChecked != true;
        }
    }

    // A second click on an armed button cancels rather than re-arming.
    private void Hotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_rebinding == RebindTarget.Click) CancelRebind();
        else BeginRebind(RebindTarget.Click);
    }

    private void ShakeHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_rebinding == RebindTarget.Shake) CancelRebind();
        else BeginRebind(RebindTarget.Shake);
    }

    // The button itself becomes the prompt, so no dialog interrupts the flow.
    private void BeginRebind(RebindTarget target)
    {
        _rebinding = target;

        Button button = target == RebindTarget.Shake ? ShakeHotkeyButton : HotkeyButton;
        button.Content = "Select A Hotkey";
    }

    private void CancelRebind()
    {
        _rebinding = RebindTarget.None;
        ApplyHotkeyToUi();
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

        HotkeyBinding other = target == RebindTarget.Click
            ? _hotkeySettings.ShakeHotkey
            : _hotkeySettings.Hotkey;

        if (binding.VirtualKey == other.VirtualKey)
        {
            CancelRebind();
            MessageBox.Show($"{binding.Name} is already bound to the other action.",
                "Hotkey Unchanged", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (target == RebindTarget.Click) _hotkeySettings.Hotkey = binding;
        else _hotkeySettings.ShakeHotkey = binding;

        _hotkeySettings.Save();

        // Prime the edge detector, or releasing the button that was just bound
        // would read as a fresh transition and fire it immediately.
        _clickKeyWasDown = IsKeyDown(_hotkeySettings.Hotkey.VirtualKey);
        _shakeKeyWasDown = IsKeyDown(_hotkeySettings.ShakeHotkey.VirtualKey);

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

        // Separate from the click loop: shake runs at its own 25-45 ms cadence,
        // which at low CPS is far faster than the click loop wakes up.
        new Thread(() => ShakeLoop(token))
        {
            IsBackground = true,
            Name = "ShakeEngine"
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
        double Cps, double Duty, bool Shaky, ShakeRange Shake,
        bool UltraAccuracy, bool HoldMode, int HotkeyVk);

    private volatile ClickSettings _settings =
        new(10.0, 0.67, false, new ShakeRange(8, 20, 40, 8), false, false, VK_F6);

    // Called on the UI thread whenever anything the engine reads changes.
    private void UpdateEngineSettings()
    {
        if (CpsSlider == null || CdcSlider == null) return;

        // Ping Sync forces the spin-wait on while latency is high, so the only
        // variance left in the click stream is the network's, not ours.
        bool spin = UltraAccuracy?.IsChecked == true
                    || (PingSync?.IsChecked == true && _lastPingMs >= HighPingMs);

        _settings = new ClickSettings(
            CpsSlider.Value,
            Math.Clamp(CdcSlider.Value / 100.0, 0.0, 1.0),
            ShakyTracking?.IsChecked == true,
            new ShakeRange(_shakeLeft, _shakeRight, _shakeUp, _shakeDown),
            spin,
            _holdMode,
            _hotkeySettings.Hotkey.VirtualKey);
    }

    private void EngineSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (PingSync?.IsChecked != true) _lastPingMs = -1;
        UpdatePingLabel();
        UpdateShakeLabel();
        UpdateEngineSettings();
    }

    private void ShakeBox_LostFocus(object sender, RoutedEventArgs e) => CommitShakeBox(sender as TextBox);

    private void ShakeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitShakeBox(sender as TextBox);
            Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Put the committed value back; Escape still reaches the emergency stop.
            WriteShakeBoxes();
        }
    }

    private void CommitShakeBox(TextBox? box)
    {
        if (box == null) return;

        if (box == ShakeLeftBox) _shakeLeft = ParsePixels(box.Text, _shakeLeft);
        else if (box == ShakeRightBox) _shakeRight = ParsePixels(box.Text, _shakeRight);
        else if (box == ShakeUpBox) _shakeUp = ParsePixels(box.Text, _shakeUp);
        else if (box == ShakeDownBox) _shakeDown = ParsePixels(box.Text, _shakeDown);

        WriteShakeBoxes();
        UpdateShakeLabel();
        UpdateEngineSettings();
    }

    /// <summary>
    /// Each box is a distance in its own direction, so the sign carries no
    /// information — "-8" and "8" both mean eight pixels that way. Unparseable
    /// or out-of-range text falls back to the committed value.
    /// </summary>
    /// <remarks>
    /// Rounded on the way in, so the stored value, the text in the box, and the
    /// figure in the status line cannot disagree — "8.7" previously stored 8.7,
    /// displayed 9, and read 8 in the label.
    /// </remarks>
    private static double ParsePixels(string text, double current) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double parsed)
            ? Math.Round(Math.Clamp(Math.Abs(parsed), 0.0, MaxShakePixels))
            : current;

    private void WriteShakeBoxes()
    {
        if (ShakeLeftBox == null) return;

        ShakeLeftBox.Text = Format(_shakeLeft);
        ShakeRightBox.Text = Format(_shakeRight);
        ShakeUpBox.Text = Format(_shakeUp);
        ShakeDownBox.Text = Format(_shakeDown);

        static string Format(double v) => ((int)Math.Round(v)).ToString(CultureInfo.CurrentCulture);
    }

    private void UpdateShakeLabel()
    {
        if (ShakeStatusText == null) return;

        if (_shakeLeft <= 0 && _shakeRight <= 0 && _shakeUp <= 0 && _shakeDown <= 0)
        {
            ShakeStatusText.Text = "All four directions are zero — no camera movement";
            return;
        }

        string range = $"L{(int)_shakeLeft} R{(int)_shakeRight} U{(int)_shakeUp} D{(int)_shakeDown} px " +
                       $"every {ShakeMinIntervalMs}-{ShakeMaxIntervalMs} ms";

        if (ShakyTracking?.IsChecked != true)
        {
            ShakeStatusText.Text = $"{range}, only while Roblox has the mouse locked";
            return;
        }

        // The shake thread is spawned by StartClicking, so armed-but-stopped is
        // a real state and saying "waiting for first person" there would lie.
        if (!_running)
        {
            ShakeStatusText.Text = $"{range}, starts with the clicker";
            return;
        }

        ShakeStatusText.Text = _shakeActive
            ? $"Active — {range}"
            : $"Waiting for Roblox first person — {range}";
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

        try
        {
            long freq = Stopwatch.Frequency;
            long deadline = Stopwatch.GetTimestamp();

            while (!token.IsCancellationRequested)
            {
                ClickSettings s = _settings;

                if (s.HoldMode && !IsKeyDown(s.HotkeyVk))
                {
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
                    Thread.Sleep(50);
                    deadline = Stopwatch.GetTimestamp();
                    continue;
                }

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
            if (buttonDown) SendLeftUp();
            if (raisedTimer) TimeEndPeriod(TimerResolutionMs);
        }
    }

    private static int NextOffset(double min, double max) =>
        (int)Math.Round(min + Random.Shared.NextDouble() * (max - min));

    /// <summary>
    /// True when Roblox owns the foreground window and the system cursor is
    /// hidden, which is how Roblox presents a camera-locked mouse.
    /// </summary>
    /// <remarks>
    /// This is the closest an external process can get to "is the player in
    /// first person" without reading Roblox's memory. It is a superset: third
    /// person with shift-lock, and holding right-mouse to rotate, both lock the
    /// cursor the same way. Distinguishing them would mean reading game state
    /// out of a protected process, which this app does not do.
    /// </remarks>
    private bool IsRobloxMouseLocked()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return false;

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

        if (!_cachedForegroundIsRoblox) return false;

        CURSORINFO info = default;
        info.cbSize = Marshal.SizeOf<CURSORINFO>();
        if (!GetCursorInfo(ref info)) return false;

        return (info.flags & CURSOR_SHOWING) == 0;
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
                bool eligible = s.Shaky && !range.IsZero && IsRobloxMouseLocked();
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

                Thread.Sleep(Random.Shared.Next(ShakeMinIntervalMs, ShakeMaxIntervalMs + 1));
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
        ShakeHotkeyButton.Content = _hotkeySettings.ShakeHotkey.Name;
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

    private void ToggleMode_Click(object sender, RoutedEventArgs e)
    {
        _holdMode = false;
        ToggleModeButton.Background = (System.Windows.Media.Brush)FindResource("Accent");
        HoldModeButton.ClearValue(BackgroundProperty);
        UpdateEngineSettings();
    }

    private void HoldMode_Click(object sender, RoutedEventArgs e)
    {
        _holdMode = true;
        HoldModeButton.Background = (System.Windows.Media.Brush)FindResource("Accent");
        ToggleModeButton.ClearValue(BackgroundProperty);
        UpdateEngineSettings();
    }

    private void PresetLow_Click(object sender, RoutedEventArgs e) => ApplyPreset(_presets[0]);
    private void PresetNormal_Click(object sender, RoutedEventArgs e) => ApplyPreset(_presets[1]);
    private void PresetHigh_Click(object sender, RoutedEventArgs e) => ApplyPreset(_presets[2]);
    private void PresetFast_Click(object sender, RoutedEventArgs e) => ApplyPreset(_presets[3]);
    private void PresetMax_Click(object sender, RoutedEventArgs e) => ApplyPreset(_presets[4]);
    private void Preset100_Click(object sender, RoutedEventArgs e) => ApplyPreset(_presets[4]);

    private void ApplyPreset(Preset preset)
    {
        CpsSlider.Value = preset.Cps;
        CdcSlider.Value = preset.Cdc;
    }



    // Navigation placeholders. These keep the project simple while we build
    // the remaining pages from your screenshots.
    private void NavClicker_Click(object sender, RoutedEventArgs e) { }
    private void NavPresets_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Presets page is next.");
    private void NavResolution_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Resolution page is next.");
    private void NavTweaks_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Tweaks page is next.");
    private void NavOptimizations_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Optimizations page is next.");
    private void NavHistory_Click(object sender, RoutedEventArgs e) => MessageBox.Show("History page is next.");
    private void NavTheme_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Theme page is next.");
    private void NavSettings_Click(object sender, RoutedEventArgs e)
    {
        // Both hotkeys are rebound from their own buttons now, so this is just a
        // page placeholder like the rest of the nav.
        MessageBox.Show("Settings page is next.");
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
        _statsTimer.Stop();
        _hotkeyTimer.Stop();
    }

    // Below this the interval exceeds a minute and the app is effectively idle.
    private const double MinimumCps = 0.05;

    private const uint TimerResolutionMs = 1;

    // ICMP round trip toward Roblox's front end. This is not the in-game ping
    // readout — that is measured application-side inside a process we do not
    // touch — but it tracks the same connection getting worse.
    private const string PingHost = "www.roblox.com";
    private const int HighPingMs = 60;

    private const int ShakeMinIntervalMs = 20;
    private const int ShakeMaxIntervalMs = 40;
    private const double MaxShakePixels = 200.0;
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

    private record Preset(string Name, double Cps, double Cdc);
}
