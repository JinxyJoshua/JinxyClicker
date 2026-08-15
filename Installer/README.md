# Building the installer

Produces `dist\JinxyClicker-Setup-<version>.exe` — a per-user installer that
bundles the .NET runtime and ffmpeg, so the machine it lands on needs nothing
preinstalled.

## Prerequisites

- [Inno Setup 6](https://jrsoftware.org/isinfo.php) — `winget install JRSoftware.InnoSetup`
- ffmpeg, placed at `ffmpeg\ffmpeg.exe` beside the project file

`ffmpeg\` is gitignored. The binary is 213 MB, which is over twice GitHub's
100 MB hard limit for a single file, so it cannot live in the repository and has
to be fetched. Any recent Windows build with libx264 works:

```
winget install Gyan.FFmpeg
```

then copy `ffmpeg.exe` out of the winget package folder into `ffmpeg\`.

## Build

```
dotnet publish JinxyClicker.csproj -c Release -r win-x64 --self-contained true
"%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" Installer\JinxyClicker.iss
```

The publish step is not optional and not implied by the second — `JinxyClicker.iss`
copies whatever is sitting in the publish folder, so a stale publish silently
ships a stale build.

## Decisions worth knowing

**Per-user install, not Program Files.** No UAC prompt, and nothing the app does
needs machine-wide scope. Settings live in `%APPDATA%\JinxyClicker`, so the
install location does not decide whether they can be written — this was a hard
constraint before that changed, and is now just a preference.

**Uninstall removes `%APPDATA%\JinxyClicker`.** Setup never installed those
files, so without an explicit rule the folder would survive uninstalling.

**ffmpeg is GPL.** The build bundled here includes libx264, which makes the
whole ffmpeg binary GPL-licensed. Distributing this installer therefore
distributes GPL software, and recipients are entitled to the corresponding
source. `THIRD-PARTY-NOTICES.txt` is shown during setup and installed alongside
the app; the ffmpeg licence text is installed to `ffmpeg\LICENSE`.

**`AppId` must never change.** It is how Windows recognises an install as the
same product. Changing it turns the next upgrade into a second parallel copy
sitting beside the first.

**Uninstall removes generated settings and the replay buffer, but not clips.**
Saved recordings in `Videos\JinxyClicker` are content the user chose to create
and may not have copied anywhere else.
