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
    case "verify":
        Verify(args.Length > 1 ? args[1] : "");
        break;
    default:
        Console.WriteLine("Usage: keygen init   |   keygen make --tier (day|weekend|month|lifetime)   |   keygen verify <key>");
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

// Checks a key against the SAME public key the shipped app carries, so you can
// confirm a key you are about to send will actually unlock Pro for the buyer.
void Verify(string key)
{
    using var pub = LicenseKeys.PublicKey();
    LicensePayload? p = LicenseKeyCodec.Verify(key, pub);
    if (p is { } payload)
        Console.WriteLine($"VALID — tier {payload.Tier}, id {payload.KeyId}");
    else
        Console.WriteLine("INVALID — the shipped app would reject this key.");
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
