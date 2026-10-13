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
    private int _count;          // present events since the last sample
    private volatile int _fps;
    private volatile int _robloxPid;

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

            _sampler = new Timer(Sample, null, 1000, 1000);
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
        // One present == one frame, but DxgKrnl logs both the Start and the Stop of
        // each present, so counting every "Present" event double-counts (reads ~2x
        // the real FPS). Count only the Start of each frame.
        if (data.ProcessID == _robloxPid && _robloxPid != 0
            && data.Opcode == TraceEventOpcode.Start
            && data.EventName.IndexOf("Present", StringComparison.Ordinal) >= 0)
        {
            Interlocked.Increment(ref _count);
        }
    }

    private void Sample(object? _)
    {
        _robloxPid = FindRobloxPid();
        _fps = Interlocked.Exchange(ref _count, 0);
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
