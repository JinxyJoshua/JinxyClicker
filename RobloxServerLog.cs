using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace JinxyClicker;

/// <summary>One game server's public address, as the Roblox log records it.</summary>
public readonly record struct ServerEndpoint(string Ip, int Port);

/// <summary>
/// Reads the Roblox client's own logs to find which game server the player is on.
/// </summary>
/// <remarks>
/// Roblox writes a line the moment it connects to a server:
///   <c>[FLog::Network] serverId: 128.116.48.33|64508</c>
/// and, a beat before it,
///   <c>[FLog::Network] UDMUX Address = 128.116.48.33, Port = 64508 | RCC Server Address = 10.x ...</c>
///
/// The address on both is the UDMUX edge the server is reached through, not the
/// machine itself — Roblox puts a relay in front of every server, so the real
/// host is never exposed. Geolocating the relay gives the server's <em>region</em>,
/// which is what this is for; it is not, and cannot be, the exact host. The RCC
/// address after the pipe on the UDMUX line is a private 10.x one and is ignored,
/// because it means nothing outside Roblox's own network.
///
/// Nothing here touches the game process — it only reads files Roblox has already
/// written to disk. Turning the address into a place is <see cref="IpRegionLookup"/>.
/// </remarks>
public static class RobloxServerLog
{
    public static string DefaultLogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "logs");

    // The clean source: one token, public IP then port. Preferred when present.
    private static readonly Regex ServerIdLine =
        new(@"serverId:\s*(\d{1,3}(?:\.\d{1,3}){3})\|(\d{1,5})", RegexOptions.Compiled);

    // The same address, written just before serverId. The fallback for the brief
    // window after a connection starts but before serverId is logged. The private
    // RCC address after the pipe is deliberately not captured.
    private static readonly Regex UdmuxLine = new(
        @"UDMUX Address\s*=\s*(\d{1,3}(?:\.\d{1,3}){3}),\s*Port\s*=\s*(\d{1,5})",
        RegexOptions.Compiled);

    /// <summary>
    /// The server the log most recently describes, or null if the player is not
    /// on one yet.
    /// </summary>
    /// <remarks>
    /// A session hops servers, and every hop writes a fresh line, so the last
    /// address in the file is the one in effect and earlier ones are history.
    /// serverId wins over UDMUX wherever both describe the same connection,
    /// because the two sit a few lines apart and serverId is the tidier line;
    /// UDMUX is only taken when it belongs to a connection newer than any
    /// serverId line, which is exactly the before-serverId-is-written case.
    /// </remarks>
    public static ServerEndpoint? FindCurrentServer(string? logText)
    {
        if (string.IsNullOrEmpty(logText)) return null;

        ServerEndpoint? latest = null;
        int latestPos = -1;

        foreach (Match m in ServerIdLine.Matches(logText))
            if (m.Index > latestPos && TryEndpoint(m, out ServerEndpoint ep))
            {
                latest = ep;
                latestPos = m.Index;
            }

        foreach (Match m in UdmuxLine.Matches(logText))
            if (m.Index > latestPos && TryEndpoint(m, out ServerEndpoint ep))
            {
                latest = ep;
                latestPos = m.Index;
            }

        return latest;
    }

    private static bool TryEndpoint(Match m, out ServerEndpoint endpoint)
    {
        endpoint = default;

        if (!IPAddress.TryParse(m.Groups[1].Value, out IPAddress? ip)) return false;
        if (!IsPublic(ip)) return false;
        if (!int.TryParse(m.Groups[2].Value, out int port) || port is < 1 or > 65535) return false;

        endpoint = new ServerEndpoint(m.Groups[1].Value, port);
        return true;
    }

    /// <summary>
    /// Rejects the addresses that can never be a game server — the private and
    /// link-local ranges — so the RCC 10.x address and anything loopback is never
    /// mistaken for one.
    /// </summary>
    public static bool IsPublic(IPAddress ip)
    {
        byte[] b = ip.GetAddressBytes();
        if (b.Length != 4) return false; // Roblox logs IPv4 servers only.

        return b[0] switch
        {
            0 or 10 or 127 => false,                        // this-network, private, loopback
            100 when b[1] >= 64 && b[1] <= 127 => false,    // carrier-grade NAT
            169 when b[1] == 254 => false,                  // link-local
            172 when b[1] >= 16 && b[1] <= 31 => false,     // private
            192 when b[1] == 168 => false,                  // private
            _ => true
        };
    }

    /// <summary>The newest real player log, or null if none can be found.</summary>
    /// <remarks>
    /// The CrashHandler logs share the folder and the Player tag but carry no
    /// connection lines, so they are passed over.
    /// </remarks>
    public static string? NewestPlayerLog(string? directory = null)
    {
        directory ??= DefaultLogDirectory;

        if (!Directory.Exists(directory)) return null;

        try
        {
            return new DirectoryInfo(directory)
                .GetFiles("*_Player_*.log")
                .Where(f => !f.Name.Contains("CrashHandler", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => f.FullName)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a log Roblox still has open for writing.
    /// </summary>
    /// <remarks>
    /// The live log is held open by the client, so it has to be opened shared for
    /// both read and write or the read throws. Returns null rather than throwing
    /// when the file is momentarily unavailable, because a poll that fails once is
    /// not worth interrupting.
    /// </remarks>
    public static string? ReadShared(string path)
    {
        try
        {
            using var fs = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            return reader.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The current server from the newest log, in one call.</summary>
    public static ServerEndpoint? CurrentServer(string? directory = null)
    {
        string? log = NewestPlayerLog(directory);
        if (log == null) return null;

        return FindCurrentServer(ReadShared(log));
    }
}
