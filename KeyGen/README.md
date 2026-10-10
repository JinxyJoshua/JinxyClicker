# KeyGen — how you make keys to sell

This is your private key machine. It never ships with the app. Keep it to yourself.

## The easy way (the KeyGen app)

Double-click **"Jinxy KeyGen"** on your Desktop. A small window opens with four
buttons:

- **Lifetime $20**
- **Month $8**
- **Weekend $4**
- **Day $2**

Click the one the buyer paid for. The key shows up in the box **and is copied to
your clipboard automatically** — just paste it to the buyer. That's it.

The buyer pastes it into the app's PRO page and clicks Activate.

> The app lives at `C:\Users\rschi\JinxyKeyGen\JinxyKeyGen.exe` (the Desktop
> shortcut points there). To rebuild it after code changes, run:
> `dotnet publish KeyGen/KeyGen.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o "C:\Users\rschi\JinxyKeyGen"`

## Where your private stuff lives

Everything private is in `%APPDATA%\JinxyKeyGen` (paste that into the File
Explorer address bar):

- `signing-key.txt` — the secret that makes keys. **Back this up. Never share it,
  never put it online, never send it to anyone.** If you lose it you can't make
  keys that work with the shipped app.
- `keys.csv` — a log of every key you made (date, tier, id, key). This is your
  sales record. If a key leaks, find its id here so it can be blocked in the next
  app update.

## First-time only (already done)

The signing key pair was created once. You don't redo it. The matching public key
is already baked into the app (`LicenseKeys.cs`).

## Running from source instead (optional)

If you ever need to make a key without the built app, from the project folder:

```
dotnet run --project KeyGen/KeyGen.csproj
```

That opens the same window.
