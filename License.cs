using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace JinxyClicker;

/// <summary>The one answer every Pro feature reads.</summary>
/// <remarks>
/// NeedsFirstUseStamp is true the first time a timed pass is seen: the caller
/// should record "now" as the first-use date so the countdown starts from use,
/// not from when the key was minted.
/// </remarks>
public readonly record struct LicenseStatus(
    bool IsPro, ProTier? Tier, DateTime? ExpiresUtc, bool NeedsFirstUseStamp)
{
    public static readonly LicenseStatus Free = new(false, null, null, false);
}

/// <summary>
/// Turns a stored key plus a first-use date into an <see cref="LicenseStatus"/>.
/// Pure and clock-injected, so expiry is testable without waiting real time.
/// </summary>
public sealed class License
{
    private readonly ECDsa _publicKey;
    private readonly IReadOnlySet<ulong> _blocklist;
    private readonly Func<DateTime> _nowUtc;

    public License(ECDsa publicKey, IReadOnlySet<ulong> blocklist, Func<DateTime> nowUtc)
    {
        _publicKey = publicKey;
        _blocklist = blocklist;
        _nowUtc = nowUtc;
    }

    public static int DaysFor(ProTier tier) => tier switch
    {
        ProTier.Day => 1,
        ProTier.Weekend => 3,
        ProTier.Month => 30,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), "Lifetime has no day count")
    };

    public LicenseStatus Evaluate(string? key, DateTime? firstUseUtc)
    {
        if (string.IsNullOrWhiteSpace(key)) return LicenseStatus.Free;

        LicensePayload? parsed = LicenseKeyCodec.Verify(key, _publicKey);
        if (parsed is not { } p) return LicenseStatus.Free;
        if (_blocklist.Contains(p.KeyId)) return LicenseStatus.Free;

        if (p.Tier == ProTier.Lifetime)
            return new LicenseStatus(true, ProTier.Lifetime, null, false);

        int days = DaysFor(p.Tier);
        DateTime now = _nowUtc();

        if (firstUseUtc is null)
            return new LicenseStatus(true, p.Tier, now.AddDays(days), true);

        DateTime expires = firstUseUtc.Value.AddDays(days);
        return now < expires
            ? new LicenseStatus(true, p.Tier, expires, false)
            : LicenseStatus.Free;
    }
}
