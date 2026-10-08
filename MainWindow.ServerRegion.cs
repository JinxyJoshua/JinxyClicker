using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace JinxyClicker;

/// <summary>
/// The server-region banner: names the region of the Roblox server on joining.
/// </summary>
/// <remarks>
/// Kept out of MainWindow.xaml.cs because it is a self-contained feature with its
/// own window, poller and lookup, and that file is already the largest here.
///
/// The whole feature reads one thing Roblox has already written to its log — the
/// server's relay address — and asks a geolocation service where that is. It never
/// reads the game. It shows a region, not a ping: a real ping to a Roblox server
/// cannot be measured from outside the game (the relays drop ICMP and refuse TCP,
/// and Roblox logs no round-trip of its own), so a ping field would have to be
/// invented, and an invented number is worse than an honest absence.
/// </remarks>
public partial class MainWindow
{
    private ServerOverlay? _serverOverlay;
    private DispatcherTimer? _serverTimer;
    private readonly IpRegionLookup _serverLookup = new();

    /// <summary>The server currently shown, so an unchanged poll does nothing.</summary>
    private string? _lastServerIp;

    /// <summary>How many seconds the banner stays up; 0 means "keep it on".</summary>
    private int _serverOverlaySeconds;

    /// <summary>The region now shown, so the globe button can open the real server.</summary>
    private ServerRegion? _currentRegion;

    /// <summary>The user's own coarse location, looked up once for the "you" pin.</summary>
    private ServerRegion? _selfLocation;

    /// <summary>
    /// The high-tech server map, hosted off the app so it adds nothing to the
    /// download. The app only ever opens it with the real server's coordinates.
    /// </summary>
    /// <remarks>
    /// Dev points at the local file so the real detected server can be checked
    /// right now; this becomes the Cloudflare Pages URL once it is deployed.
    /// </remarks>
    /// <summary>
    /// The globe page on disk, served to the browser over HTTP by the feed.
    /// Opened over HTTP, not file://, because the shell strips a file URL's query
    /// string — and that query is how the real server reaches the page. Becomes
    /// the Cloudflare Pages URL once deployed.
    /// </summary>
    private const string GlobeHtmlPath =
        @"C:\Users\rschi\_dev\Joshua\jinxy-server-globe\index.html";

    /// <summary>The loopback feed the open globe page polls to follow servers live.</summary>
    private readonly GlobeFeed _globeFeed = new();

    /// <summary>
    /// True once the globe has been opened, which keeps detection and the feed
    /// running even after the user leaves the Server page, so the open map keeps
    /// following them.
    /// </summary>
    private bool _globeLive;

    /// <summary>How often the newest log is checked for a server change.</summary>
    /// <remarks>
    /// Joining is an occasional event, so there is nothing to gain from polling
    /// fast — two and a half seconds keeps the banner feeling prompt on joining
    /// without reading a megabyte-scale log any more often than it needs to.
    /// </remarks>
    private static readonly TimeSpan ServerPollInterval = TimeSpan.FromSeconds(2.5);

    private void NavServer_Click(object sender, RoutedEventArgs e) =>
        ShowPage(NavServer, PageServer, "Server", "Where the server you joined is");

    /// <summary>Puts the saved overlay duration onto the field and its preset button.</summary>
    private void LoadServerRegion(AppSettings s)
    {
        _serverOverlaySeconds = s.ServerOverlaySeconds;

        // Direct-set above, then light the matching preset. A hand-edited value
        // that is none of the presets leaves them all unlit but still in effect.
        RadioButton? match = _serverOverlaySeconds switch
        {
            0 => ServerDurAlways,
            3 => ServerDur3,
            5 => ServerDur5,
            10 => ServerDur10,
            _ => null
        };

        if (match != null) match.IsChecked = true;
    }

