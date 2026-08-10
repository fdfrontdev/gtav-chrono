; CHRONO · FIRDAUS BUILDS — GTA V one-click installer (S22 v8 r2)
; Compile: ISCC.exe ChronoInstaller.iss
; Output: dist/Chrono-Setup-<ver>.exe
;
; ONE-CLICK for everyone: detects GTA V automatically, installs the mod,
; and AUTO-INSTALLS missing dependencies from BUNDLED copies (no stale
; download links — the user asked for self-contained installs).
#define AppName "CHRONO — Firdaus Builds"
#define AppVersion "1.0.0"
#define AppPublisher "Firdaus Builds"
#define AppURL "https://github.com/fdfrontdev"

[Setup]
AppId={{8E2F7C1A-4B3D-4C5E-9F0A-1C2B3D4E5F60}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
DefaultDirName={code:FindGtaDir}
DefaultGroupName=CHRONO
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=Chrono-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayName=CHRONO — Firdaus Builds
UninstallDisplayIcon={app}\Chrono\Chrono.dll

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
; ── The mod itself (source: dist/release — kept by bundle.sh for the installer) ──
Source: "..\dist\release\Chrono\*"; DestDir: "{app}\scripts\Chrono"; Flags: recursesubdirs createallsubdirs
; ── Bundled dependencies (auto-install ONLY if missing — never overwrite) ──
; ScriptHookV + SHVDN ship INSIDE the installer so players never need to
; hunt for downloads (links go stale; bundled copies are versioned here).
; Check: DepMissing — a player who already has a (possibly newer) copy keeps it.
Source: "..\dist\deps\ScriptHookV.dll"; DestDir: "{app}"; Flags: skipifsourcedoesntexist; Check: DepMissing('ScriptHookV.dll')
Source: "..\dist\deps\ScriptHookVDotNet.asi"; DestDir: "{app}"; Flags: skipifsourcedoesntexist; Check: DepMissing('ScriptHookVDotNet.asi')
Source: "..\dist\deps\ScriptHookVDotNet.ini"; DestDir: "{app}"; Flags: skipifsourcedoesntexist; Check: DepMissing('ScriptHookVDotNet.ini')
Source: "..\dist\deps\ScriptHookVDotNet2.dll"; DestDir: "{app}"; Flags: skipifsourcedoesntexist; Check: DepMissing('ScriptHookVDotNet2.dll')
Source: "..\dist\deps\ScriptHookVDotNet3.dll"; DestDir: "{app}"; Flags: skipifsourcedoesntexist; Check: DepMissing('ScriptHookVDotNet3.dll')

[Icons]
Name: "{group}\CHRONO (open mod folder)"; Filename: "{app}\scripts\Chrono"
Name: "{group}\Firdaus Builds — YouTube"; Filename: "https://www.youtube.com/@firdausbuilds"
Name: "{group}\Uninstall CHRONO"; Filename: "{uninstallexe}"
Name: "{autodesktop}\CHRONO — Firdaus Builds"; Filename: "{app}\scripts\Chrono"; Tasks: desktopicon

[Run]
Filename: "{app}\scripts\Chrono\README.txt"; Description: "View the README"; Flags: postinstall shellexec skipifsilent

[Code]
// ── Dependency install guard: only install bundled deps if MISSING ──
function DepMissing(FileName: string): Boolean;
begin
  Result := not FileExists(AddBackslash(ExpandConstant('{app}')) + FileName);
end;

// ── GTA V install detection: registry + common paths ──
function FindGtaDir(Param: string): string;
var
  RegPaths: array of string;
  TryPath, SteamLib, CheckPath: string;
  I, J: Integer;
begin
  // Registry (Rockstar Launcher / Steam installs write this)
  RegPaths := ['SOFTWARE\Rockstar Games\Grand Theft Auto V',
               'SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V'];
  for I := 0 to GetArrayLength(RegPaths) - 1 do
  begin
    if RegQueryStringValue(HKLM, RegPaths[I], 'InstallFolder', TryPath) then
      if FileExists(AddBackslash(TryPath) + 'GTA5.exe') then
      begin
        Result := TryPath;
        Exit;
      end;
    if RegQueryStringValue(HKCU, RegPaths[I], 'InstallFolder', TryPath) then
      if FileExists(AddBackslash(TryPath) + 'GTA5.exe') then
      begin
        Result := TryPath;
        Exit;
      end;
  end;
  // Common Steam locations (libraryfolders.vdf → steamapps\common)
  TryPath := 'C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V';
  if FileExists(AddBackslash(TryPath) + 'GTA5.exe') then
  begin
    Result := TryPath;
    Exit;
  end;
  TryPath := 'D:\SteamLibrary\steamapps\common\Grand Theft Auto V';
  if FileExists(AddBackslash(TryPath) + 'GTA5.exe') then
  begin
    Result := TryPath;
    Exit;
  end;
  // Default fallback — let the user browse
  Result := 'C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V';
end;

// ── Welcome page branding ──
procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel2.Caption :=
    'CHRONO · FIRDAUS BUILDS' + #13#10 + #13#10 +
    'A GTA V superpower + justice system mod:' + #13#10 +
    'dash · time stop · invisibility · fly · god mode · arrests · courts · prison · manhunts' + #13#10 + #13#10 +
    'The installer finds your GTA V automatically and installs everything — ' +
    'including dependencies if missing. Start the game and play.';
end;
