; Jinxy Clicker installer.
;
; Build with:  ISCC.exe Installer\JinxyClicker.iss
; Requires a self-contained publish to exist first:
;   dotnet publish JinxyClicker.csproj -c Release -r win-x64 --self-contained true
; and ffmpeg.exe to be present in the ffmpeg folder beside the project.

#define AppName        "Jinxy AutoClicker Beta"
#define AppVersion     "1.4.15"
#define AppPublisher   "JinxyJoshua"
#define AppExeName     "JinxyClicker.exe"
#define FfmpegDir      "..\ffmpeg"

; Two installers come out of this one script. Compiling with /DDEV=1 packages
; the developer build instead of the public one - a different publish folder and
; a different output name, so the two cannot be confused on disk or uploaded to
; the wrong repository.
;
; The AppId is deliberately the SAME for both. They are one product, so
; installing either replaces the other rather than leaving two copies fighting
; over the same settings folder. A dev user stays on dev because a dev build
; updates itself from the private repository and never from public releases.
#ifdef DEV
  #define PublishDir   "..\bin\DevBuild\Release\net10.0-windows\win-x64\publish"
  #define OutputName   "JinxyAutoClicker-DEV-Setup-" + AppVersion
  ; The dev update token, read from the build machine's own settings folder at
  ; compile time. Only the dev installer carries it, so it never reaches a public
  ; build; see the [Files] note below for why it is packaged at all.
  #define DevTokenSource GetEnv("APPDATA") + "\JinxyClicker\dev-update.token"
#else
  #define PublishDir   "..\bin\Release\net10.0-windows\win-x64\publish"
  #define OutputName   "JinxyAutoClicker-Beta-Setup-" + AppVersion
#endif

[Setup]
; Never change AppId. It is how Windows recognises an existing install as the
; same product, and changing it turns upgrades into a second parallel copy.
AppId={{7D4E1C9A-3B62-4F18-9E5D-2A6C8F0B4173}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=..\dist
OutputBaseFilename={#OutputName}
SetupIconFile=..\icon.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Per-user install: no UAC prompt, and nothing here needs machine-wide scope.
; Settings live under %APPDATA%, so the install location no longer decides
; whether the app can write them.
PrivilegesRequired=lowest

InfoBeforeFile=THIRD-PARTY-NOTICES.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
; The kit art rides along inside the publish output, as {app}\kits. The project
; copies it there on publish, so there is deliberately no separate line for it —
; one that listed the files again could fall out of step with the roster.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#FfmpegDir}\ffmpeg.exe"; DestDir: "{app}\ffmpeg"; Flags: ignoreversion
Source: "{#FfmpegDir}\LICENSE"; DestDir: "{app}\ffmpeg"; Flags: ignoreversion skipifsourcedoesntexist
Source: "THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

; The dev update token, for the DEV installer only. It is placed into the
; settings folder so a dev build on any machine can update itself without the
; token being put there by hand first - which is the whole point of handing
; someone a dev build. It travels inside every dev build, so it is only as
; private as the people those builds are given to; that is an accepted trade for
; auto-update working everywhere, and it is why the token is fine-grained,
; read-only, and scoped to the dev repository alone - a leaked one lets someone
; download the dev installer they already have and nothing more. The public
; installer never sees this line.
#ifdef DEV
Source: "{#DevTokenSource}"; DestDir: "{userappdata}\JinxyClicker"; \
    Flags: ignoreversion skipifsourcedoesntexist
#endif

[Icons]
; WorkingDir is load-bearing, not decoration. The settings files resolve against
; the working directory, so a shortcut without it would have the app reading and
; writing them wherever the shell happened to start it.
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Settings, presets, bindings and history. Generated at runtime, so Setup never
; installed them and would otherwise leave the whole folder behind.
Type: filesandordirs; Name: "{userappdata}\JinxyClicker"

; The rolling replay buffer. Bounded, but it is still up to a minute of video
; that nobody asked to keep.
Type: filesandordirs; Name: "{localappdata}\Temp\JinxyClicker"

; Saved clips in Videos\JinxyClicker are deliberately NOT removed. Those are
; recordings the user chose to make and may not have copied anywhere else —
; an uninstaller is not entitled to them.
