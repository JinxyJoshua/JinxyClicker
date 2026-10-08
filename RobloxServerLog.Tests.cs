using System.Net;
using Xunit;

namespace JinxyClicker;

public class RobloxServerLogTests
{
    // Real line shapes, taken verbatim from a Roblox Player log.
    private const string Udmux =
        "2026-09-26T12:26:50.671Z,7.671295,70b8,7 [FLog::Network] UDMUX Address = 128.116.127.33, Port = 63946 | RCC Server Address = 10.204.5.203, Port = 63946";
    private const string ServerId =
        "2026-09-26T12:26:51.319Z,8.319178,7bd8,7 [FLog::Network] serverId: 128.116.127.33|63946";

    [Fact]
    public void ServerId_line_is_parsed()
    {
        ServerEndpoint? s = RobloxServerLog.FindCurrentServer(ServerId);

        Assert.NotNull(s);
        Assert.Equal("128.116.127.33", s!.Value.Ip);
        Assert.Equal(63946, s.Value.Port);
    }

    [Fact]
    public void Udmux_line_is_parsed_and_its_private_rcc_address_is_ignored()
    {
        ServerEndpoint? s = RobloxServerLog.FindCurrentServer(Udmux);

        Assert.NotNull(s);
        // The public UDMUX address, never the 10.x RCC one after the pipe.
        Assert.Equal("128.116.127.33", s!.Value.Ip);
        Assert.Equal(63946, s.Value.Port);
    }

    [Fact]
    public void Last_connection_in_the_file_wins()
    {
        string log = string.Join('\n',
            "[FLog::Network] serverId: 128.116.2.33|54089",
            "... play ...",
            "[FLog::Network] serverId: 128.116.48.33|64508");

        ServerEndpoint? s = RobloxServerLog.FindCurrentServer(log);

        Assert.Equal("128.116.48.33", s!.Value.Ip);
        Assert.Equal(64508, s.Value.Port);
    }

    [Fact]
    public void ServerId_wins_over_the_udmux_line_of_the_same_connection()
    {
        // As they actually appear: UDMUX first, serverId a few lines later.
        string log = Udmux + "\n" + ServerId;

        ServerEndpoint? s = RobloxServerLog.FindCurrentServer(log);

        // Same address either way here, but the point is it resolves to one
        // endpoint rather than being thrown by the two lines.
        Assert.Equal("128.116.127.33", s!.Value.Ip);
    }

    [Fact]
    public void Udmux_is_used_when_it_is_newer_than_any_serverId()
    {
        // A fresh connection has logged UDMUX but not yet serverId; the only
        // serverId in the file belongs to the previous server.
        string log = string.Join('\n',
            "[FLog::Network] serverId: 128.116.2.33|54089",
            "[FLog::Network] UDMUX Address = 128.116.48.33, Port = 64508 | RCC Server Address = 10.1.2.3, Port = 64508");

        ServerEndpoint? s = RobloxServerLog.FindCurrentServer(log);

        Assert.Equal("128.116.48.33", s!.Value.Ip);
        Assert.Equal(64508, s.Value.Port);
    }

    [Fact]
    public void The_api_endpoint_line_is_not_mistaken_for_a_server()
    {
        // This is the line that geolocates to Ashburn no matter the server — it is
        // the API host, not the game server, and must be ignored.
        string log =
            "[DFLog::HttpTraceError] HttpResponse status:404 url:{ \"https://apis.roblox.com/\" } ip:128.116.102.3 external:1";

        Assert.Null(RobloxServerLog.FindCurrentServer(log));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("just some unrelated log text with no connection in it")]
    public void No_connection_means_no_server(string? log)
    {
        Assert.Null(RobloxServerLog.FindCurrentServer(log));
    }

    [Theory]
    [InlineData("128.116.48.33", true)]
    [InlineData("8.8.8.8", true)]
    [InlineData("10.204.5.203", false)]   // private (RCC)
    [InlineData("127.0.0.1", false)]      // loopback
    [InlineData("192.168.1.5", false)]    // private
    [InlineData("172.16.0.1", false)]     // private
    [InlineData("172.32.0.1", true)]      // just outside the private block
    [InlineData("169.254.1.1", false)]    // link-local
    [InlineData("100.64.0.1", false)]     // CGNAT
    [InlineData("0.0.0.0", false)]
    public void Public_addresses_are_told_from_private_ones(string ip, bool expected)
    {
        Assert.Equal(expected, RobloxServerLog.IsPublic(IPAddress.Parse(ip)));
    }

    [Fact]
    public void A_private_only_connection_yields_nothing()
    {
        // If somehow only a private address is present, it is not a server.
        string log = "[FLog::Network] serverId: 10.0.0.5|50000";

        Assert.Null(RobloxServerLog.FindCurrentServer(log));
    }
}
