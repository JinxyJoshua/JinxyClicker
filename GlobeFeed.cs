using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace JinxyClicker;

/// <summary>
/// A tiny loopback HTTP endpoint the hosted globe page polls to follow the
/// current server live, so the map moves on its own when you change servers
/// instead of showing a one-off snapshot.
/// </summary>
/// <remarks>
/// It serves only coarse, non-personal location data — the server's city and
/// coordinates, and the user's own city-level point for the "you" pin, never an
/// address — and only on 127.0.0.1. CORS is permissive because the page lives on
/// its own origin (the Cloudflare one) and has to be allowed to read it; a browser
/// still treats a loopback address as trustworthy, so an https page may read it
/// without a mixed-content block. Nothing here accepts input — every request gets
/// the same current snapshot — so there is no surface to send it anything.
/// </remarks>
public sealed class GlobeFeed : IDisposable
{
    private HttpListener? _listener;
    private volatile string _json = "{\"inGame\":false}";

    /// <summary>The port it bound to, or 0 if it never started.</summary>
    public int Port { get; private set; }

    /// <summary>
    /// The globe page this serves at "/". Serving it over HTTP rather than opening
    /// it as a file is deliberate: a file:// URL opened through the shell loses its
    /// query string, which is how the real server and the live port reach the page.
    /// </summary>
    public string? HtmlPath { get; set; }

    public bool IsRunning => _listener is { IsListening: true };

    /// <summary>Binds a loopback port and begins serving. Safe to call twice.</summary>
    public bool Start()
    {
        if (IsRunning) return true;

        for (int attempt = 0; attempt < 4; attempt++)
        {
            int port = FreeLoopbackPort();
            if (port == 0) continue;

            try
            {
                var listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                listener.Start();

                _listener = listener;
                Port = port;
                _ = Task.Run(ServeLoop);
                return true;
            }
            catch
            {
                // Port taken between picking it and binding, or blocked — try again.
            }
        }

        return false;
    }

    /// <summary>Replaces the snapshot every request will return from now on.</summary>
    public void Publish(object payload)
    {
        try { _json = JsonSerializer.Serialize(payload); }
        catch { /* a payload that won't serialise just leaves the last one up */ }
    }

    /// <summary>Marks that the player is not on a server right now.</summary>
    public void PublishIdle() => _json = "{\"inGame\":false}";

    private async Task ServeLoop()
    {
        HttpListener listener = _listener!;

        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync().ConfigureAwait(false); }
            catch { break; } // listener stopped

            try
            {
                ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
                ctx.Response.Headers["Cache-Control"] = "no-store";

                string path = ctx.Request.Url?.AbsolutePath ?? "/";
                byte[] bytes;

                if (path.EndsWith("/feed", StringComparison.Ordinal))
                {
                    ctx.Response.ContentType = "application/json";
                    bytes = Encoding.UTF8.GetBytes(_json);
                }
                else
                {
                    byte[]? html = ReadHtml();
                    if (html == null)
                    {
                        ctx.Response.StatusCode = 404;
                        bytes = Encoding.UTF8.GetBytes("globe page not found");
                    }
                    else
                    {
                        ctx.Response.ContentType = "text/html; charset=utf-8";
                        bytes = html;
                    }
                }

                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            }
            catch
            {
                // The page navigated away mid-write, or similar — nothing to do.
            }
            finally
            {
                try { ctx.Response.Close(); } catch { }
            }
        }
    }

    private byte[]? ReadHtml()
    {
        // A dev file on disk wins when present, so the page can be edited and
        // reloaded without rebuilding. Shipped builds fall back to the copy baked
        // into the assembly, so the globe works with no file and no web host.
        try
        {
            if (HtmlPath != null && File.Exists(HtmlPath))
                return File.ReadAllBytes(HtmlPath);
        }
        catch { }

        try
        {
            using System.IO.Stream? stream =
                typeof(GlobeFeed).Assembly.GetManifestResourceStream("globe.html");
            if (stream == null) return null;

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static int FreeLoopbackPort()
    {
        try
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        try { _listener?.Stop(); _listener?.Close(); } catch { }
        _listener = null;
    }
}
