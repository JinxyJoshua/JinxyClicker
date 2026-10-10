using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace JinxyClicker;

/// <summary>
/// Asks the pass-check Worker when a timed pass key was first activated. The server
/// records that moment once and returns the same date forever after, so a buyer
/// cannot reset a pass by wiping local files or reinstalling.
/// </summary>
/// <remarks>
/// Only passes call this. Lifetime keys don't expire, so they never need it and the
/// app works fully offline for them. If the server can't be reached, this returns
/// null and the caller falls back to the locally stored date — so a pass still
/// activates offline, and the server's date wins the moment the app is online again.
/// It sends only the key (the buyer's own property); no personal data.
/// </remarks>
public sealed class PassServer
{
    private readonly string _url;
    private readonly HttpClient _http;

    public PassServer(string url, HttpClient? http = null)
    {
        _url = url;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
    }

    /// <summary>
    /// The authoritative first-use date for a pass key, recording it the first time.
    /// Null for a lifetime key, an invalid key, or when the server can't be reached.
    /// </summary>
    public async Task<DateTime?> FirstUseUtcAsync(string key)
    {
        try
        {
            string payload = JsonSerializer.Serialize(new { key });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using HttpResponseMessage resp = await _http.PostAsync(_url, content).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(body);
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("ok", out JsonElement ok) || !ok.GetBoolean()) return null;
            if (!root.TryGetProperty("firstUseUtc", out JsonElement f) || f.ValueKind == JsonValueKind.Null)
                return null;

            if (DateTime.TryParse(f.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime dt))
                return dt.ToUniversalTime();

            return null;
        }
        catch
        {
            return null; // offline, timed out, or a bad response: fall back to local
        }
    }
}
