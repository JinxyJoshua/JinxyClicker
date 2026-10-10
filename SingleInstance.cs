using System;
using System.IO;
using System.IO.Pipes;
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
    public static bool IsFirstInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        return createdNew;
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
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.None);
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
}
