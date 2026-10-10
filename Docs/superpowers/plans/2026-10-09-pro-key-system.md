# Pro Key System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the app a server-less license gate — a buyer pastes a signed key, the app verifies it offline and flips a single `IsPro` switch — plus the `KeyGen` tool to mint keys.

**Architecture:** A pure codec (`LicenseKeyCodec`) turns a tiny payload (tier + random id) into a signed, Base32 key string and verifies one with an embedded public key. A `License` gate turns a stored key + first-use date into an `IsPro` answer, handling pass expiry. The private signing key lives only in a separate `KeyGen` console project that never ships. The app stores the pasted key in `AppSettings` and shows a small activation box in Settings.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WPF, xUnit. Signing is ECDSA P-256 + SHA-256 from `System.Security.Cryptography` (built in — no new package). Sources are linked into the test project by file, matching the existing pattern.

**Spec:** `docs/superpowers/specs/2026-10-09-pro-key-system-design.md`

## Global Constraints

- Target framework is `net10.0-windows`; match the existing projects.
- Commit messages: imperative, no Claude attribution, no `Co-Authored-By` trailer (repo rule — history was rewritten to strip it).
- The `KeyGen` project and the private signing key file MUST never ship: `KeyGen/**` is excluded from the app's compile glob, and the key file is written outside the repo (`%APPDATA%\JinxyKeyGen`).
- The embedded public key in `LicenseKeys.cs` is filled in by running `keygen init` during Task 5 — it is a real pasted value, never left as a placeholder in a committed, "done" state.
- Tests never depend on the shipped public key: codec and gate tests generate their own throwaway key pair in-test.
- Key tiers and their lengths: Day = 1 day, Weekend = 3 days, Month = 30 days, Lifetime = forever. These live in exactly one map (`License.DaysFor`).

## Review Focus

- **Malformed / garbage key** (random text, wrong length, bad Base32): `LicenseKeyCodec.Verify` must return null, never throw. Covered in Task 2.
- **Tampered payload** (valid-looking key with one character changed): signature check must fail → null. Covered in Task 2.
- **Expired pass** (`now` past first-use + duration): gate returns not-Pro. Covered in Task 3.
- **Blocklisted key id** (leaked key added to blocklist): gate returns not-Pro even with a valid signature. Covered in Task 3.
- **Empty / whitespace / null stored key** (the normal free-user state): gate returns not-Pro without throwing. Covered in Task 3.

---

## File Structure

- `LicenseKeyCodec.cs` (app root) — pure make/verify + Base32. Compiled by app, linked by tests and KeyGen.
- `LicenseKeyCodec.Tests.cs` (app root) — codec tests.
- `License.cs` (app root) — the `IsPro` gate; depends only on the codec and an injected clock.
- `License.Tests.cs` (app root) — gate tests.
- `LicenseKeys.cs` (app root) — the embedded public key constant + the blocklist. Not unit-tested (it is data).
- `AppSettings.cs` (modify) — add `LicenseKey` and `LicenseFirstUseUtc`.
- `KeyGen/KeyGen.csproj` + `KeyGen/Program.cs` (new project) — `init` and `make`. Never shipped.
- `KeyGen/make-lifetime.cmd` etc. — double-click wrappers so no terminal typing is needed.
- `JinxyClicker.csproj` (modify) — exclude `KeyGen/**`.
- `Testing/JinxyClicker.Tests.csproj` (modify) — link the four new source/test files.
- `MainWindow.xaml` / a new `MainWindow.License.cs` (modify/new) — the activation box + startup wiring.

---

### Task 1: Codec — payload + Base32

**Files:**
- Create: `LicenseKeyCodec.cs`
- Test: `LicenseKeyCodec.Tests.cs`
- Modify: `Testing/JinxyClicker.Tests.csproj` (link both)

