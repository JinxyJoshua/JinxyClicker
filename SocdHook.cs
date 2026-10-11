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
    private const int VK_A = 0x41, VK_D = 0x44, VK_W = 0x57, VK_S = 0x53;

    public SocdMode Mode { get; set; } = SocdMode.LastInputWins;

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;   // kept alive so the GC can't collect the callback
    private Thread? _thread;
    private uint _threadId;
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

    public void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(HookThread) { IsBackground = true, Name = "SocdHook" };
        _thread.Start();
    }

    public void Stop()
    {
        Thread? thread = _thread;
        if (thread == null) return;

        ReleaseAll();   // let go of anything held before the hook goes away
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        thread.Join(1000);
        _thread = null;
        _threadId = 0;
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

    private Pair? PairFor(int vk, out bool isPos)
    {
        isPos = false;
        if (vk == _horizontal.NegVk) return _horizontal;
        if (vk == _horizontal.PosVk) { isPos = true; return _horizontal; }
        if (vk == _vertical.NegVk) return _vertical;
        if (vk == _vertical.PosVk) { isPos = true; return _vertical; }
        return null;
    }

    private void Handle(Pair p, bool isPos, bool isDown)
    {
        lock (_gate)
        {
            if (isPos) p.PosDown = isDown; else p.NegDown = isDown;
            if (isDown) p.LastWasPos = isPos;

            (bool neg, bool pos) = Socd.Resolve(p.NegDown, p.PosDown, p.LastWasPos, Mode);

            if (neg != p.NegOut) { Inject(p.NegVk, neg); p.NegOut = neg; }
            if (pos != p.PosOut) { Inject(p.PosVk, pos); p.PosOut = pos; }
        }
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

    private static void Inject(int vk, bool down)
    {
        var input = new INPUT { type = 1 /* INPUT_KEYBOARD */ };
        input.u.ki.wVk = (ushort)vk;
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
