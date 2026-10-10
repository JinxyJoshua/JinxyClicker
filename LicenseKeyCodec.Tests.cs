using JinxyClicker;
using Xunit;

public class LicenseKeyCodecTests
{
    [Fact]
    public void Payload_round_trips()
    {
        var p = new LicensePayload(ProTier.Lifetime, 0xDEADBEEF12345678UL);
        byte[] bytes = LicenseKeyCodec.SerializePayload(p);
        Assert.True(LicenseKeyCodec.TryDeserializePayload(bytes, out var back));
        Assert.Equal(p, back);
    }

    [Fact]
    public void Payload_rejects_wrong_length()
    {
        Assert.False(LicenseKeyCodec.TryDeserializePayload(new byte[3], out _));
    }

    [Fact]
    public void Payload_rejects_unknown_tier()
    {
        byte[] bytes = LicenseKeyCodec.SerializePayload(new LicensePayload(ProTier.Day, 1));
        bytes[1] = 99; // not a defined tier
        Assert.False(LicenseKeyCodec.TryDeserializePayload(bytes, out _));
    }

    [Fact]
    public void Base32_round_trips()
    {
        byte[] data = { 0, 1, 2, 250, 251, 252, 253, 254, 255 };
        string text = LicenseKeyCodec.Base32Encode(data);
        Assert.Equal(data, LicenseKeyCodec.Base32Decode(text));
    }

    [Fact]
    public void Base32_decode_is_forgiving()
    {
        // lower case, and Crockford-confusable letters map to digits
        byte[] data = { 10, 20, 30, 40 };
        string text = LicenseKeyCodec.Base32Encode(data).ToLowerInvariant();
        Assert.Equal(data, LicenseKeyCodec.Base32Decode(text));
    }

    [Fact]
    public void Base32_decode_returns_null_on_bad_char()
    {
        Assert.Null(LicenseKeyCodec.Base32Decode("!!!!"));
    }
}
