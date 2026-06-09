; ============================================================================
;  Mutagen Manager — Inno Setup installer script
;
;  Per-user install (no admin): installs into %LOCALAPPDATA%\Programs\MutagenManager
;  alongside the bundled mutagen.exe and a config.example.json reference.
;  The app creates config.json next to itself on first run.
;
;  Build:  ISCC.exe installer.iss   (after .\build.ps1 has populated dist\)
;  Output: dist\MutagenManager-Setup-<version>.exe
;
;  Only the BUILDER needs Inno Setup installed. End users just run the setup.exe.
; ============================================================================

#define AppName        "Mutagen Manager"
; AppVersion la puede sobrescribir el build: ISCC /DAppVersion=3.1.3 (CI desde el git tag).
; Si no se pasa, cae al valor por defecto de abajo.
#ifndef AppVersion
  #define AppVersion    "3.1.2"
#endif
#define AppPublisher    "fibanez7"
#define AppExe          "MutagenManager.exe"
; Stable GUID — keeps upgrades in place instead of duplicating installs.
#define AppId           "{{8F3C2A14-9D6B-4E77-B1A2-2C5E9F0A7D31}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
; Per-user install — no admin rights required, writable config location.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={localappdata}\Programs\MutagenManager
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=dist
OutputBaseFilename=MutagenManager-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Close the running app before upgrading so files aren't locked.
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"
; Nota: el inicio con Windows se gestiona desde el menú tray de la app (HKCU\Run),
; no aquí, para tener una única fuente de verdad y no duplicar mecanismos.

[Files]
Source: "dist\{#AppExe}";              DestDir: "{app}"; Flags: ignoreversion
Source: "dist\mutagen.exe";            DestDir: "{app}"; Flags: ignoreversion
; Agent bundle (~97MB): mutagen lo necesita para instalar el agente POSIX en el server.
; Sin el -> "unable to locate agent bundle" al crear sync en maquina nueva.
Source: "dist\mutagen-agents.tar.gz";  DestDir: "{app}"; Flags: ignoreversion
Source: "config.example.json";         DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}";              Filename: "{app}\{#AppExe}"
Name: "{group}\Desinstalar {#AppName}";  Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}";        Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Iniciar Mutagen Manager"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Remove the bundled-CLI rollback copy if present; leave user config.json untouched.
Type: files; Name: "{app}\mutagen.exe.old"

[Code]
{ Stops the running mutagen daemon and the tray app so their .exe files aren't locked
  during install/uninstall. The daemon is a windowless background process, so Inno's
  CloseApplications/RestartManager can't close it — we stop it explicitly with
  `mutagen daemon stop`. Sync sessions are persisted on disk by the daemon, so this
  only PAUSES syncing; the app restarts the daemon on next launch and syncs resume. }
procedure StopMutagenProcesses;
var
  ResultCode: Integer;
  MutagenPath: String;
begin
  MutagenPath := ExpandConstant('{app}\mutagen.exe');
  if FileExists(MutagenPath) then
    Exec(MutagenPath, 'daemon stop', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  { Close the tray app (it runs as a NotifyIcon without a normal window, so a plain
    RestartManager close is unreliable). }
  Exec(ExpandConstant('{cmd}'), '/C taskkill /IM MutagenManager.exe /F /T', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopMutagenProcesses;
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    StopMutagenProcesses;
end;
