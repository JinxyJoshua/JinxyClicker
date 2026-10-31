using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace JinxyClicker;

/// <summary>
/// Everything on the Clicker page that the user tunes, so a session starts where
/// the last one left off. Hotkeys and presets have their own files; this covers
/// the sliders, the direction fields and the toggles.
/// </summary>
public sealed class AppSettings
{
    private static readonly string SETTINGS_FILE = SettingsPath.For("app_settings.json");

    public double Cps { get; set; } = 10;
    public double Cdc { get; set; } = 67;

    // Tuned by hand against a live client and kept. The old 8/20/40/8 was a
    // guess with a 40px vertical throw that read as a flick rather than a shake.
    public double ShakeLeft { get; set; } = 7;
    public double ShakeRight { get; set; } = 9;
    public double ShakeUp { get; set; } = 6;
    public double ShakeDown { get; set; } = 5;

    /// <summary>A snapshot the user can return to after experimenting.</summary>
    public bool HasSavedShake { get; set; }
    public double SavedShakeLeft { get; set; }
    public double SavedShakeRight { get; set; }
    public double SavedShakeUp { get; set; }
    public double SavedShakeDown { get; set; }

    /// <summary>Shake movements per second.</summary>
    public double ShakeSpeed { get; set; } = 50;

    /// <summary>
    /// Shake smoothness, 0–100: how much the shake glides toward each random
    /// point instead of jumping to it. 0 is the original sharp jitter.
    /// </summary>
    public double ShakeSmoothness { get; set; } = 60;

    public bool ShakyTracking { get; set; }
    public bool UltraAccuracy { get; set; }
    public bool PingSync { get; set; }
    public bool HitFix { get; set; } = true;

    /// <summary>
    /// Plus: lock the click rate to the measured-best hit-registration ceiling.
    /// </summary>
    /// <remarks>
    /// Only honoured while Plus is active — the gate forces it off the moment a
    /// key lapses, so a file that carries it on from a lapsed pass does not keep
    /// a paid feature running.
    /// </remarks>
    public bool AutoTune { get; set; }

    /// <summary>Plus: raise the sword block between hits (blockhit assist).</summary>
    public bool BlockHit { get; set; }

    /// <summary>Plus: id of the combat profile last applied, for the lit button. Null for none.</summary>
    public string? CombatProfile { get; set; }

    /// <summary>Whether the taskbar and the top of the screen also stop the clicker.</summary>
    /// <remarks>
    /// Off by default, and deliberately separate from the corners, which are
    /// always on. This zone is crossed in ordinary play — the top edge is where
    /// tabs, menus and title bars live — so it fires by accident in a way a
    /// corner does not, and that has to be opted into.
    /// </remarks>
    public bool StopAtTaskbar { get; set; }

    /// <summary>
    /// App targeting for the clicker: 0 Off, 1 Whitelist (click only over the
    /// listed apps), 2 Blacklist (click everywhere except the listed apps).
    /// </summary>
    /// <remarks>
    /// Both lists are kept at once and remembered independently, so switching
    /// Mode never loses the other list — the Mode just selects which one is in
    /// force. Entries are executable file names such as RobloxPlayerBeta.exe.
    /// </remarks>
    public int ClickTargetMode { get; set; }
    public List<string> ClickWhitelist { get; set; } = new();
    public List<string> ClickBlacklist { get; set; } = new();

    /// <summary>
    /// Humanize amount, 0–100: how much the gap between clicks is randomised. 0
    /// (the default) is the old perfectly-even spacing.
    /// </summary>
    public double ClickHumanize { get; set; }

    public bool HoldMode { get; set; }

    /// <summary>Which button is clicked: Left, Right or Middle.</summary>
    /// <remarks>
    /// Stored by name rather than as a number, so the file stays readable and a
    /// reordered enum cannot silently change what an existing setting means.
    /// </remarks>
    public string ClickButton { get; set; } = "Left";

    public bool HideValues { get; set; }

    /// <summary>
    /// Hides the on-screen "macro on" badge from screen capture.
    /// </summary>
    /// <remarks>
    /// Off by default, so the badge behaves like any other window and shows up
    /// in a recording. Turning it on hides it from OBS, Discord and the rest
    /// while leaving it visible on the monitor — the warning is for the person
    /// playing, and they still need it while streaming.
    /// </remarks>
    public bool StreamerMode { get; set; }

