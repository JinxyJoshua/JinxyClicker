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

    [Fact]
    public void Activating_a_fresh_key_starts_with_no_first_use()
    {
        var (gate, minter) = Build(T0);
        string key = LicenseKeyCodec.Create(new LicensePayload(ProTier.Weekend, 5), minter);
        var prep = gate.PrepareActivation(currentKey: null, currentFirstUse: null, pastedKey: key);
        Assert.NotNull(prep);
        Assert.Equal(key, prep!.Value.Key);
        Assert.Null(prep.Value.FirstUseUtc);
    }

    [Fact]
    public void Re_pasting_the_same_pass_key_keeps_its_original_clock()
    {
        var (gate, minter) = Build(T0);
        string key = LicenseKeyCodec.Create(new LicensePayload(ProTier.Day, 6), minter);
        // It was first used three days ago and has long since expired.
        DateTime firstUse = T0.AddDays(-3);
        var prep = gate.PrepareActivation(currentKey: key, currentFirstUse: firstUse, pastedKey: key);
        Assert.NotNull(prep);
        Assert.Equal(firstUse, prep!.Value.FirstUseUtc); // not reset to now
    }

    [Fact]
    public void Activating_a_different_key_resets_the_clock()
    {
        var (gate, minter) = Build(T0);
        string oldKey = LicenseKeyCodec.Create(new LicensePayload(ProTier.Day, 7), minter);
        string newKey = LicenseKeyCodec.Create(new LicensePayload(ProTier.Month, 8), minter);
        var prep = gate.PrepareActivation(currentKey: oldKey, currentFirstUse: T0.AddDays(-1), pastedKey: newKey);
        Assert.NotNull(prep);
        Assert.Equal(newKey, prep!.Value.Key);
        Assert.Null(prep.Value.FirstUseUtc); // a different key starts fresh
    }

    [Fact]
    public void Activating_an_invalid_key_is_rejected()
    {
        var (gate, _) = Build(T0);
        Assert.Null(gate.PrepareActivation(null, null, "JINXY-NOTAREALKEY"));
    }

    [Fact]
    public void TierOf_reads_the_tier_of_a_valid_key()
    {
        var (gate, minter) = Build(T0);
        string key = LicenseKeyCodec.Create(new LicensePayload(ProTier.Month, 1), minter);
        Assert.Equal(ProTier.Month, gate.TierOf(key));
        Assert.Null(gate.TierOf("garbage"));
        Assert.Null(gate.TierOf(null));
    }
}