**Interfaces:**
- Produces:
  - `enum ProTier : byte { Day = 1, Weekend = 2, Month = 3, Lifetime = 4 }`
  - `readonly record struct LicensePayload(ProTier Tier, ulong KeyId)`
  - `static byte[] LicenseKeyCodec.SerializePayload(LicensePayload p)`
  - `static bool LicenseKeyCodec.TryDeserializePayload(byte[] bytes, out LicensePayload p)`
  - `static string LicenseKeyCodec.Base32Encode(byte[] data)`
  - `static byte[]? LicenseKeyCodec.Base32Decode(string s)`

- [ ] **Step 1: Link the new files into the test project**

In `Testing/JinxyClicker.Tests.csproj`, add inside the main `<ItemGroup>` of `<Compile>` entries:

```xml
    <Compile Include="../LicenseKeyCodec.cs" />
    <Compile Include="../LicenseKeyCodec.Tests.cs" />
```

- [ ] **Step 2: Write the failing tests**

Create `LicenseKeyCodec.Tests.cs`:

```csharp
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
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test Testing/JinxyClicker.Tests.csproj --filter LicenseKeyCodecTests`
Expected: FAIL — `LicenseKeyCodec` does not exist yet.

- [ ] **Step 4: Write the minimal implementation**

Create `LicenseKeyCodec.cs`:

```csharp
using System;
using System.Buffers.Binary;
using System.Text;

namespace JinxyClicker;

/// <summary>A tier of Pro access, as stored inside a license key.</summary>
public enum ProTier : byte { Day = 1, Weekend = 2, Month = 3, Lifetime = 4 }

/// <summary>What a key grants: a tier and a unique id used to ban a leaked key.</summary>
public readonly record struct LicensePayload(ProTier Tier, ulong KeyId);

/// <summary>
/// Makes and verifies license keys. The payload is tiny and signed; the signature
/// is what stops anyone forging a key without the private signing key.
/// </summary>
public static partial class LicenseKeyCodec
{
    // version(1) + tier(1) + keyId(8, little-endian) = 10 bytes.
    private const byte Version = 1;
    internal const int PayloadLength = 10;

    public static byte[] SerializePayload(LicensePayload p)
    {
        var b = new byte[PayloadLength];
        b[0] = Version;
        b[1] = (byte)p.Tier;
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(2), p.KeyId);
        return b;
    }

    public static bool TryDeserializePayload(byte[] bytes, out LicensePayload p)
    {
        p = default;
        if (bytes is null || bytes.Length != PayloadLength || bytes[0] != Version) return false;
        if (!Enum.IsDefined(typeof(ProTier), bytes[1])) return false;
        ulong keyId = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(2));
        p = new LicensePayload((ProTier)bytes[1], keyId);
        return true;
    }

    // Crockford Base32: no I, L, O, U to avoid confusion when read aloud or typed.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (byte value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                sb.Append(Alphabet[(buffer >> bits) & 31]);
            }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[]? Base32Decode(string s)
    {
        if (s is null) return null;
        var bytes = new System.Collections.Generic.List<byte>();
        int buffer = 0, bits = 0;
        foreach (char raw in s)
        {
            char c = char.ToUpperInvariant(raw);
            if (c is '-' or ' ') continue;
            c = c switch { 'I' or 'L' => '1', 'O' => '0', _ => c };
            int index = Alphabet.IndexOf(c);
            if (index < 0) return null;
            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)((buffer >> bits) & 0xFF));
            }
        }
        return bytes.ToArray();
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test Testing/JinxyClicker.Tests.csproj --filter LicenseKeyCodecTests`
Expected: PASS (6 tests).

- [ ] **Step 6: Commit**

```bash
git add LicenseKeyCodec.cs LicenseKeyCodec.Tests.cs Testing/JinxyClicker.Tests.csproj
git commit -m "Add license key payload and Base32 codec"
```

---

### Task 2: Codec — sign and verify

**Files:**
- Modify: `LicenseKeyCodec.cs`
- Modify: `LicenseKeyCodec.Tests.cs`