    /// <summary>
    /// Whether the live readout is shown over the game while clicking.
    /// </summary>
    /// <remarks>
    /// Off until it is asked for. It shipped on, which meant it appeared over
    /// everyone's game the first time they clicked without anyone having chosen
    /// it — and something drawn on top of a game people are trying to play has
    /// to be opted into rather than opted out of.
    ///
    /// Deliberately a different name from the "ShowOverlay" this replaces.
    /// Settings are stored as the whole object by property name, so every file
    /// written before this carries ShowOverlay: true — not because anybody
    /// turned it on, but because that was the default. Reading the old name
    /// would leave it on for exactly the people it is being turned off for. The
    /// old key is ignored, so everyone starts from off and whoever wants it
    /// turns it on once.
    /// </remarks>
    public bool OverlayOn { get; set; }

    /// <summary>
    /// The crosshair: which gallery entry was last chosen, and its size.
    /// </summary>
    /// <remarks>
    /// The crosshair is written into Roblox's own cursor files rather than drawn
    /// as an overlay, so nothing here switches it on — that is what the Apply
    /// button does, and whether it is applied is read back from the files on
    /// disk. This only remembers the choice so the page opens where it was left.
    /// The name is stored rather than an index so reordering the gallery cannot
    /// silently change which crosshair a settings file means.
    /// </remarks>
    public string CrosshairName { get; set; } = "Green Cross";

    /// <summary>
    /// Whether a crosshair is meant to be on Roblox's cursor right now.
    /// </summary>
    /// <remarks>
    /// Kept so the app can put the crosshair back after a Roblox update, which
    /// installs a fresh version folder with the default cursor and would otherwise
    /// silently lose it — the usual "it just stopped working" report. The on-disk
    /// backups can't answer this on their own: an old version folder left behind by
    /// an update still carries a backup, so they say "applied" while the live cursor
    /// is already back to default.
    /// </remarks>
    public bool CrosshairApplied { get; set; }

    /// <summary>
    /// The display name of the imported custom Roblox font (its original file
    /// name), or empty when none has been imported. The font's bytes live at a
    /// fixed path in the app's data folder, so only the name is stored here.
    /// </summary>
    public string CustomFontName { get; set; } = "";

    /// <summary>
    /// Whether the custom font is meant to be applied to Roblox right now. Kept
    /// for the same reason as <see cref="CrosshairApplied"/>: a Roblox update lays
    /// down a fresh version folder with the stock fonts, and this is what tells the
    /// app to re-apply the custom font on the next launch.
    /// </summary>
    public bool CustomFontApplied { get; set; }

    /// <summary>
    /// The user's custom sidebar order (groups and the items inside them), as JSON.
    /// Null means the default order. Reconciled against the live sidebar on load so
    /// new tabs appear and removed ones drop out — see <see cref="JinxyClicker.NavLayout"/>.
    /// </summary>
    public string? NavLayout { get; set; }

    /// <summary>
    /// The size, as a percentage, kept per crosshair by name.
    /// </summary>
    /// <remarks>
    /// Each crosshair remembers its own size, so turning one up does not change
    /// the others — a dot and a sniper cross want very different sizes. A name
    /// missing from the map just means it has never been resized and uses 100%.
    /// </remarks>
    public Dictionary<string, int> CrosshairSizes { get; set; } = new();

    /// <summary>Crosshairs the user built themselves, shown after the ready-made ones.</summary>
    public List<CustomCrosshair> CustomCrosshairs { get; set; } = new();

    /// <summary>
    /// Whether the server-region banner is shown over the game on joining.
    /// </summary>
    /// <remarks>
    /// Off until asked for, like the click readout — it draws over a game people
    /// are playing, so it has to be opted into. It reads only Roblox's own log,
    /// never the game, and shows a region, never a ping (which cannot be measured
    /// from outside the game). See <see cref="ServerOverlay"/>.
    /// </remarks>
    public bool ServerRegionOverlayOn { get; set; }

