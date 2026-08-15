; Jinxy Clicker installer.
;
; Build with:  ISCC.exe Installer\JinxyClicker.iss
; Requires a self-contained publish to exist first:
;   dotnet publish JinxyClicker.csproj -c Release -r win-x64 --self-contained true
; and ffmpeg.exe to be present in the ffmpeg folder beside the project.

#define AppName        "Jinxy Clicker"
#define AppVersion     "1.0.0"
#define AppPublisher   "JinxyJoshua"
#define AppExeName     "JinxyClicker.exe"
#define PublishDir     "..\bin\Release\net10.0-windows\win-x64\publish"
#define FfmpegDir      "..\ffmpeg"

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
OutputBaseFilename=JinxyClicker-Setup-{#AppVersion}
SetupIconFile=..\icon.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Per-user install, and deliberately so. The app resolves its settings files
; against the working directory, so an install under Program Files would leave
; it unable to write them — it would start from defaults every launch and
; silently discard every change. Installing per-user also means no UAC prompt.
PrivilegesRequired=lowest

InfoBeforeFile=THIRD-PARTY-NOTICES.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#FfmpegDir}\ffmpeg.exe"; DestDir: "{app}\ffmpeg"; Flags: ignoreversion
Source: "{#FfmpegDir}\LICENSE"; DestDir: "{app}\ffmpeg"; Flags: ignoreversion skipifsourcedoesntexist
Source: "THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

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
; Generated at runtime, so Setup did not install them and would otherwise leave
; them behind along with the folder holding them.
Type: files; Name: "{app}\app_settings.json"
Type: files; Name: "{app}\click_presets.json"
Type: files; Name: "{app}\hotkey_settings.json"
Type: files; Name: "{app}\tweak_state.json"
Type: files; Name: "{app}\history.json"

; The rolling replay buffer. Bounded, but it is still up to a minute of video
; that nobody asked to keep.
Type: filesandordirs; Name: "{localappdata}\Temp\JinxyClicker"

; Saved clips in Videos\JinxyClicker are deliberately NOT removed. Those are
; recordings the user chose to make and may not have copied anywhere else —
; an uninstaller is not entitled to them.
