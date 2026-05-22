# Mutagen Manager

<p align="center">
  <img src="mutagen.svg" alt="Mutagen Manager" width="120">
</p>

<p align="center">
  <strong>Visual Monitor & Manager for Mutagen Sync on Windows</strong>
</p>

<p align="center">
  A native Windows application (.exe) that manages <a href="https://mutagen.io/">Mutagen</a> file synchronizations with a system tray icon, real-time status, notifications, conflict resolution UI, and a full settings panel — no config file editing required.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/platform-Windows-blue?style=flat-square" alt="Platform">
  <img src="https://img.shields.io/badge/.NET-8.0%20WPF-512BD4?style=flat-square&logo=dotnet&logoColor=white" alt=".NET 8 WPF">
  <img src="https://img.shields.io/badge/license-MIT-green?style=flat-square" alt="License">
  <img src="https://img.shields.io/badge/version-3.1-brightgreen?style=flat-square" alt="v3.1">
</p>

---

## Features

- **System Tray Icon** — Real-time status overlay (green/orange/red) with tooltip summary
- **Smart Polling** — 30s interval when all OK, drops to 5s automatically when conflicts are active
- **Settings Panel** — Add/edit/delete syncs and servers visually, no JSON editing needed
- **Conflict Resolution Window** — Resolve conflicts with buttons (local / remote / VS Code diff)
- **Windows Notifications** — Alerts on conflict detection, disconnection, and sync resume
- **Auto-start** — Toggle start-with-Windows via registry (HKCU) directly from the tray menu
- **Bundled Mutagen CLI** — Ships with `mutagen.exe`; no PATH setup. Update on demand from the tray menu
- **Per-user installer** — One setup.exe, no admin rights, desktop shortcut; or run portable
- **Resilient to sleep/hibernate** — Survives repeated suspend/resume cycles without freezing
- **Non-blocking UI** — Full async/await, the tray never freezes regardless of mutagen command duration

---

## Installation (end users)

1. Download `MutagenManager-Setup-x.y.z.exe` from the [Releases](../../releases) page
2. Run it — installs per-user (no admin) into `%LOCALAPPDATA%\Programs\MutagenManager`, with an optional
   desktop shortcut and start-with-Windows option
3. Launch it and add your servers/syncs from the Settings panel (double-click the tray icon)

That's it — the Mutagen CLI is bundled, so there's nothing else to install. `config.json` is created
automatically on first run; update the bundled CLI any time from tray → Ajustes → "Actualizar Mutagen CLI…".

> **Requirements:** Windows 10/11. No .NET, no PowerShell, no separate Mutagen install needed.
>
> **Portable mode:** prefer no installer? Drop `MutagenManager.exe` + `mutagen.exe` in any folder and run it.

---

## Configuration

The app reads `config.json` from the same folder as the exe. You can edit it visually from inside the app (tray → double-click → Settings), or manually following `config.example.json` as a template.

```json
{
  "servers": {
    "my-server": {
      "host": "192.168.1.100",
      "port": 22,
      "user": "your-username",
      "defaultOwner": "www-data",
      "defaultGroup": "www-data"
    }
  },
  "syncs": [
    {
      "name": "my-project",
      "server": "my-server",
      "localPath": "C:\\Projects\\MyApp",
      "remotePath": "/var/www/myapp",
      "ignores": ["node_modules", ".git", "*.log", ".env", "cache"]
    }
  ],
  "defaults": {
    "mode": "two-way-safe",
    "fileMode": "0664",
    "directoryMode": "0775",
    "checkInterval": 30
  },
  "notifications": {
    "enabled": true,
    "sound": true,
    "showOnConflict": true,
    "showOnDisconnect": true,
    "showOnResume": true
  }
}
```

### Ignore pattern syntax

Mutagen uses gitignore syntax. Key rules:
- `node_modules` — matches at any depth (no slash = any level) ✅
- `**/android/.gradle/` — matches nested paths at any depth ✅
- `android/.gradle/` — only matches from sync root, **not** in subdirectories ❌

---

## SSH key setup (required — one-time, per server)

Mutagen connects over SSH and **must not** be prompted for a password mid-sync. Set up key-based
authentication once per server so the connection is passwordless. This is the only step that can't be
automated by the app — it needs access to the server.

### 1. Generate a key (Windows, once)

If you don't already have one (`C:\Users\<you>\.ssh\id_ed25519`):

```powershell
ssh-keygen -t ed25519 -C "mutagen"
# Press Enter to accept the default path; leave the passphrase EMPTY
# (a passphrase would make mutagen prompt — use an empty one, or an ssh-agent)
```

### 2. Copy the public key to the server

```powershell
# Replace user@host and port with your server's values
type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh -p 22 user@host "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 700 ~/.ssh && chmod 600 ~/.ssh/authorized_keys"
```

Enter the password **this one time**. From now on the key is used instead.

### 3. Verify it's passwordless

```powershell
ssh -p 22 user@host "echo OK"
# Must print OK without asking for a password
```

If it still asks for a password, check on the server: `~/.ssh` is `700`, `authorized_keys` is `600`,
and the home directory isn't group/other-writable (`chmod g-w,o-w ~`).

