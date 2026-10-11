using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace JinxyClicker;

/// <summary>
/// Keeps the app to one running copy. A second launch (for example a double-click
/// of the "Jinxy Play" shortcut while the app is already open) hands its intent to
/// the running copy over a named pipe and exits, instead of opening a second window.
/// </summary>
public static class SingleInstance
{
    private const string MutexName = "JinxyClicker.SingleInstance.Mutex.v1";
    private const string PipeName = "JinxyClicker.SingleInstance.Pipe.v1";

    // Held for the life of the first instance so the name stays taken.
    private static Mutex? _mutex;

    /// <summary>True for the first copy; false when another is already running.</summary>
    /// <remarks>
    /// Not owned, just named: we never wait on it, we only ask whether the name
    /// already exists. If a copy already running as administrator created it, this
    /// process may be denied full access — that denial itself means a copy exists,
    /// so it is treated as "not first" rather than crashing out of startup.
    /// </remarks>
    public static bool IsFirstInstance()
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: false, MutexName, out bool createdNew);
            return createdNew;
        }
        catch (UnauthorizedAccessException)
        {
            return false;   // an elevated first copy owns the name
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Frees the name so a deliberate relaunch (e.g. restart-as-administrator) can
    /// become the new first instance instead of bouncing off this one.
    /// </summary>
    public static void Release()
    {
        try { _mutex?.Dispose(); } catch { }
        _mutex = null;
    }

    /// <summary>Sends a one-word intent ("play" or "activate") to the running copy.</summary>
    public static void Signal(string message)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(message);
        }
        catch
        {
            // If the running copy can't be reached, there is nothing useful to do;
            // the second launch simply exits.
        }
    }

    /// <summary>Listens for later launches and hands each message to the app.</summary>
    public static void StartServer(Action<string> onMessage)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using NamedPipeServerStream server = CreateServer();
                    server.WaitForConnection();

                    using var reader = new StreamReader(server);
                    string? message = reader.ReadLine();
                    if (!string.IsNullOrWhiteSpace(message)) onMessage(message.Trim());
                }
                catch
                {
                    Thread.Sleep(200); // a bad connection must not kill the listener
                }
            }
        })
        {
            IsBackground = true,
            Name = "SingleInstancePipe"
        };
        thread.Start();
    }

    // Grant authenticated users read/write so a non-elevated launch can still
    // signal a copy that is running as administrator (and vice versa).
    private static NamedPipeServerStream CreateServer()
    {
        try
        {
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                AccessControlType.Allow));

            return NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.In, 1,
                PipeTransmissionMode.Byte, PipeOptions.None, 0, 0, security);
        }
        catch
        {
            // If the ACL path is unavailable, fall back to a default pipe — same-
            // elevation signalling still works.
            return new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                PipeTransmissionMode.Byte, PipeOptions.None);
        }
    }
}
