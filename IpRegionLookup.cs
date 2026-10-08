using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace JinxyClicker;

/// <summary>
/// Turns a server IP into a <see cref="ServerRegion"/> using ip-api.com, and
/// remembers what it learns so the same server is never looked up twice.
/// </summary>
/// <remarks>
/// The free ip-api endpoint is HTTP-only and keyless, with a generous per-minute
/// limit. That suits this exactly: the only value ever sent is the game server's
/// public address — not anything of the user's — and a lookup happens at most once
/// per server joined, which is rare next to the limit. Results are cached for the
/// life of the app because a server's location does not move.
///
/// Every failure path returns null rather than throwing: a blocked request, a
/// rate-limit, an address the service cannot place. The overlay then simply shows
/// no region, which is the honest outcome.
/// </remarks>
public sealed class IpRegionLookup
{
    private const string Fields = "status,message,country,countryCode,regionName,city,lat,lon";

    private static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(6) };

    private readonly HttpClient _http;
    private readonly Dictionary<string, ServerRegion> _cache = new();

    public IpRegionLookup(HttpClient? http = null) => _http = http ?? Shared;

    /// <summary>
    /// Where the user themselves are, for drawing the "you" end of the line.
    /// </summary>
    /// <remarks>
    /// Looks up the caller's own public address — ip-api answers the bare /json/
    /// with the requester's location. The address is never shown or stored; only
    /// the coarse city-level point is kept, and only to place a pin.
    /// </remarks>
    public Task<ServerRegion?> LookupSelfAsync(CancellationToken ct = default) =>
        LookupAsync("", ct);

    public async Task<ServerRegion?> LookupAsync(string ip, CancellationToken ct = default)
    {
        string cacheKey = ip.Length == 0 ? "__self__" : ip;

        lock (_cache)
            if (_cache.TryGetValue(cacheKey, out ServerRegion? cached))
                return cached;

        string json;
        try
        {
            json = await _http
                .GetStringAsync($"http://ip-api.com/json/{ip}?fields={Fields}", ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }

        ServerRegion? region = ServerRegion.FromApiJson(json, ip);

        if (region != null)
            lock (_cache)
                _cache[cacheKey] = region;

        return region;
    }
}