    /// <summary>
    /// How long the server banner stays on screen after a join, in seconds.
    /// Zero means it stays up until the server changes or Roblox closes.
    /// </summary>
    public int ServerOverlaySeconds { get; set; }

    /// <summary>
    /// Gaming Mode: whether it is on, and exactly what it changed, so turning it
    /// off reverts only its own doing.
    /// </summary>
    /// <remarks>
    /// The list holds the ids of the tweaks the switch itself applied — not the
    /// ones the user had already turned on by hand, which it must leave untouched.
    /// The FPS cap and priority are tracked the same way: a flag for whether the
    /// switch changed each, and the previous FPS value to put back. All of it is
    /// restorable; none of it is a one-way change.
    /// </remarks>
    public bool GamingModeOn { get; set; }
    public List<string> GamingModeAppliedTweaks { get; set; } = new();

    /// <summary>
    /// Graphics Quality slider position, 1 (max FPS) to 10 (full quality). 10 is
    /// the default: no overrides, Roblox renders normally.
    /// </summary>
    public int GraphicsQualityLevel { get; set; } = 10;
    public bool GamingModeSetFps { get; set; }
    public int? GamingModePrevFpsCap { get; set; }
    public bool GamingModeSetPriority { get; set; }

    /// <summary>
    /// Plus: Lower Ping — whether the one-switch network bundle is on, and exactly
    /// which tweaks it applied, so turning it off reverts only its own doing (the
    /// same reversible contract as Gaming Mode).
    /// </summary>
    public bool LowerPingOn { get; set; }
    public List<string> LowerPingAppliedTweaks { get; set; } = new();

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

    /// <summary>Clip Studio: export montages vertically (9:16) for TikTok/Shorts.</summary>
    public bool MontageVertical { get; set; }

    /// <summary>
    /// Clip Studio: the chosen montage music — a bundled track's file name, or a
    /// full path to the user's own file. Null uses the first bundled track.
    /// </summary>
    public string? MontageMusic { get; set; }

    /// <summary>Clip Studio (Pro): the intro title-card text, or null/empty for no intro.</summary>
    public string? MontageTitle { get; set; }

    /// <summary>Clip Studio (Pro): crossfade clips into each other instead of hard cuts.</summary>
    public bool MontageCrossfade { get; set; }

    /// <summary>Pro: saved whole-app setups (crosshair + click preset + flags + theme).</summary>
    public List<SetupProfile> SetupProfiles { get; set; } = new();

    /// <summary>Pro: the name of the setup last applied, if any.</summary>
    public string? ActiveSetup { get; set; }

    /// <summary>Pro: the game link the Play button opens, or null for Roblox's games page.</summary>
    public string? PlayGameUrl { get; set; }

    /// <summary>Pro: SOCD cleaner on.</summary>
    public bool SocdEnabled { get; set; }

    /// <summary>Pro: SOCD profile — "WASD" (cleans A/D and W/S) or "Custom".</summary>
    public string? SocdProfile { get; set; } = "Custom";

    /// <summary>Pro: the two opposing SOCD keys for the Custom profile (single characters).</summary>
    public string? SocdLeft { get; set; } = "A";
    public string? SocdRight { get; set; } = "D";

    /// <summary>Plus: display switcher — apply a resolution/refresh while Roblox is open.</summary>
    public bool DisplayEnabled { get; set; }
    public int DisplayWidth { get; set; }
    public int DisplayHeight { get; set; }
    public int DisplayRefresh { get; set; }

    /// <summary>Plus: Screen Tint — a translucent colour veil over the screen.</summary>
    public bool TintEnabled { get; set; }
    public string TintPreset { get; set; } = "warm";
    public double TintIntensity { get; set; } = 50;

    /// <summary>Screen Tint vignette strength, 0–100 (0 = no vignette).</summary>
    public double TintVignette { get; set; }

    /// <summary>The id of the selected Look card, or null once the sliders are nudged off it.</summary>
    public string? TintLook { get; set; }
    public bool TintOnlyRoblox { get; set; } = true;

    /// <summary>Overlays page: which of the on-screen pills are switched on.</summary>
    public bool OverlayFps { get; set; }
    public bool OverlaySystem { get; set; }
    public bool OverlayMusic { get; set; }
    public bool OverlayMouse { get; set; }
    public bool OverlayClock { get; set; }
    public bool OverlayWatermark { get; set; }
    public string OverlayWatermarkText { get; set; } = "Jinxy";

