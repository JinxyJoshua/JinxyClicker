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