> **Tip — custom port/key per host:** add a block to `C:\Users\<you>\.ssh\config` so mutagen picks the
> right key and port automatically:
> ```
> Host myserver
>     HostName 192.168.1.100
>     User your-username
>     Port 22
>     IdentityFile ~/.ssh/id_ed25519
> ```

Once `ssh user@host` connects without a prompt, Mutagen Manager will sync without interruptions.

---

## Building from source

### Requirements
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- First time only: `dotnet nuget add source https://api.nuget.org/v3/index.json --name nuget.org`

### Compile

```powershell
.\build.ps1              # publish self-contained exe + bundle mutagen.exe → dist\
.\build.ps1 -Run         # + launch
.\build.ps1 -Installer   # + build the Inno Setup installer → dist\MutagenManager-Setup-*.exe
```

Output: `dist\MutagenManager.exe` (~70MB, no dependencies) + bundled `dist\mutagen.exe`.
Pin a specific CLI version with `-MutagenVersion v0.18.1` (default: latest release at build time).

The `-Installer` step needs [Inno Setup 6](https://jrsoftware.org/isdl.php) on the build machine only:

```powershell
winget install JRSoftware.InnoSetup
```

`build.ps1` finds `ISCC.exe` on PATH or in the default `Program Files` location automatically.

---

## Project structure

```
(repo root)                   <- C# .NET 8 WPF source
  App.xaml / App.xaml.cs      <- Entry point, single-instance mutex
  TrayApplication.cs          <- NotifyIcon, context menu, coordinator
  IconRenderer.cs             <- GDI+ icon with status overlay
  GlobalUsings.cs             <- Namespace alias resolution
  Models/
    AppConfig.cs              <- config.json model (compatible with v2)
    SyncStatus.cs             <- Status codes and conflict pairs
  Services/
    ConfigService.cs          <- Load/save/auto-create config.json
    LogService.cs             <- Thread-safe UTF-8 logger with rotation
    MutagenService.cs         <- All mutagen CLI calls (async); resolves bundled exe
    MutagenUpdater.cs         <- On-demand CLI update from GitHub Releases
    AutoStartService.cs       <- HKCU registry Run key
    MonitorService.cs         <- Smart adaptive polling loop
  Views/
    ConflictWindow            <- Visual conflict resolver
    SettingsWindow            <- Settings panel (live validation)
    StatusWindow              <- Global status of all syncs
    LogViewerWindow           <- Log viewer with auto-refresh
    SyncEditDialog            <- Add/edit sync
    ServerEditDialog          <- Add/edit server

build.ps1                     <- Publish + bundle mutagen + build installer
installer.iss                 <- Inno Setup per-user installer script
config.example.json           <- Reference template for users
mutagen.ico / mutagen.svg     <- App icons
```

---

## Tray menu

```
[Mutagen Monitor]
-----------------------------------------
* my-project  -  Sincronizado
  |- Abrir Carpeta
  |- Pausar / Reanudar
  |- Reiniciar Sincronizacion
  +- Eliminar Sincronizacion
-----------------------------------------
  Ver Estado Global
  Resolver Conflictos
-----------------------------------------
  Ajustes
  |- Iniciar con Windows - Activado
  |- Reiniciar Monitor
  |- Actualizar Mutagen CLI...
  |- Configuracion...
  |- Ver Logs
  +- Acerca de...
-----------------------------------------
  Salir
```

---

## Status icons

| Overlay | Meaning |
|---------|---------|
| Green dot | All syncs watching (OK) |
| Orange dot | One or more syncs paused |
| Red dot | Conflict or error detected |
| Grey dot | Status unknown / starting |

---

## Changelog

### v3.1 (2026-05-22)
- **Bundled Mutagen CLI** — ships with `mutagen.exe`, resolved by path (no PATH dependency); update on demand from the tray
- **Per-user installer** (Inno Setup) — no admin, installs to `%LOCALAPPDATA%\Programs`, desktop shortcut, optional autostart
- **Config auto-create** — `config.json` is generated on first run
- **Sleep/hibernate fix** — resilient resume handling; no more freeze/blank menu after repeated suspend cycles
- Log rotation, live settings validation, hot-reload of notifications/interval, force-flush per sync

### v3.0.0 (2026-03-30)
- **Complete rewrite** in C# .NET 8 WPF — no more PowerShell, no PS2EXE
- Settings panel with 4 tabs: Syncs, Servers, General, Notifications
- Visual conflict resolution window (replaces resolve-conflicts.ps1)
- Auto-start via Windows registry — reliable, no SmartScreen issues
- Smart adaptive polling: 30s normal, 5s when conflicts active
- Fully async — UI never blocks regardless of mutagen command duration
- Single self-contained .exe, no dependencies for end users
- config.json format unchanged (compatible with v2 configs)

### v2.1.1 (2026-03-28)
- "Reiniciar Sincronizacion" re-reads config.json at click time
- Fixed slow conflict resolution (removed blocking flush)

### v2.1.0
- Migrated auto-start from Task Scheduler to Startup folder

### v2.0.0
- Multiple server support, ownership config, tray icon with overlay

---

## Dependencies

- [Mutagen CLI](https://mutagen.io/) — **bundled** with the app (no separate install). Falls back to PATH in portable/dev use
- SSH configured for remote servers (key-based auth recommended)
- VS Code (optional, for conflict diff view)

## License

MIT — see [LICENSE](LICENSE)