    /// <summary>Score overlay: follows a real game's live score (ESPN). League name and
    /// the followed game's id; empty id means nothing is being followed yet.</summary>
    public bool OverlayScore { get; set; }
    public string OverlayScoreLeague { get; set; } = "NFL";
    public string OverlayScoreGameId { get; set; } = "";

    /// <summary>True = overlays show over any window; false = only while Roblox is in front.</summary>
    public bool OverlayShowAlways { get; set; }

    /// <summary>Dragged positions for the movable overlays; null means its default corner.</summary>
    public double? OverlayCpsX { get; set; }
    public double? OverlayCpsY { get; set; }
    public double? OverlayFpsX { get; set; }
    public double? OverlayFpsY { get; set; }
    public double? OverlaySystemX { get; set; }
    public double? OverlaySystemY { get; set; }
    public double? OverlayMusicX { get; set; }
    public double? OverlayMusicY { get; set; }
    public double? OverlayMouseX { get; set; }
    public double? OverlayMouseY { get; set; }
    public double? OverlayClockX { get; set; }
    public double? OverlayClockY { get; set; }
    public double? OverlayWatermarkX { get; set; }
    public double? OverlayWatermarkY { get; set; }
    public double? OverlayScoreX { get; set; }
    public double? OverlayScoreY { get; set; }

    /// <summary>Pro: the on-screen keystrokes + CPS overlay.</summary>
    public bool KeystrokesOverlay { get; set; }

    /// <summary>Pro: saved overlay position (screen pixels), or null for the default spot.</summary>
    public double? KeystrokesX { get; set; }
    public double? KeystrokesY { get; set; }

    /// <summary>Pro: draw the overlay's panel behind the keys, or leave just the keys
    /// floating (Medal-style) when false. Defaults to the panel look.</summary>
    public bool KeystrokesBackground { get; set; } = true;

    /// <summary>Plus: show Roblox's in-game FPS on the overlay (needs admin).</summary>
    public bool KeystrokesFps { get; set; }

    public bool ReplayEnabled { get; set; }
    public int ReplaySeconds { get; set; } = 30;

    /// <summary>The accent colour, as hex. Must match one of the Theme swatches
    /// to be restored; anything else falls back to the default red.</summary>
    public string AccentColor { get; set; } = "#FF4B52";

    /// <summary>Window opacity, 0.4 to 1. Clamped to the slider on load, so a
    /// hand-edited zero cannot produce a window nobody can see.</summary>
    public double WindowOpacity { get; set; } = 1.0;

    /// <summary>Light mode. Dark is the default and the designed appearance.</summary>
    public bool LightTheme { get; set; }

    /// <summary>Bare file name of the wallpaper copy, or empty for none.</summary>
    /// <remarks>
    /// A name rather than a path, because the picture is copied into the
    /// settings folder and the original is not depended on afterwards.
    /// </remarks>
    public string WallpaperFile { get; set; } = "";

    /// <summary>How far the wallpaper is darkened, as a percentage.</summary>
    public int WallpaperDimming { get; set; } = Wallpaper.DefaultDimming;

    /// <summary>Where clips are written. Null or empty means the Videos folder.</summary>
    public string? ClipFolder { get; set; }

    /// <summary>Clip feedback: the on-screen note and the sound played when a clip saves.</summary>
    public bool ClipNoteEnabled { get; set; } = true;
    public string ClipNoteText { get; set; } = "Clip saved";
    /// <summary>0 = top-left, 1 = top-right, 2 = bottom-left, 3 = bottom-right.</summary>
    public int ClipNoteCorner { get; set; } = 1;
    public bool ClipSoundEnabled { get; set; } = true;
    /// <summary>"chime", "pop", "shutter" or "own".</summary>
    public string ClipSound { get; set; } = "chime";
    public string? ClipSoundFile { get; set; }
    public double ClipSoundVolume { get; set; } = 80;

    /// <summary>Capture picture quality: "light", "balanced" or "high" (a bitrate tilt).</summary>
    public string RecordPictureQuality { get; set; } = "balanced";
    /// <summary>Save clips into a per-app subfolder.</summary>
    public bool RecordSortByApp { get; set; }

