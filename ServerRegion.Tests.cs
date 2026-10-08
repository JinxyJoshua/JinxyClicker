using Xunit;

namespace JinxyClicker;

public class ServerRegionTests
{
    // The ip-api.com free endpoint's actual response shape.
    private const string DallasJson =
        "{\"status\":\"success\",\"country\":\"United States\",\"countryCode\":\"US\",\"regionName\":\"Texas\",\"city\":\"Dallas\"}";

    [Fact]
    public void A_success_response_is_parsed_into_its_fields()
    {
        ServerRegion? r = ServerRegion.FromApiJson(DallasJson, "128.116.32.33");

        Assert.NotNull(r);
        Assert.Equal("128.116.32.33", r!.Ip);
        Assert.Equal("US", r.CountryCode);
        Assert.Equal("United States", r.Country);
        Assert.Equal("Texas", r.Region);
        Assert.Equal("Dallas", r.City);
    }

    [Fact]
    public void Lat_and_lon_are_parsed_for_the_map()
    {
        string json =
            "{\"status\":\"success\",\"country\":\"United States\",\"countryCode\":\"US\"," +
            "\"regionName\":\"Texas\",\"city\":\"Dallas\",\"lat\":32.7767,\"lon\":-96.797}";

        ServerRegion? r = ServerRegion.FromApiJson(json, "128.116.32.33");

        Assert.Equal(32.7767, r!.Lat, 3);
        Assert.Equal(-96.797, r.Lon, 3);
    }

    [Fact]
    public void A_failed_lookup_is_null_not_an_exception()
    {
        string fail = "{\"status\":\"fail\",\"message\":\"private range\",\"query\":\"10.0.0.1\"}";

        Assert.Null(ServerRegion.FromApiJson(fail, "10.0.0.1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]        // valid JSON, wrong shape
    [InlineData("\"a string\"")]
    public void Garbage_in_gives_null_out(string? json)
    {
        Assert.Null(ServerRegion.FromApiJson(json, "1.2.3.4"));
    }

    [Fact]
    public void Headline_is_the_flag_and_the_city()
    {
        ServerRegion? r = ServerRegion.FromApiJson(DallasJson, "128.116.32.33");

        Assert.Equal("\U0001F1FA\U0001F1F8 Dallas", r!.Headline); // 🇺🇸 Dallas
    }

    [Fact]
    public void Detail_is_region_then_country()
    {
        ServerRegion? r = ServerRegion.FromApiJson(DallasJson, "128.116.32.33");

        Assert.Equal("Texas, United States", r!.Detail);
    }

    [Fact]
    public void Detail_does_not_repeat_a_city_that_equals_its_region()
    {
        // City states (Singapore, Warsaw-as-region, etc.) can report city == region.
        string json =
            "{\"status\":\"success\",\"country\":\"Singapore\",\"countryCode\":\"SG\",\"regionName\":\"Singapore\",\"city\":\"Singapore\"}";

        ServerRegion? r = ServerRegion.FromApiJson(json, "1.2.3.4");

        Assert.Equal("\U0001F1F8\U0001F1EC Singapore", r!.Headline); // 🇸🇬 Singapore
        // The headline already says Singapore, so the detail adds nothing and is
        // left empty rather than repeating it as "Singapore, Singapore".
        Assert.Equal("", r.Detail);
    }

    [Fact]
    public void A_city_with_no_region_still_reads_cleanly()
    {
        string json =
            "{\"status\":\"success\",\"country\":\"Poland\",\"countryCode\":\"PL\",\"regionName\":\"\",\"city\":\"Warsaw\"}";

        ServerRegion? r = ServerRegion.FromApiJson(json, "128.116.2.33");

        Assert.Equal("\U0001F1F5\U0001F1F1 Warsaw", r!.Headline); // 🇵🇱 Warsaw
        Assert.Equal("Poland", r.Detail);
    }

    [Theory]
    [InlineData("US", "\U0001F1FA\U0001F1F8")]
    [InlineData("pl", "\U0001F1F5\U0001F1F1")] // case-insensitive
    [InlineData("", "")]
    [InlineData("U", "")]
    [InlineData("USA", "")]
    [InlineData("1A", "")]
    public void Flags_come_from_two_letter_codes_only(string code, string expected)
    {
        Assert.Equal(expected, ServerRegion.FlagFor(code));
    }
}
