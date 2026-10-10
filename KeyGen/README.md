# KeyGen — how you make keys to sell

This is your private key machine. It never ships with the app. Keep this folder
to yourself.

## Make a key (the everyday thing)

Double-click the file for the tier someone paid for:

- `make-lifetime.cmd`  → a forever key ($20)
- `make-month.cmd`     → a 30-day pass ($8)
- `make-weekend.cmd`   → a 3-day pass ($4)
- `make-day.cmd`       → a 1-day pass ($2)

A black window opens and prints a key like `JINXY-042DW-JPWKQ-…`. Select the whole
key, copy it, and send it to the buyer. Press a key to close the window.

That's it. The buyer pastes it into the app's PRO page and clicks Activate.

## Where your stuff lives

Everything private is in `%APPDATA%\JinxyKeyGen` (type that into the File Explorer
address bar):

- `signing-key.txt` — the secret that makes keys. **Back this up. Never share it,
  never put it online, never send it to anyone.** If you lose it you can't make
  new keys that work with the shipped app.
- `keys.csv` — a log of every key you made (date, tier, id, key). This is your
  sales record. If a key gets leaked/shared publicly, find its id here so it can
  be blocked in the next app update.

## First-time only (already done)

`keygen init` created the key pair once. You don't run it again. The matching
public key is already baked into the app (`LicenseKeys.cs`).

## Check a key before sending (optional)

Open a terminal in this folder and run:

```
dotnet run --project KeyGen.csproj -- verify PASTE_THE_KEY_HERE
```

It says VALID or INVALID — a quick way to confirm a key will work before you send
it.