    /// <summary>
    /// Whether the auto switcher may run at all.
    /// </summary>
    /// <remarks>
    /// Separate from being switched on: disabled means its hotkey and its own
    /// toggle both refuse it, and it stays that way across restarts. Stored as
    /// "disabled" so a settings file written before this shipped — where the
    /// value is missing and reads as false — leaves it enabled.
    /// </remarks>
    public bool SwitcherDisabled { get; set; }

    /// <summary>
    /// The two hotbar slots the auto switcher swaps between, and its timing.
    /// </summary>
    /// <remarks>
    /// These are the numbers the app's author actually plays on rather than
    /// round figures picked to look reasonable. The equip delay in particular
    /// was tuned against the hit counter, and the first hold is deliberately
    /// short because the engine raises it to whatever still lands a click —
    /// 5 ms of equip plus two click periods, which at the measured rate is
    /// 65 ms. A default nobody has played on is a worse starting point than one
    /// somebody has.
    /// </remarks>
    public string SwitcherSlotA { get; set; } = "4";
    public string SwitcherSlotB { get; set; } = "1";
    public int SwitcherIntervalMs { get; set; } = 21;

    /// <summary>How long the second slot is held.</summary>
    public int SwitcherIntervalBMs { get; set; } = 1300;

    /// <summary>How long the game takes to equip a weapon, in the user's judgement.</summary>
    public int SwitcherEquipMs { get; set; } = 5;

    /// <summary>Capture framerate for both the recorder and the replay buffer.</summary>
    public int RecordFps { get; set; } = 30;

    /// <summary>Keeps the Roblox process above normal priority while on.</summary>
    public bool RobloxPriority { get; set; }

    /// <summary>Master switch for all four hotkeys. On by default — off is a
    /// deliberate act, not a state to wake up in and be confused by.</summary>
    public bool HotkeysEnabled { get; set; } = true;

    /// <summary>Device name of the monitor to capture, or null for all of them.
    /// Stored by name rather than index so unplugging a screen cannot silently
    /// repoint the recording at a different one.</summary>
    /// <summary>
    /// The monitor to capture: a device name, <see cref="AllDisplays"/>, or null
    /// for a setting nobody has chosen yet.
    /// </summary>
    /// <remarks>
    /// Null used to mean "the whole desktop", which made the whole desktop the
    /// default. On two monitors that captures both — a 3840x1080 frame with the
    /// game in half of it, at double the pixels to encode. It is the reason
    /// clips were expensive to record and hard to see anything in.
    ///
    /// So null now means unset and resolves to the primary monitor, and an
    /// explicit choice of every display is stored as <see cref="AllDisplays"/>.
    /// Files written before this carry null and are read as unset, which moves
    /// existing users onto the primary monitor — the point of the fix, and one
    /// button away from the old behaviour if they wanted it.
    /// </remarks>
    public string? RecordDisplay { get; set; }

    /// <summary>Stored in <see cref="RecordDisplay"/> for a deliberate "all displays".</summary>
    public const string AllDisplays = "all";

    /// <summary>
    /// Null when nothing has been stored yet, so a first run uses the designed
    /// size rather than a zero-sized window.
    /// </summary>
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public bool WindowMaximized { get; set; }

    /// <summary>A missing or unreadable file is not an error — it just means defaults.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SETTINGS_FILE)) return FreshInstall();

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SETTINGS_FILE))
                   ?? new AppSettings();
        }
        catch
        {
            // Unreadable is not fresh — leave the wallpaper alone rather than
            // installing the default on top of whatever the user picked.
            return new AppSettings();
        }
    }

    /// <summary>
    /// The first-run defaults, which include installing the shipped wallpaper.
    /// </summary>
    /// <remarks>
    /// Kept separate from the plain constructor so anyone constructing an
    /// AppSettings for tests or migrations does not accidentally trigger a
    /// disk write to the settings folder.
    /// </remarks>
    private static AppSettings FreshInstall() => new()
    {
        WallpaperFile = Wallpaper.InstallDefault()
    };

    public void Save()
    {
        try
        {
            File.WriteAllText(SETTINGS_FILE,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