**Interfaces:**
- Consumes: `SerializePayload`, `TryDeserializePayload`, `Base32Encode`, `Base32Decode` from Task 1.
- Produces:
  - `static string LicenseKeyCodec.Create(LicensePayload p, ECDsa signingKey)`
  - `static LicensePayload? LicenseKeyCodec.Verify(string keyString, ECDsa publicKey)`

- [ ] **Step 1: Write the failing tests**

Add to `LicenseKeyCodec.Tests.cs`:

```csharp
using System.Security.Cryptography;

public class LicenseKeyCodecSigningTests
{
    private static ECDsa NewKey() => ECDsa.Create(ECCurve.NamedCurves.nistP256);

    [Fact]
    public void Create_then_verify_returns_payload()
    {
        using ECDsa key = NewKey();
        var p = new LicensePayload(ProTier.Lifetime, 123456789UL);
        string text = LicenseKeyCodec.Create(p, key);
        Assert.StartsWith("JINXY-", text);
        Assert.Equal(p, LicenseKeyCodec.Verify(text, key));
    }

    [Fact]
    public void Verify_rejects_a_different_key()
    {
        using ECDsa minter = NewKey();
        using ECDsa other = NewKey();
        string text = LicenseKeyCodec.Create(new LicensePayload(ProTier.Month, 42), minter);
        Assert.Null(LicenseKeyCodec.Verify(text, other));
    }

    [Fact]
    public void Verify_rejects_tampering()
    {
        using ECDsa key = NewKey();
        string text = LicenseKeyCodec.Create(new LicensePayload(ProTier.Day, 7), key);
        // Flip a character in the body (after the JINXY- prefix).
        char[] chars = text.ToCharArray();
        int i = text.Length - 3;
        chars[i] = chars[i] == 'A' ? 'B' : 'A';
        Assert.Null(LicenseKeyCodec.Verify(new string(chars), key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a key")]
    [InlineData("JINXY-!!!!")]
    [InlineData("JINXY-ABCDE")]
    public void Verify_returns_null_on_garbage(string junk)
    {
        using ECDsa key = NewKey();
        Assert.Null(LicenseKeyCodec.Verify(junk, key));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Testing/JinxyClicker.Tests.csproj --filter LicenseKeyCodecSigningTests`
Expected: FAIL — `Create`/`Verify` not defined.

- [ ] **Step 3: Write the minimal implementation**

Add to `LicenseKeyCodec.cs` (inside the class; also add `using System.Security.Cryptography;` at the top):

```csharp
    private const string Prefix = "JINXY-";
    private const int SignatureLength = 64; // P-256 IEEE-P1363 (r||s)

    public static string Create(LicensePayload p, ECDsa signingKey)
    {
        byte[] payload = SerializePayload(p);
        byte[] sig = signingKey.SignData(payload, HashAlgorithmName.SHA256);
        byte[] blob = new byte[payload.Length + sig.Length];
        Buffer.BlockCopy(payload, 0, blob, 0, payload.Length);
        Buffer.BlockCopy(sig, 0, blob, payload.Length, sig.Length);
        return Prefix + Group(Base32Encode(blob));
    }

    public static LicensePayload? Verify(string keyString, ECDsa publicKey)
    {
        if (string.IsNullOrWhiteSpace(keyString)) return null;

        string body = keyString.Trim();
        if (body.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            body = body.Substring(Prefix.Length);

        byte[]? blob = Base32Decode(body);
        if (blob is null || blob.Length != PayloadLength + SignatureLength) return null;

        byte[] payload = blob[..PayloadLength];
        byte[] sig = blob[PayloadLength..];

        if (!TryDeserializePayload(payload, out var p)) return null;
        if (!publicKey.VerifyData(payload, sig, HashAlgorithmName.SHA256)) return null;
        return p;
    }

    // Dash-grouped for readability; Base32Decode ignores the dashes again.
    private static string Group(string s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && i % 5 == 0) sb.Append('-');
            sb.Append(s[i]);
        }
        return sb.ToString();
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Testing/JinxyClicker.Tests.csproj --filter LicenseKeyCodecSigningTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add LicenseKeyCodec.cs LicenseKeyCodec.Tests.cs
git commit -m "Sign and verify license keys with ECDSA P-256"
```

