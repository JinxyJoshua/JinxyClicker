using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace JinxyClicker;

/// <summary>
/// A global low-level keyboard hook that SOCD-cleans the A/D and W/S pairs: it
/// swallows the real presses and injects the single cleaned direction the game
/// should receive (see <see cref="Socd"/> for the decision).
/// </summary>
/// <remarks>
/// Runs on its own thread with a message loop, which is what a WH_KEYBOARD_LL hook
/// needs to be called. Injected key events carry the OS "injected" flag, so the
/// hook ignores its own output and can't feed back on itself. On stop it releases
/// anything it is holding down, so toggling off never leaves a key stuck.
/// </remarks>
public sealed class SocdHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private const uint WM_QUIT = 0x0012;
    private const uint LLKHF_INJECTED = 0x10;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const int VK_A = 0x41, VK_D = 0x44, VK_W = 0x57, VK_S = 0x53;

    public SocdMode Mode { get; set; } = SocdMode.Strafe;

    /// <summary>How fast strafe mode alternates sides, in milliseconds.</summary>
    public int StrafeIntervalMs { get; set; } = 40;

    public const int MinStrafeMs = 10, MaxStrafeMs = 500;

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;   // kept alive so the GC can't collect the callback
    private Thread? _thread;
    private uint _threadId;
    private System.Threading.Timer? _strafeTimer;
    private readonly object _gate = new();

    private sealed class Pair
    {
        public int NegVk, PosVk;
        public bool NegDown, PosDown, LastWasPos;   // physical + which was last pressed
        public bool NegOut, PosOut;                 // what we currently hold down for the game
    }
    private readonly Pair _horizontal = new() { NegVk = VK_A, PosVk = VK_D };
    private readonly Pair _vertical = new() { NegVk = VK_S, PosVk = VK_W };

    public bool IsRunning => _thread is { IsAlive: true };

    /// <summary>
    /// Sets the two opposing pairs. A key of 0 (unset) disables that pair. Horizontal
    /// is left/right, vertical is up/down.
    /// </summary>
    public void Configure(int leftVk, int rightVk, int upVk, int downVk)
    {
        lock (_gate)
        {
            _horizontal.NegVk = leftVk;
            _horizontal.PosVk = rightVk;
            _vertical.NegVk = downVk;
            _vertical.PosVk = upVk;
        }
    }

    public void Start()
    {
        if (_thread != null) return;
        int period = Math.Clamp(StrafeIntervalMs, MinStrafeMs, MaxStrafeMs);
        _strafeTimer = new System.Threading.Timer(StrafeTick, null, period, period);
        _thread = new Thread(HookThread) { IsBackground = true, Name = "SocdHook" };
        _thread.Start();
    }

    public void Stop()
    {
        Thread? thread = _thread;
        if (thread == null) return;

        _strafeTimer?.Dispose();
        _strafeTimer = null;

        ReleaseAll();   // let go of anything held before the hook goes away
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        thread.Join(1000);
        _thread = null;
        _threadId = 0;
    }

    /// <summary>Updates how fast strafe mode alternates, live.</summary>
    public void SetStrafeInterval(int ms)
    {
        StrafeIntervalMs = Math.Clamp(ms, MinStrafeMs, MaxStrafeMs);
        _strafeTimer?.Change(StrafeIntervalMs, StrafeIntervalMs);
    }

    // Every tick, flip the output of any pair that is being held both-down in
    // strafe mode — that is the side-to-side strafe.
    private void StrafeTick(object? _)
    {
        lock (_gate)
        {
            if (Mode != SocdMode.Strafe) return;
            foreach (Pair p in new[] { _horizontal, _vertical })
                if (p.NegVk != 0 && p.PosVk != 0 && p.NegDown && p.PosDown)
                    SetOutput(p, p.PosOut, !p.PosOut);   // toggle to the other side
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
                    Handle(pair, isPos, isDown);
                    return (IntPtr)1;   // swallow the real key; the game gets the cleaned injection
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    // Only pairs with both keys set are cleaned; a pair with an unset key is off.
    private Pair? PairFor(int vk, out bool isPos)
    {
        isPos = false;
        if (vk == 0) return null;

        if (_horizontal.NegVk != 0 && _horizontal.PosVk != 0)
        {
            if (vk == _horizontal.NegVk) return _horizontal;
            if (vk == _horizontal.PosVk) { isPos = true; return _horizontal; }
        }
        if (_vertical.NegVk != 0 && _vertical.PosVk != 0)
        {
            if (vk == _vertical.NegVk) return _vertical;
            if (vk == _vertical.PosVk) { isPos = true; return _vertical; }
        }
        return null;
    }

    private void Handle(Pair p, bool isPos, bool isDown)
    {
        lock (_gate)
        {
            if (isPos) p.PosDown = isDown; else p.NegDown = isDown;
            if (isDown) p.LastWasPos = isPos;

            bool both = p.NegDown && p.PosDown;

            if (both && Mode == SocdMode.Strafe)
            {
                // Hand over to the strafe timer; just make sure one side is pressed
                // so the first toggle has somewhere to go.
                if (!p.NegOut && !p.PosOut) SetOutput(p, false, true);
                return;
            }

            (bool neg, bool pos) = both
                ? Socd.Resolve(p.NegDown, p.PosDown, p.LastWasPos, Mode)
                : (p.NegDown, p.PosDown);   // one or none held passes straight through
            SetOutput(p, neg, pos);
        }
    }

    // Caller holds _gate. Injects only the changes from the current output.
    private void SetOutput(Pair p, bool neg, bool pos)
    {
        if (neg != p.NegOut) { Inject(p.NegVk, neg); p.NegOut = neg; }
        if (pos != p.PosOut) { Inject(p.PosVk, pos); p.PosOut = pos; }
    }

    private void ReleaseAll()
    {
        lock (_gate)
        {
            foreach (Pair p in new[] { _horizontal, _vertical })
            {
                if (p.NegOut) { Inject(p.NegVk, false); p.NegOut = false; }
                if (p.PosOut) { Inject(p.PosVk, false); p.PosOut = false; }
                p.NegDown = p.PosDown = false;
            }
        }
    }

    // Injected as a SCAN CODE, not a virtual key: games (Roblox included) read
    // movement through raw/scan-code input, and a vk-only SendInput is ignored —
    // which looked like the character just stopping when both keys were held.
    private static void Inject(int vk, bool down)
    {
        ushort scan = (ushort)MapVirtualKey((uint)vk, 0 /* MAPVK_VK_TO_VSC */);
        var input = new INPUT { type = 1 /* INPUT_KEYBOARD */ };
        input.u.ki.wVk = 0;
        input.u.ki.wScan = scan;
        input.u.ki.dwFlags = KEYEVENTF_SCANCODE | (down ? 0u : KEYEVENTF_KEYUP);
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
}
