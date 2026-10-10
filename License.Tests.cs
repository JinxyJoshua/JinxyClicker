using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using JinxyClicker;
using Xunit;

public class LicenseTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static (License gate, ECDsa minter) Build(
        DateTime now, IReadOnlySet<ulong>? blocked = null)
    {
        var minter = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pub = ECDsa.Create();
        pub.ImportSubjectPublicKeyInfo(minter.ExportSubjectPublicKeyInfo(), out _);
        var gate = new License(pub, blocked ?? new HashSet<ulong>(), () => now);
        return (gate, minter);
    }

    [Fact]
    public void No_key_is_not_pro()
    {
        var (gate, _) = Build(T0);
        Assert.False(gate.Evaluate(null, null).IsPro);
        Assert.False(gate.Evaluate("   ", null).IsPro);
    }

    [Fact]
    public void Lifetime_key_is_pro_forever()
    {
        var (gate, minter) = Build(T0);
        string key = LicenseKeyCodec.Create(new LicensePayload(ProTier.Lifetime, 1), minter);
        var status = gate.Evaluate(key, null);
        Assert.True(status.IsPro);
        Assert.Equal(ProTier.Lifetime, status.Tier);
        Assert.Null(status.ExpiresUtc);
        Assert.False(status.NeedsFirstUseStamp);
    }

    [Fact]
    public void First_use_of_a_pass_asks_to_be_stamped()
    {
        var (gate, minter) = Build(T0);
        string key = LicenseKeyCodec.Create(new LicensePayload(ProTier.Weekend, 2), minter);
        var status = gate.Evaluate(key, null);
        Assert.True(status.IsPro);
        Assert.True(status.NeedsFirstUseStamp);
        Assert.Equal(T0.AddDays(3), status.ExpiresUtc);
    }

    [Fact]
    public void Pass_within_window_is_pro()
    {
        var (gate, minter) = Build(T0.AddDays(2));
        string key = LicenseKeyCodec.Create(new LicensePayload(ProTier.Weekend, 3), minter);
        Assert.True(gate.Evaluate(key, T0).IsPro);
    }

    [Fact]
    public void Expired_pass_is_not_pro()
    {
        var (gate, minter) = Build(T0.AddDays(4));
        string key = LicenseKeyCodec.Create(new LicensePayload(ProTier.Weekend, 4), minter);
        Assert.False(gate.Evaluate(key, T0).IsPro);
    }

    [Fact]
    public void Blocklisted_key_is_not_pro()
    {
        var (gate, minter) = Build(T0, new HashSet<ulong> { 99 });
        string key = LicenseKeyCodec.Create(new LicensePayload(ProTier.Lifetime, 99), minter);
        Assert.False(gate.Evaluate(key, null).IsPro);
    }

    [Fact]
    public void Garbage_key_is_not_pro()
    {
        var (gate, _) = Build(T0);
        Assert.False(gate.Evaluate("JINXY-NOTAREALKEY", null).IsPro);
    }
}