---

### Task 3: The License gate

**Files:**
- Create: `License.cs`
- Create: `License.Tests.cs`
- Modify: `Testing/JinxyClicker.Tests.csproj` (link both)

**Interfaces:**
- Consumes: `LicenseKeyCodec.Verify`, `LicensePayload`, `ProTier`.
- Produces:
  - `readonly record struct LicenseStatus(bool IsPro, ProTier? Tier, DateTime? ExpiresUtc, bool NeedsFirstUseStamp)`
  - `class License` with ctor `License(ECDsa publicKey, IReadOnlySet<ulong> blocklist, Func<DateTime> nowUtc)`
  - `LicenseStatus License.Evaluate(string? key, DateTime? firstUseUtc)`
  - `static int License.DaysFor(ProTier tier)` (Day=1, Weekend=3, Month=30; Lifetime throws — callers must check Lifetime first)

- [ ] **Step 1: Link the new files into the test project**

In `Testing/JinxyClicker.Tests.csproj` add:

```xml
    <Compile Include="../License.cs" />
    <Compile Include="../License.Tests.cs" />
```

- [ ] **Step 2: Write the failing tests**

Create `License.Tests.cs`:

```csharp
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
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test Testing/JinxyClicker.Tests.csproj --filter LicenseTests`
Expected: FAIL — `License` not defined.

- [ ] **Step 4: Write the minimal implementation**

Create `License.cs`:

```csharp
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
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test Testing/JinxyClicker.Tests.csproj --filter LicenseTests`
Expected: PASS (7 tests).

- [ ] **Step 6: Commit**

```bash
git add License.cs License.Tests.cs Testing/JinxyClicker.Tests.csproj
git commit -m "Add License gate that turns a key into an IsPro answer"
```

---

### Task 4: AppSettings fields

**Files:**
- Modify: `AppSettings.cs`
- Modify: `AppSettings.Tests.cs`

**Interfaces:**
- Produces: `AppSettings.LicenseKey` (`string?`), `AppSettings.LicenseFirstUseUtc` (`DateTime?`).

- [ ] **Step 1: Write the failing test**

Add to `AppSettings.Tests.cs` a test that the new fields round-trip through JSON. Match the file's existing test style; if it already has a serialize-round-trip helper, reuse it. Otherwise:

```csharp
[Fact]
public void License_fields_round_trip_through_json()
{
    var s = new AppSettings
    {
        LicenseKey = "JINXY-ABCDE-FGHIJ",
        LicenseFirstUseUtc = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc)
    };
    string json = System.Text.Json.JsonSerializer.Serialize(s);
    var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
    Assert.Equal(s.LicenseKey, back.LicenseKey);
    Assert.Equal(s.LicenseFirstUseUtc, back.LicenseFirstUseUtc);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Testing/JinxyClicker.Tests.csproj --filter License_fields_round_trip_through_json`
Expected: FAIL — properties do not exist.

- [ ] **Step 3: Add the properties**

In `AppSettings.cs`, after the Gaming Mode block, add:

```csharp
    /// <summary>
    /// The Pro license key the user pasted, or null for a free install. The key
    /// itself carries the tier and is re-verified on every launch, so nothing here
    /// needs to be trusted — a hand-edited value just fails verification.
    /// </summary>
    public string? LicenseKey { get; set; }

    /// <summary>
    /// When a timed pass was first activated, so its countdown runs from first use
    /// rather than from when it was minted. Null for a lifetime key or no key.
    /// </summary>
    public DateTime? LicenseFirstUseUtc { get; set; }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test Testing/JinxyClicker.Tests.csproj --filter License_fields_round_trip_through_json`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AppSettings.cs AppSettings.Tests.cs