    /// <summary>A display-duration preset was picked.</summary>
    private void ServerOverlayDuration_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } || !int.TryParse(tag, out int seconds))
            return;

        _serverOverlaySeconds = seconds;

        // Apply to a banner already on screen: "keep it on" stops a countdown, a
        // shorter time starts hiding now rather than only on the next join.
        _serverOverlay?.ReapplyAutoHide(seconds);

        SaveAppSettings();
    }

    /// <summary>Called when the overlay toggle is flipped.</summary>
    private void ServerRegion_Changed(object sender, RoutedEventArgs e)
    {
        if (ServerRegionCheck?.IsChecked != true)
        {
            // Turned off: drop the banner at once, and forget the current server
            // so turning it back on re-detects rather than showing nothing.
            _serverOverlay?.Hide();
            _lastServerIp = null;
        }

        UpdateServerRegionActivity();
        SaveAppSettings();
    }

    /// <summary>
    /// Starts or stops the poller to match what currently needs it: the overlay
    /// being on, or the Server page being open for its live readout.
    /// </summary>
    private void UpdateServerRegionActivity()
    {
        bool wanted = ServerRegionCheck?.IsChecked == true
                      || PageServer?.IsVisible == true
                      || _globeLive;

        if (wanted) StartServerPolling();
        else StopServerPolling();
    }

    private void StartServerPolling()
    {
        _serverTimer ??= CreateServerTimer();

        if (!_serverTimer.IsEnabled)
        {
            _serverTimer.Start();
            PollServerOnce(); // don't wait a whole interval for the first reading
        }
    }

    private void StopServerPolling()
    {
        _serverTimer?.Stop();

        // The overlay is the toggle's; the page readout is not. Only the banner
        // comes down here — the page, if it is still open, just stops updating.
        if (ServerRegionCheck?.IsChecked != true) _serverOverlay?.Hide();
    }

    private DispatcherTimer CreateServerTimer()
    {
        var timer = new DispatcherTimer { Interval = ServerPollInterval };
        timer.Tick += (_, _) => PollServerOnce();
        return timer;
    }

    private void PollServerOnce()
    {
        if (!IsRobloxRunning())
        {
            _lastServerIp = null;
            _currentRegion = null;
            UpdateGlobeButton();
            if (_globeFeed.IsRunning) _globeFeed.PublishIdle();
            _serverOverlay?.Hide();
            SetServerPageStatus("Roblox isn't running", "Start a game and your server region shows here.");
            return;
        }

        ServerEndpoint? server = RobloxServerLog.CurrentServer();

        if (server == null)
        {
            // In the app but not in a game yet — nothing to place.
            _lastServerIp = null;
            _currentRegion = null;
            UpdateGlobeButton();
            if (_globeFeed.IsRunning) _globeFeed.PublishIdle();
            _serverOverlay?.Hide();
            SetServerPageStatus("Waiting to join a server…", "");
            return;
        }

        string ip = server.Value.Ip;
        if (ip == _lastServerIp) return; // same server, already resolved and shown

        _lastServerIp = ip;

        if (ServerRegionCheck?.IsChecked == true) EnsureServerOverlay().ShowLooking();
        SetServerPageStatus("Locating server…", "");

        _ = ResolveAndShowAsync(server.Value);
    }

    private async Task ResolveAndShowAsync(ServerEndpoint server)
    {
        ServerRegion? region = await _serverLookup.LookupAsync(server.Ip);

        // The lookup is slower than the poll, so a newer server may have been
        // seen while this one was in flight. If so, this result is stale — drop it.
        if (_lastServerIp != server.Ip) return;

        if (region == null)
        {
            SetServerPageStatus("Couldn't locate this server", "The geolocation service didn't answer.");
            _serverOverlay?.Hide();
            return;
        }

        if (ServerRegionCheck?.IsChecked == true)
            EnsureServerOverlay().ShowRegion(region, _serverOverlaySeconds);

        SetServerPageStatus(region.Headline, region.Detail);

        // Remember the real server and light the globe button for it. The user's
        // own location is looked up once in the background for the "you" pin.
        _currentRegion = region;
        UpdateGlobeButton();
        await EnsureSelfLocationAsync();
        PublishFeed();
    }

    private async Task EnsureSelfLocationAsync()
    {
        if (_selfLocation != null) return;

        try { _selfLocation = await _serverLookup.LookupSelfAsync(); }
        catch { /* no "you" pin, the server still shows */ }
    }

    private void UpdateGlobeButton()
    {
        if (ViewGlobeButton == null) return;

        // Enabled only once there is a real server with real coordinates to open.
        ViewGlobeButton.IsEnabled = _currentRegion is { Lat: not 0, Lon: not 0 };
    }

    /// <summary>Opens the hosted map in the browser, centred on the real server.</summary>
    /// <remarks>
    /// Starts the loopback feed and keeps detection running from here on, so the
    /// page the user just opened follows them live as they change servers rather
    /// than freezing on the one it opened with.
    /// </remarks>
    private void ViewServerGlobe_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRegion == null) return;

        _globeFeed.HtmlPath = GlobeHtmlPath;
        _globeFeed.Start();
        _globeLive = true;
        UpdateServerRegionActivity(); // keep polling even after leaving this page
        PublishFeed();

        try
        {
            Process.Start(new ProcessStartInfo(BuildGlobeUrl(_currentRegion)) { UseShellExecute = true });
        }
        catch
        {
            SetServerPageStatus(_currentRegion.Headline, "Couldn't open the browser.");
        }
    }

    /// <summary>Pushes the current server (or idle) to the live feed.</summary>
    private void PublishFeed()
    {
        if (!_globeFeed.IsRunning) return;

        if (_currentRegion is { } r)
            _globeFeed.Publish(new
            {
                inGame = true,
                city = r.City,
                region = r.Region,
                country = r.Country,
                cc = r.CountryCode,
                lat = r.Lat,
                lon = r.Lon,
                ulat = _selfLocation?.Lat,
                ulon = _selfLocation?.Lon
            });
        else
            _globeFeed.PublishIdle();
    }

    private string BuildGlobeUrl(ServerRegion r)
    {
        var sb = new StringBuilder($"http://127.0.0.1:{_globeFeed.Port}/").Append('?');

        void Add(string key, string value) =>
            sb.Append(key).Append('=').Append(Uri.EscapeDataString(value)).Append('&');

        string Fixed(double d) => d.ToString("0.####", CultureInfo.InvariantCulture);

        Add("city", r.City);
        Add("region", r.Region);
        Add("country", r.Country);
        Add("cc", r.CountryCode);
        Add("lat", Fixed(r.Lat));
        Add("lon", Fixed(r.Lon));

        if (_selfLocation is { Lat: not 0, Lon: not 0 } me)
        {
            Add("ulat", Fixed(me.Lat));
            Add("ulon", Fixed(me.Lon));
        }

        // Tells the page to poll the loopback feed and follow servers live.
        if (_globeFeed.IsRunning) Add("live", _globeFeed.Port.ToString());

        return sb.ToString().TrimEnd('&');
    }

    private ServerOverlay EnsureServerOverlay()
    {
        _serverOverlay ??= new ServerOverlay { StreamerMode = StreamerModeCheck?.IsChecked == true };
        return _serverOverlay;
    }

    private void SetServerPageStatus(string headline, string detail)
    {
        if (ServerRegionStatusText != null) ServerRegionStatusText.Text = headline;

        if (ServerRegionDetailText != null)
        {
            ServerRegionDetailText.Text = detail;
            ServerRegionDetailText.Visibility =
                detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private static bool IsRobloxRunning()
    {
        try
        {
            Process[] found = Process.GetProcessesByName("RobloxPlayerBeta");
            bool any = found.Length > 0;
            foreach (Process p in found) p.Dispose();
            return any;
        }
        catch
        {
            // If the process list can't be read, assume it might be up rather than
            // hiding a banner the user asked for.
            return true;
        }
    }

    /// <summary>Closes the banner when the app does.</summary>
    private void ShutdownServerRegion()
    {
        _serverTimer?.Stop();
        _serverOverlay?.Close();
        _serverOverlay = null;
        _globeFeed.Dispose();
    }
}
