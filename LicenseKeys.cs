using System.Collections.Generic;
using System.Security.Cryptography;

namespace JinxyClicker;

/// <summary>
/// The public half of the signing key (safe to ship — it can only check keys,
/// not make them) and the list of key ids that have leaked and must be refused.
/// </summary>
public static class LicenseKeys
{
    // Printed by `keygen init`. The matching private key lives only on the
    // author's PC under %APPDATA%\JinxyKeyGen and is never committed or shipped.
    public const string PublicKeyBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEd4yGojWAX+tgU7cK9qPvJirCBvq1nFWeFFUr48F4oAxnyAHyw/meEyNdAyINpz+ErEu1usS6riuSGvugfausxg==";

    public static ECDsa PublicKey()
    {
        var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(System.Convert.FromBase64String(PublicKeyBase64), out _);
        return key;
    }

    /// <summary>
    /// The pass-check Worker: records when each timed pass was first activated, on
    /// the server, so the countdown can't be reset by deleting local files or
    /// reinstalling. Lifetime keys never call it and stay fully offline. Empty
    /// disables the check (passes then fall back to the local date).
    /// </summary>
    public const string PassServerUrl = "https://jinxy-pass-check.jinxy-server-globe.workers.dev";

    /// <summary>
    /// Key ids of leaked keys to refuse. Add an id here and ship an update to kill
    /// a key that has been posted publicly; look the id up in your KeyGen keys.csv.
    /// </summary>
    public static readonly IReadOnlySet<ulong> Blocklist = new HashSet<ulong>();
}
