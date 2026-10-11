using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace JinxyClicker;

/// <summary>
/// A global low-level keyboard hook that SOCD-cleans one opposing key pair
/// (A vs D by default): while both are held it releases them so they don't fight,
/// and a single key passes through as the real key.
/// </summary>
/// <remarks>
/// Two design points, both learned the hard way:
///   1. It never fakes a key you are holding. Your *winning* key stays your real
///      physical key — the hook just stops hiding it and lets it reach the game.
///      Only the *losing* key is hidden (its events swallowed) and released with
///      an injected key-UP. Releasing by injection registers where spoofing a
///      held-down key did not.
///   2. All injection happens on a background thread, never inside the hook
///      callback — the auto-switcher proves injection from a thread registers in
///      Roblox, while injection from the callback does not.
/// The hook only tracks which side to hide and swallows that side's events; a
/// timer reconciles the game's view by injecting the releases. When the winner is
/// let go, the loser reappears through its own hardware auto-repeat (and an
/// injected down, as a best-effort for instant snap-back).
/// </remarks>
public sealed class SocdHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private const uint WM_QUIT = 0x0012;
    private const uint LLKHF_INJECTED = 0x10;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const int VK_A = 0x41, VK_D = 0x44;

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;   // kept alive so the GC can't collect the callback
    private Thread? _thread;
    private uint _threadId;
    private System.Threading.Timer? _timer;
    private readonly object _gate = new();

    private const int TickMs = 4;   // how often the background thread reconciles / strafes

    private sealed class Pair
    {
        public int NegVk, PosVk;
        public bool NegDown, PosDown, LastWasPos;   // physical state + which was pressed last
        public bool SupNeg, SupPos;                 // which side we want hidden from the game (set in hook)
        public bool UpNeg, UpPos;                   // whether we have injected an UP to release that side
    }
    // One opposing pair, e.g. A vs D.
    private readonly Pair _pair = new() { NegVk = VK_A, PosVk = VK_D };

    public bool IsRunning => _thread is { IsAlive: true };

    /// <summary>Sets the two opposing keys. A key of 0 (unset) turns SOCD off.</summary>
    public void Configure(int key1Vk, int key2Vk)
    {
        lock (_gate)
        {
            // RefreshLicense calls this every minute; when the keys are unchanged
            // there is nothing to set up, and resetting the state here would
            // briefly drop tracking of keys you happen to be holding at that
            // instant (both would read as up until the next press).
            if (key1Vk == _pair.NegVk && key2Vk == _pair.PosVk) return;

            RestoreHidden(_pair);
            _pair.NegVk = key1Vk;
            _pair.PosVk = key2Vk;
            _pair.NegDown = _pair.PosDown = false;
            _pair.SupNeg = _pair.SupPos = false;
        }
    }

    public void Start()
    {
        if (_thread != null) return;
        _timer = new System.Threading.Timer(Tick, null, TickMs, TickMs);
        _thread = new Thread(HookThread) { IsBackground = true, Name = "SocdHook" };
        _thread.Start();
    }

    public void Stop()
    {
        Thread? thread = _thread;
        if (thread == null) return;

        _timer?.Dispose();
        _timer = null;

        ReleaseAll();   // stop hiding anything before the hook goes away
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        thread.Join(1000);
        _thread = null;
        _threadId = 0;
    }

    // Background thread: reconciles the game's view with the hidden state by
    // injecting the releases / re-presses. This is the ONLY place keys are injected.
    private void Tick(object? _)
    {
        lock (_gate)
        {
            if (_pair.NegVk == 0 || _pair.PosVk == 0) return;
            // If Roblox isn't the active window, stop suppressing so any key we
            // were hiding is let go — keys must work normally in every other app.
            if (!IsRobloxForeground()) { _pair.SupNeg = _pair.SupPos = false; }
            Reconcile(_pair);
        }
    }

    private void HookThread()
    {
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);   // force the thread message queue to exist

        _proc = HookCallback;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);

        while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_hookId != IntPtr.Zero) UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            KBDLLHOOKSTRUCT info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            bool injected = (info.flags & LLKHF_INJECTED) != 0;
            if (!injected)
            {
                int message = (int)wParam;
                bool isDown = message is WM_KEYDOWN or WM_SYSKEYDOWN;
                bool isUp = message is WM_KEYUP or WM_SYSKEYUP;

                Pair? pair = PairFor((int)info.vkCode, out bool isPos);
                if (pair != null && (isDown || isUp))
                {
                    // Only the hidden (losing) key is swallowed. The winning/active
                    // key falls through and reaches the game as the real key.
                    if (Handle(pair, isPos, isDown)) return (IntPtr)1;
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    // Only pairs with both keys set are cleaned; a pair with an unset key is off.
    private Pair? PairFor(int vk, out bool isPos)
    {
        isPos = false;
        if (vk == 0 || _pair.NegVk == 0 || _pair.PosVk == 0) return null;

        if (vk == _pair.NegVk) return _pair;
        if (vk == _pair.PosVk) { isPos = true; return _pair; }
        return null;
    }

    // Runs in the hook callback — fast, no injection. Records physical state,
    // decides which side to hide, and returns whether THIS event is the hidden
    // side (and so should be swallowed).
    private bool Handle(Pair p, bool isPos, bool isDown)
    {
        lock (_gate)
        {
            if (isDown)
            {
                bool wasDown = isPos ? p.PosDown : p.NegDown;
                if (isPos) p.PosDown = true; else p.NegDown = true;
                if (!wasDown) p.LastWasPos = isPos;   // a fresh press wins; ignore auto-repeat
            }
            else
            {
                if (isPos) p.PosDown = false; else p.NegDown = false;
            }

            Resolve(p);

            // SOCD only cleans while Roblox is the active window. In any other app
            // (or when nothing is focused) every key passes straight through, so it
            // can never swallow A/D — or any rebound pair — while you type elsewhere.
            if (!IsRobloxForeground()) { p.SupNeg = p.SupPos = false; }

            return isPos ? p.SupPos : p.SupNeg;
        }
    }

    // Cached foreground check: resolving a pid to a process name is comparatively
    // expensive and this runs on every key event and every tick, so the answer is
    // kept until the foreground pid changes (same approach as MainWindow's shake
    // gate). Roblox chat is the same process, so typing in chat is unaffected.
    private uint _fgPid;
    private bool _fgIsRoblox;
    private bool IsRobloxForeground()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return false;

        if (pid != _fgPid)
        {
            _fgPid = pid;
            _fgIsRoblox = false;
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById((int)pid);
                _fgIsRoblox = p.ProcessName.Contains("roblox", StringComparison.OrdinalIgnoreCase);
            }
            catch { /* process exited, or not ours to inspect */ }
        }
        return _fgIsRoblox;
    }

    // Sets which side(s) to hide. No injection — the timer does that.
    //
    // While both keys are held, both are hidden (released); a single key passes
    // through as the real key. This is the one resolution Roblox actually honours
    // from an external app — the "release a key you're still holding" that the
    // other modes need is rejected by the game, so this is what the feature does.
    private void Resolve(Pair p)
    {
        bool both = p.NegDown && p.PosDown;
        p.SupNeg = both;
        p.SupPos = both;
    }

    // Caller holds _gate. Brings the game's view in line with the hidden state.
    // Hiding a key => inject an UP so the game releases it. Un-hiding a still-held
    // key => inject a DOWN to re-press it (its hardware auto-repeat also brings it
    // back now that it is no longer swallowed, so this is just for an instant snap).
    private void Reconcile(Pair p)
    {
        if (p.SupNeg && !p.UpNeg) { Inject(p.NegVk, false); p.UpNeg = true; }
        else if (!p.SupNeg && p.UpNeg) { if (p.NegDown) Inject(p.NegVk, true); p.UpNeg = false; }

        if (p.SupPos && !p.UpPos) { Inject(p.PosVk, false); p.UpPos = true; }
        else if (!p.SupPos && p.UpPos) { if (p.PosDown) Inject(p.PosVk, true); p.UpPos = false; }
    }

    // Re-press anything we were hiding that is still physically held, so turning
    // SOCD off or reconfiguring never leaves you unable to move.
    private void RestoreHidden(Pair p)
    {
        if (p.UpNeg && p.NegDown) Inject(p.NegVk, true);
        if (p.UpPos && p.PosDown) Inject(p.PosVk, true);
        p.SupNeg = p.SupPos = false;
        p.UpNeg = p.UpPos = false;
    }

    private void ReleaseAll()
    {
        lock (_gate)
        {
            RestoreHidden(_pair);
            _pair.NegDown = _pair.PosDown = false;
        }
    }

    // Sends the virtual key AND its scan code together with no extra flags —
    // exactly how the auto-switcher (KeyMacro) does it, which is proven to move
    // the character in Roblox.
    private static void Inject(int vk, bool down)
    {
        ushort scan = (ushort)MapVirtualKey((uint)vk, 0 /* MAPVK_VK_TO_VSC */);
        var input = new INPUT { type = 1 /* INPUT_KEYBOARD */ };
        input.u.ki.wVk = (ushort)vk;
        input.u.ki.wScan = scan;
        input.u.ki.dwFlags = down ? 0u : KEYEVENTF_KEYUP;
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public void Dispose() => Stop();

    // ---- P/Invoke ----

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int ptX, ptY; }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion u; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
