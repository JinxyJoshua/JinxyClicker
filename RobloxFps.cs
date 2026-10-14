using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace JinxyClicker;

/// <summary>
/// Measures Roblox's frame rate from outside the game by counting its present
/// (frame) events on a real-time ETW session — the same technique the Xbox Game
/// Bar's FPS counter uses. It needs an elevated process: creating the session
/// throws without admin, in which case <see cref="Available"/> stays false and the
/// overlay simply shows no number.
/// </summary>
public sealed class RobloxFps : IDisposable
{
    // Microsoft-Windows-DxgKrnl: the GPU scheduler, which logs a present per frame.
    private const string DxgKrnlProvider = "Microsoft-Windows-DxgKrnl";

    private TraceEventSession? _session;
    private Thread? _pump;
    private Timer? _sampler;
    private readonly object _lock = new();
    private readonly System.Collections.Generic.Queue<DateTime> _frames = new();   // recent frame timestamps
    private DateTime _lastStamp;
    private volatile int _fps;
    private volatile int _robloxPid;

    // Average frames over this many seconds. We window on each frame's own ETW
    // timestamp, not on when the event was delivered — ETW flushes events in bursts,
    // so delivery time is jumpy while the timestamps are accurate.
    private const double WindowSeconds = 2.0;

    /// <summary>True once an ETW session is running (requires admin).</summary>
    public bool Available { get; private set; }

    /// <summary>Why Start() failed, for diagnostics.</summary>
    public string? LastError { get; private set; }

    /// <summary>The most recent frames-per-second sample, or 0 if unknown.</summary>
    public int Current => _robloxPid == 0 ? 0 : _fps;

    public void Start()
    {
        if (_session != null) return;
        try
        {
            _robloxPid = FindRobloxPid();
            TryStopStaleSession();   // clear a session a crashed run may have left
            _session = new TraceEventSession("JinxyFpsSession") { StopOnDispose = true };   // throws if not elevated
            _session.EnableProvider(DxgKrnlProvider, TraceEventLevel.Verbose, 0x1 /* Base: present events */);
            _session.Source.Dynamic.All += OnEvent;

            _pump = new Thread(PumpEvents) { IsBackground = true, Name = "JinxyFps" };
            _pump.Start();

            _sampler = new Timer(Sample, null, 500, 500);
            Available = true;
        }
        catch (Exception ex)
        {
            LastError = ex.GetType().Name + ": " + ex.Message;
            Available = false;
            Stop();
        }
    }

    private void PumpEvents()
    {
        try { _session?.Source.Process(); }
        catch { /* session stopped */ }
    }

    private void OnEvent(TraceEvent data)
    {
        // The DxgKrnl "Present" event fires once per Present() call — one per frame,
        // in both windowed and fullscreen. (Matching every event whose name merely
        // contains "Present" also catches present-history and detailed-present events,
        // which over-counts; and those history events don't fire in fullscreen, so a
        // history-based count reads zero there.)
        if (data.ProcessID == _robloxPid && _robloxPid != 0
            && data.EventName == "Present")
        {
            lock (_lock)
            {
                _frames.Enqueue(data.TimeStamp);
                if (data.TimeStamp > _lastStamp) _lastStamp = data.TimeStamp;
            }
        }
    }

    private void Sample(object? _)
    {
        int prevPid = _robloxPid;
        _robloxPid = FindRobloxPid();

        lock (_lock)
        {
            if (_robloxPid == 0) { _frames.Clear(); _fps = 0; return; }
            if (_robloxPid != prevPid) _frames.Clear();   // new game — don't blend across

            // No frames for a couple of seconds → Roblox is minimized/stalled.
            if (_frames.Count == 0 || (DateTime.Now - _lastStamp).TotalSeconds > 2.0)
            {
                _frames.Clear();
                _fps = 0;
                return;
            }

            DateTime cutoff = _lastStamp - TimeSpan.FromSeconds(WindowSeconds);
            while (_frames.Count > 1 && _frames.Peek() < cutoff) _frames.Dequeue();

            int c = _frames.Count;
            double span = (_lastStamp - _frames.Peek()).TotalSeconds;
            // c timestamps span (c-1) frame intervals over `span` seconds.
            _fps = (c >= 2 && span > 0.3) ? (int)Math.Round((c - 1) / span) : 0;
        }
    }

    // A real-time ETW session outlives a process that doesn't dispose it, and a
    // stale one with our name blocks a new one. Attach to any leftover and stop it.
    private static void TryStopStaleSession()
    {
        try { using var stale = new TraceEventSession("JinxyFpsSession", TraceEventSessionOptions.Attach); stale.Stop(); }
        catch { /* nothing to attach to — fine */ }
    }

    private static int FindRobloxPid()
    {
        try
        {
            Process[] ps = Process.GetProcessesByName("RobloxPlayerBeta");
            return ps.Length > 0 ? ps[0].Id : 0;
        }
        catch { return 0; }
    }

    public void Stop()
    {
        _sampler?.Dispose();
        _sampler = null;
        try { _session?.Stop(); } catch { }
        try { _session?.Dispose(); } catch { }
        _session = null;
        _pump = null;
        Available = false;
        _fps = 0;
    }

    public void Dispose() => Stop();
}