git commit -m "Store the Pro license key and first-use date in settings"
```

---

### Task 5: KeyGen tool + embedded public key

**Files:**
- Create: `KeyGen/KeyGen.csproj`
- Create: `KeyGen/Program.cs`
- Create: `KeyGen/make-lifetime.cmd`, `KeyGen/make-month.cmd`, `KeyGen/make-weekend.cmd`, `KeyGen/make-day.cmd`
- Create: `LicenseKeys.cs` (app root)
- Modify: `JinxyClicker.csproj` (exclude `KeyGen/**`)
- Modify: `.gitignore` (ignore the signing-key folder if it is ever placed in-repo; belt and braces)

**Interfaces:**
- Consumes: `LicenseKeyCodec.Create`, `LicensePayload`, `ProTier`.
- Produces: `LicenseKeys.PublicKey()` → `ECDsa`; `LicenseKeys.Blocklist` → `IReadOnlySet<ulong>`.

- [ ] **Step 1: Keep KeyGen out of the app build**

In `JinxyClicker.csproj`, in the `<ItemGroup>` that already has `<Compile Remove="Testing/**" />`, add:

```xml
    <Compile Remove="KeyGen/**" />
    <None Remove="KeyGen/**" />
```

- [ ] **Step 2: Create the KeyGen project**

Create `KeyGen/KeyGen.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Program.cs" />
    <Compile Include="../LicenseKeyCodec.cs" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write the tool**

Create `KeyGen/Program.cs`:

```csharp
using System;
using System.IO;
using System.Security.Cryptography;
using JinxyClicker;

// Where the private signing key and the sales log live: outside the repo, in the
// user's own AppData, so they can never be committed or shipped by accident.
string dir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JinxyKeyGen");
Directory.CreateDirectory(dir);
string keyFile = Path.Combine(dir, "signing-key.txt");
string logFile = Path.Combine(dir, "keys.csv");

string command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

switch (command)
{
    case "init":
        Init();
        break;
    case "make":
        Make(args.Length > 2 && args[1] == "--tier" ? args[2] : "");
        break;
    default:
        Console.WriteLine("Usage: keygen init   |   keygen make --tier (day|weekend|month|lifetime)");
        break;
}

void Init()
{
    if (File.Exists(keyFile))
    {
        Console.WriteLine($"A signing key already exists at {keyFile}. Delete it only if you mean to.");
        return;
    }
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    File.WriteAllText(keyFile, Convert.ToBase64String(key.ExportECPrivateKey()));
    string pub = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    Console.WriteLine("Signing key written to " + keyFile);
    Console.WriteLine("BACK THIS FILE UP somewhere safe. Never commit it, never share it.\n");
    Console.WriteLine("Paste this public key into LicenseKeys.cs (PublicKeyBase64):\n");
    Console.WriteLine(pub);
}

void Make(string tierName)
{
    if (!File.Exists(keyFile))
    {
        Console.WriteLine("No signing key yet. Run: keygen init");
        return;
    }
    if (!TryTier(tierName, out ProTier tier))
    {
        Console.WriteLine("Unknown tier. Use day, weekend, month, or lifetime.");
        return;
    }
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    key.ImportECPrivateKey(Convert.FromBase64String(File.ReadAllText(keyFile)), out _);

    ulong id = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));
    string keyString = LicenseKeyCodec.Create(new LicensePayload(tier, id), key);

    File.AppendAllText(logFile, $"{DateTime.UtcNow:o},{tier},{id},{keyString}\n");
    Console.WriteLine($"\n{tier} key (logged to {logFile}):\n");
    Console.WriteLine(keyString);
}

static bool TryTier(string name, out ProTier tier)
{
    tier = default;
    switch (name.ToLowerInvariant())
    {
        case "day": tier = ProTier.Day; return true;
        case "weekend": tier = ProTier.Weekend; return true;
        case "month": tier = ProTier.Month; return true;
        case "lifetime": tier = ProTier.Lifetime; return true;
        default: return false;
    }
}
```

- [ ] **Step 4: Create the double-click wrappers**

Create `KeyGen/make-lifetime.cmd`:

```bat
@echo off
dotnet run --project "%~dp0KeyGen.csproj" -- make --tier lifetime
pause
```

Create `make-month.cmd`, `make-weekend.cmd`, `make-day.cmd` the same way, changing the tier word. (A buyer-facing terminal is never needed — you double-click the one you sold.)

- [ ] **Step 5: Generate the real key pair**

Run once:

```bash
dotnet run --project KeyGen/KeyGen.csproj -- init
```

Copy the printed public key string for the next step. Confirm `signing-key.txt` exists under `%APPDATA%\JinxyKeyGen`.

- [ ] **Step 6: Create LicenseKeys.cs with the real public key**

Create `LicenseKeys.cs`, pasting the value printed in Step 5 into `PublicKeyBase64`:

```csharp
using System.Collections.Generic;
using System.Security.Cryptography;

namespace JinxyClicker;

/// <summary>
/// The public half of the signing key (safe to ship — it can only check keys,
/// not make them) and the list of key ids that have leaked and must be refused.
/// </summary>
public static class LicenseKeys
{
    // Printed by `keygen init`. Replace the whole string with that output.
    public const string PublicKeyBase64 = "PASTE_FROM_KEYGEN_INIT";

    public static ECDsa PublicKey()
    {
        var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(System.Convert.FromBase64String(PublicKeyBase64), out _);
        return key;
    }

    /// <summary>
    /// Key ids of leaked keys to refuse. Add an id here and ship an update to kill
    /// a key that has been posted publicly; look the id up in your KeyGen keys.csv.
    /// </summary>
    public static readonly IReadOnlySet<ulong> Blocklist = new HashSet<ulong>();
}
```

- [ ] **Step 7: Verify it all builds and the key works end to end**

Run:

```bash
dotnet run --project KeyGen/KeyGen.csproj -- make --tier lifetime
dotnet build JinxyClicker.csproj
```

Expected: a `JINXY-…` key prints, and the app builds with the real public key compiled in. (Full activation is wired in Task 6.)

- [ ] **Step 8: Commit**

```bash
git add KeyGen/KeyGen.csproj KeyGen/Program.cs KeyGen/make-lifetime.cmd KeyGen/make-month.cmd KeyGen/make-weekend.cmd KeyGen/make-day.cmd LicenseKeys.cs JinxyClicker.csproj .gitignore
git commit -m "Add KeyGen tool and embed the license public key"
```

---

### Task 6: Activation UI + startup wiring

**Files:**
- Create: `MainWindow.License.cs` (new partial — follows the existing `MainWindow.ServerRegion.cs` / `MainWindow.GamingMode.cs` pattern)
- Modify: `MainWindow.xaml` (a small Pro box on the Settings page)
- Modify: `MainWindow.xaml.cs` (call the new init in startup, where other `Load*` calls live)

**Interfaces:**
- Consumes: `License`, `LicenseKeys`, `LicenseStatus`, `AppSettings.LicenseKey`, `AppSettings.LicenseFirstUseUtc`.
- Produces: `bool MainWindow.IsPro` (a property the later feature-gating plan reads).

- [ ] **Step 1: Add the Pro box to the Settings page**

In `MainWindow.xaml`, inside `PageSettings`, add a titled section (match the surrounding card/section markup already used on that page):

```xml
<StackPanel x:Name="ProBox" Margin="0,16,0,0">
  <TextBlock Text="PRO" FontWeight="Bold"/>
  <TextBlock x:Name="ProStatusText" Text="Free version" Margin="0,4,0,8"/>
  <TextBox x:Name="ProKeyInput" Height="32"/>
  <Button x:Name="ProActivateButton" Content="Activate key" Margin="0,8,0,0"
          Click="ProActivate_Click"/>
  <TextBlock x:Name="ProActivateResult" Margin="0,8,0,0" TextWrapping="Wrap"/>
</StackPanel>
```

(Styling classes/brushes should match the other Settings sections; copy them from an existing block on that page rather than inventing new ones.)

- [ ] **Step 2: Write the partial with startup + activation**

Create `MainWindow.License.cs`:

```csharp
using System;
using JinxyClicker;

namespace JinxyClicker;

public partial class MainWindow
{
    private License? _license;

    /// <summary>True when a valid Pro key is active. Every Pro feature reads this.</summary>
    public bool IsPro { get; private set; }

    private void LoadLicense()
    {
        _license = new License(LicenseKeys.PublicKey(), LicenseKeys.Blocklist, () => DateTime.UtcNow);
        ApplyLicense(_settings.LicenseKey);
    }

    // Evaluates the key, stamps first-use for a pass if needed, updates IsPro and
    // the status line. Returns the status so the activation handler can message.
    private LicenseStatus ApplyLicense(string? key)
    {
        LicenseStatus status = _license!.Evaluate(key, _settings.LicenseFirstUseUtc);

        if (status.NeedsFirstUseStamp)
        {
            _settings.LicenseFirstUseUtc = DateTime.UtcNow;
            _settings.Save();
        }

        IsPro = status.IsPro;
        ProStatusText.Text = status switch
        {
            { IsPro: true, Tier: ProTier.Lifetime } => "Pro — lifetime",
            { IsPro: true, ExpiresUtc: { } e } => $"Pro — {status.Tier}, until {e.ToLocalTime():d}",
            _ => "Free version"
        };
        return status;
    }

    private void ProActivate_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        string key = ProKeyInput.Text?.Trim() ?? "";
        LicenseStatus status = ApplyLicense(key);

        if (status.IsPro)
        {
            _settings.LicenseKey = key;
            _settings.Save();
            ProActivateResult.Text = "Activated. Thank you!";
        }
        else
        {
            ProActivateResult.Text = "That key isn't valid.";
        }
    }
}
```

Note: this assumes the window holds its settings in a field named `_settings`. If the field has a different name, use that name instead — check the top of `MainWindow.xaml.cs`.

- [ ] **Step 3: Call LoadLicense at startup**

In `MainWindow.xaml.cs`, next to the other `Load*()` calls in the constructor / init path (e.g. near `LoadGamingMode()` / `LoadServerRegion()`), add:

```csharp
        LoadLicense();
```

- [ ] **Step 4: Build and smoke-test by hand**

Run: `dotnet build JinxyClicker.csproj`
Then run the app, open Settings:
- It shows "Free version".
- Paste a `JINXY-…` lifetime key made by KeyGen, click Activate → "Activated. Thank you!" and "Pro — lifetime".
- Paste `garbage`, click Activate → "That key isn't valid."
- Close and reopen the app → still "Pro — lifetime" (key persisted).

Expected: all four behaviors hold.

- [ ] **Step 5: Commit**

```bash
git add MainWindow.License.cs MainWindow.xaml MainWindow.xaml.cs
git commit -m "Add Pro activation box and wire the license at startup"
```

---

## What this plan deliberately leaves to later plans

- **Per-feature locked cards + the unlock bar** (the storefront UI across every feature). This plan ships only the activation box in Settings and the `IsPro` switch they'll read.
- **Clip Studio / auto-montage** (the hook) — its own spec and plan.
- **Timer overlay** — its own small spec and plan.
- **DPAPI-encrypting the signing key file** — a hardening follow-up; v1 stores it as a guarded file outside the repo.

## Self-review notes

- Spec coverage: key format (Tasks 1–2), three/four tiers + durations (Task 3 `DaysFor`), offline verification + `IsPro` gate (Tasks 3, 6), stored key + first-use (Task 4), KeyGen `init`/`make` + log + non-shipping (Task 5), blocklist (Tasks 3, 5), activation UI (Task 6). Pass/lifetime distinction and expiry covered in Task 3 tests.
- Review Focus inputs each have a test: malformed/garbage (Task 2 theory + Task 3), tampering (Task 2), expired pass (Task 3), blocklisted (Task 3), empty/whitespace (Task 3).
- Type consistency: `ProTier`, `LicensePayload`, `LicenseStatus`, `License.Evaluate`, `LicenseKeyCodec.Create/Verify` names are used identically across tasks.
