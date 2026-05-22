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
#define AppVersion      "3.1.0"
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
Name: "startupicon"; Description: "Iniciar Mutagen Manager con Windows"; GroupDescription: "Inicio:"; Flags: unchecked

[Files]
Source: "dist\{#AppExe}";        DestDir: "{app}"; Flags: ignoreversion
Source: "dist\mutagen.exe";      DestDir: "{app}"; Flags: ignoreversion
Source: "config.example.json";   DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}";              Filename: "{app}\{#AppExe}"
Name: "{group}\Desinstalar {#AppName}";  Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}";        Filename: "{app}\{#AppExe}"; Tasks: desktopicon
; Optional Startup-folder launch (the app also offers HKCU\Run from its tray menu)
Name: "{userstartup}\{#AppName}";        Filename: "{app}\{#AppExe}"; Tasks: startupicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Iniciar Mutagen Manager"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Remove the bundled-CLI rollback copy if present; leave user config.json untouched.
Type: files; Name: "{app}\mutagen.exe.old"
