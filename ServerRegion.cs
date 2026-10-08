using System;
using System.Text;
using System.Text.Json;

namespace JinxyClicker;

/// <summary>
/// Where a game server is, as a place a person recognises rather than an address.
/// </summary>
/// <remarks>
/// Built from an ip-api.com lookup of the server's UDMUX address. Because that
/// address is a relay (see <see cref="RobloxServerLog"/>), this is the region the
/// server is served from — close to the real host, but not a claim about the exact
/// building. The fields are kept separate so the overlay can show a short headline
/// and a quieter line under it without re-parsing anything.
/// </remarks>
public sealed record ServerRegion(
    string Ip,
    string CountryCode,
    string Country,
    string Region,
    string City,
    double Lat = 0,
    double Lon = 0)
{
    /// <summary>The big line: a flag and the city (or the country, if no city).</summary>
    public string Headline
    {
        get
        {
            string flag = FlagFor(CountryCode);
            string place = City.Length > 0 ? City : Country.Length > 0 ? Country : "Unknown";
            return flag.Length > 0 ? flag + " " + place : place;
        }
    }

    /// <summary>The quiet line: region and country, without repeating the city.</summary>
    public string Detail
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();

            if (Region.Length > 0 && !Region.Equals(City, StringComparison.OrdinalIgnoreCase))
                parts.Add(Region);

            if (Country.Length > 0 && !Country.Equals(Region, StringComparison.OrdinalIgnoreCase))
                parts.Add(Country);

            return string.Join(", ", parts);
        }
    }

    /// <summary>
    /// Parses one ip-api.com JSON response into a region, or null if the service
    /// could not place the address.
    /// </summary>
    /// <remarks>
    /// The free endpoint answers <c>{"status":"success",...}</c> on a hit and
    /// <c>{"status":"fail","message":"..."}</c> on a miss (a reserved or private
    /// range, say). A failed status, a missing status, or malformed JSON all mean
    /// "no region", not an exception — the caller just shows nothing.
    /// </remarks>
    public static ServerRegion? FromApiJson(string? json, string ip)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object) return null;

            if (!root.TryGetProperty("status", out JsonElement status)
                || status.GetString() != "success")
                return null;

            return new ServerRegion(
                ip,
                Str(root, "countryCode"),
                Str(root, "country"),
                Str(root, "regionName"),
                Str(root, "city"),
                Num(root, "lat"),
                Num(root, "lon"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String
            ? e.GetString() ?? ""
            : "";

    private static double Num(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement e)
        && e.ValueKind == JsonValueKind.Number
        && e.TryGetDouble(out double d)
            ? d
            : 0;

    /// <summary>
    /// The flag emoji for a two-letter country code, or empty if it is not two
    /// A–Z letters.
    /// </summary>
    /// <remarks>
    /// A flag emoji is its country's two letters as regional-indicator symbols,
    /// which sit 0x1F1E6 above plain A. Anything that is not exactly two letters —
    /// an empty code, a stray value — yields no flag rather than a wrong one.
    /// </remarks>
    public static string FlagFor(string? countryCode)
    {
        if (countryCode is not { Length: 2 }) return "";

        char a = char.ToUpperInvariant(countryCode[0]);
        char b = char.ToUpperInvariant(countryCode[1]);

        if (a is < 'A' or > 'Z' || b is < 'A' or > 'Z') return "";

        const int baseCodePoint = 0x1F1E6; // regional indicator 'A'

        return new StringBuilder()
            .Append(char.ConvertFromUtf32(baseCodePoint + (a - 'A')))
            .Append(char.ConvertFromUtf32(baseCodePoint + (b - 'A')))
            .ToString();
    }
}
