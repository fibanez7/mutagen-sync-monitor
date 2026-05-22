# Mutagen Manager — Documentación interna para Claude

## Estado actual: v3.1 (2026-05-11)

App de bandeja (system tray) en **C# .NET 8 WPF + WinForms** que gestiona syncs de [Mutagen](https://mutagen.io) entre Windows y servidores Linux. Migrada desde PowerShell + PS2EXE; los PS1 antiguos ya no existen en el repo. El código fuente vive en la raíz.

> Nota de mantenimiento: `MutagenManager.csproj` declara `AssemblyVersion`/`FileVersion = 3.0.0.0`. Subir a `3.1.0.0` al preparar el Release.

---

## Cómo trabajar en este proyecto (pautas para el agente)

Programador único (el usuario) dirige. El agente principal hace todo el ciclo: explora → planifica → implementa → verifica. No delegar en subagentes salvo petición explícita.

**Antes de tocar código:**
- Leer este CLAUDE.md primero. Da el mapa completo; evita explorar a ciegas.
- Localizar la clase/servicio responsable (ver Estructura + Arquitectura) antes de editar.
- Si el cambio cruza UI ↔ background, recordar la regla de hilos (abajo).

**Al implementar:**
- Respetar el patrón existente: servicios async sin bloquear UI, eventos `MonitorService` → `Dispatcher.Invoke` en `TrayApplication`.
- Cambios de schema en config.json: actualizar `Models/AppConfig.cs`, `config.example.json` y la tabla de este doc a la vez.
- Tras tocar comportamiento técnico no obvio, añadir la nota a este CLAUDE.md.

**Antes de cerrar:**
- Compilar: `.\build.ps1` (debe terminar sin errores).
- Verificar a mano si el cambio es de UI/runtime (ejecutar el .exe y comprobar tray).
- Nunca `git add`/commit automático — lo hace el usuario.

**Reglas duras:**
- UI thread: toda mutación de UI (icono, menú) va por `Dispatcher`. El bucle de monitor corre en `Task` background, jamás toca UI directo.
- `dist/`, `bin/`, `obj/`, `config.json` nunca al git (.gitignore). El .exe se distribuye solo vía GitHub Releases.

---

## Estructura del proyecto

```
c:\NMS\MutagenManager\          <- raíz del repo
├── MutagenManager.csproj        <- proyecto .NET 8, UseWPF + UseWindowsForms
├── App.xaml / App.xaml.cs       <- entrada; ShutdownMode=OnExplicitShutdown; mutex instancia única
├── TrayApplication.cs           <- NotifyIcon + ContextMenuStrip + coordinador de servicios
├── IconRenderer.cs              <- GDI+ icon 16x16 con overlay de estado; MakeGlyphIcon (Segoe MDL2)
├── GlobalUsings.cs              <- alias globales: Application, MessageBox, ComboBox (WPF vs WinForms)
├── Models/
│   ├── AppConfig.cs             <- deserializa config.json (System.Text.Json)
│   └── SyncStatus.cs            <- SyncStatusCode enum + ConflictPair
├── Services/
│   ├── ConfigService.cs         <- carga/guarda config.json (mismo directorio que el .exe)
│   ├── LogService.cs            <- append UTF-8 a %TEMP%\mutagen-manager.log; rota a .old >1MB
│   ├── MutagenService.cs        <- ejecuta mutagen async; resuelve mutagen.exe bundleado
│   ├── MutagenUpdater.cs        <- descarga/actualiza CLI mutagen desde GitHub (a demanda)
│   ├── AutoStartService.cs      <- HKCU\...\CurrentVersion\Run
│   └── MonitorService.cs        <- bucle de polling adaptativo
├── Views/
│   ├── ConflictWindow           <- resolución de conflictos
│   ├── SettingsWindow           <- panel configuración (pestañas) + validación en vivo
│   ├── StatusWindow             <- estado global de todos los syncs
│   ├── LogViewerWindow          <- visor de logs con auto-refresh
│   ├── SyncEditDialog           <- diálogo añadir/editar sync
│   └── ServerEditDialog         <- diálogo añadir/editar servidor
├── Resources/mutagen.ico        <- icono embebido (pack://application URI)
├── build.ps1                    <- publish + bundle mutagen + (opc.) compila instalador
├── installer.iss                <- script Inno Setup (instalación per-user)
├── config.example.json          <- plantilla de referencia para usuarios nuevos
├── mutagen.ico / mutagen.svg    <- logo (raíz + copia en Resources/)
├── README.md                    <- doc pública GitHub
└── .gitignore                   <- excluye bin/, obj/, dist/, config.json
```

---

## Arquitectura

```
App.xaml.cs (OnStartup)
    └── TrayApplication.Initialize()
            ├── ConfigService.Load()        <- lee config.json (auto-abre Settings si no existe)
            ├── IconRenderer.LoadBaseIcon()
            ├── BuildTray()                 <- NotifyIcon + ContextMenuStrip
            └── MonitorService.Start()      <- bucle async en background

MonitorService (Task background — NUNCA en UI thread)
    └── MonitorLoopAsync()
            ├── CheckAllAsync() -> GetStatusAsync() -> MutagenService.SyncListAsync()
            ├── ParseStatus()               <- parsea "mutagen sync list <name> --long"
            ├── StatusChanged?.Invoke()      <- TrayApplication responde con Dispatcher.Invoke()
            └── NotificationRequested?.Invoke()

TrayApplication (UI thread via Dispatcher)
    ├── OnStatusChanged()  -> actualiza icono tray + texto menú
    ├── UpdateTrayIcon()   -> peor estado entre todos los syncs
    ├── OnConfigSaved()    -> recrea syncs (RecreateSyncsAsync) o aplica en caliente (UpdateConfig)
    └── Handlers de menú   -> llaman MutagenService async sin bloquear
```

**Recuperación tras suspensión (`OnPowerModeChanged` / `RefreshAfterResume`):** en `Resume`, tras 8s (deja recuperar red/SSH) re-asienta el icono del tray y fuerza un check. **NO** reinicia el loop salvo que `_monitor.IsRunning` sea false (watchdog). Flag `_resumeHandling` evita handlers solapados.

> Bug histórico (corregido v3.1): la versión anterior hacía `_monitor.StopAsync().Wait(3000)` **dentro de `Dispatcher.Invoke`** (bloqueaba el hilo UI) y llamaba `Start()` sin garantía de que el loop viejo muriera. Tras varias hibernaciones se acumulaban loops concurrentes que saturaban el hilo UI → card en blanco, desplegables colgados, tray muerto al pinchar. Causa raíz: bloqueo de UI + loops fugados + `_cts.Dispose()` con token en uso. Fix: resume no-bloqueante + `MonitorService.Start()` idempotente (`IsRunning`) + loop con auto-recuperación de excepciones.

---

## Polling adaptativo (MonitorService)

Diseño deliberado para no pinguear de más. Intervalo base = `config.Defaults.CheckInterval` (no hardcoded; fix v3.1):

| Situación | Intervalo |
|-----------|-----------|
| Todo OK (Watching) | `CheckInterval` (default 30s) |
| Conflicto/error activo | 5 segundos |
| Tras operación de usuario (pause/resume/create/flush) | inmediato (~1s) |

`RequestImmediateCheck()` setea flag `_forceCheck` que el bucle comprueba cada 500ms.

---

## Parsing de salida de mutagen

`MonitorService.ParseStatus()` parsea `mutagen sync list <name> --long`. Secciones:
- `Status:` → estado base (Watching, Paused, …)
- `Scan problems:` → lectura
- `Transition problems:` → escritura/permisos
- `Conflicts:` → conflictos alpha/beta

Indentación: detalles en líneas con `\t\t+` (doble tab o más).
**ConflictPairs**: regex `^\((\w+)\)\s+(.+?)\s+\(` → side (alpha/beta) + path.

---

## Estados del icono

| SyncStatusCode | Color | Condición |
|---|---|---|
| Ok | Verde #4CAF50 | Status contiene "Watching" |
| Paused | Naranja #FF9800 | "Paused" (sin conflict) |
| Conflict | Rojo #F44336 | ScanProblems/TransitionProblems/Conflicts > 0, o "conflict" en status |
| Error | Rojo #F44336 | "error" en status |
| Unknown | Gris #9E9E9E | Resto |

Icono del tray = **peor estado** entre todos los syncs.

---

## Resolución de conflictos (ConflictWindow)

1. Usuario abre "Resolver Conflictos" (o doble-click en tray si hay problemas).
2. `ConflictWindow` llama `MonitorService.GetStatusAsync()` por cada sync.
3. Muestra pares alpha/beta con fechas: local `File.GetLastWriteTime`, remota SSH `stat -c '%Y'`.
4. Por conflicto: Usar Local / Usar Remoto / Ver en VS Code / Saltar.
5. Globales: Todos Local / Todos Remoto / Reset Sync.
6. Al cerrar, `TrayApplication` llama `RequestImmediateCheck()`.

- **Usar Local** → `rm -f` remoto via SSH + `mutagen sync reset`
- **Usar Remoto** → `File.Delete` local + `mutagen sync reset`
- **VS Code diff** → SCP remoto a `%TEMP%\mutagen-conflicts\` + `code --diff`

---

## AutoStart

`HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` directo vía `Microsoft.Win32.Registry`.
**Por qué no Startup folder:** el método previo (PS2EXE + .lnk) fallaba silencioso por SmartScreen/Defender. El registro HKCU no pide admin y Windows confía en binarios .NET nativos.

---

## config.json

Buscado en el **mismo directorio que el .exe** (`ConfigService` usa `Path.GetDirectoryName(exePath)`). Schema compatible con v2. Si no existe, `ConfigService.EnsureExists()` crea uno vacío válido al arrancar y se abre Ajustes (primer arranque). `config.example.json` se distribuye solo como referencia.

```json
{
  "servers": {
    "clave-servidor": { "host": "IP/dominio", "port": 22, "user": "ssh-user",
                         "defaultOwner": "www-data", "defaultGroup": "www-data" }
  },
  "syncs": [
    { "name": "nombre-unico", "server": "clave-servidor",
      "localPath": "C:\\ruta\\local", "remotePath": "/ruta/remota",
      "ignores": ["node_modules", "*.log"], "defaultOwner": null, "defaultGroup": null }
  ],
  "defaults": { "mode": "two-way-safe", "fileMode": "0664",
                "directoryMode": "0775", "checkInterval": 30 },
  "notifications": { "enabled": true, "sound": true, "showOnConflict": true,
                     "showOnDisconnect": true, "showOnResume": true }
}
```

**Config en caliente (v3.1):** al guardar en Settings, si cambian syncs → `OnConfigSaved` ofrece recrear (`RecreateSyncsAsync`); si solo cambian notificaciones/intervalo → `MonitorService.UpdateConfig()` sin reiniciar.

### Reglas de ignores (sintaxis gitignore)
- `node_modules` → cualquier nivel ✅
- `**/android/.gradle/` → anidado en cualquier nivel ✅
- `android/.gradle/` → SOLO desde raíz de la sync ❌
- Mutagen NO recarga en caliente: recrear la sync si cambian los ignores.

---

## Build

```powershell
.\build.ps1                       # publish + descarga/bundlea mutagen.exe en dist\
.\build.ps1 -Run                  # + ejecuta
.\build.ps1 -Installer            # + compila instalador Inno Setup → dist\MutagenManager-Setup-*.exe
.\build.ps1 -MutagenVersion v0.18.1   # pin de versión concreta del CLI (vacío = última)
.\build.ps1 -SkipMutagen          # no re-descargar mutagen.exe si ya está en dist\
```

`build.ps1`: `dotnet publish` Release win-x64 self-contained single-file comprimido + descarga el CLI mutagen (windows/amd64 zip de GitHub, extrae `mutagen.exe` a `dist\`) + opcionalmente compila el instalador con `ISCC.exe`.

**Primera vez en una máquina:** configurar la fuente NuGet o falla con NU1100:
`dotnet nuget add source https://api.nuget.org/v3/index.json --name nuget.org`

**Colisión de namespace (WPF vs WinForms):** `Application`, `MessageBox`, `ComboBox` existen en ambos. Resuelto con aliases en `GlobalUsings.cs`. No quitar `UseWindowsForms` ni `UseWPF` sin revisar esos aliases.

---

## Mutagen CLI (bundled + actualizable)

El CLI se **bundlea** junto al programa (`mutagen.exe` en la carpeta de instalación). Decisión deliberada: versión fija conocida-buena, offline, sin que el usuario instale nada ni dependa del PATH global.

- **Resolución de ruta** (`MutagenService`): prefiere `mutagen.exe` junto al exe (`AppDirectory`); si no existe, cae a `"mutagen"` en PATH (modo dev/portable). `MutagenAsync` siempre usa `MutagenPath`.
- **Actualización a demanda** (`MutagenUpdater`): menú tray → Ajustes → "Actualizar Mutagen CLI…". Consulta la última release de `mutagen-io/mutagen`, descarga windows/amd64, **para el daemon**, reemplaza el exe (backup `.old` para rollback), reinicia daemon. Solo si `CanSelfUpdate` (mutagen es la copia bundleada; si viene del PATH, avisa de usar winget).
- Por defecto NO auto-actualiza — estable salvo que el usuario lo pida.

---

## Distribución — instalador Inno Setup (per-user)

Decidido: **instalador `setup.exe`** (Inno Setup). Solo el que compila necesita Inno Setup; el usuario final solo ejecuta el setup.

- Instala en `%LOCALAPPDATA%\Programs\MutagenManager` (`PrivilegesRequired=lowest` → **sin admin**, carpeta escribible → resuelve el problema de permisos de Program Files).
- Copia `MutagenManager.exe` + `mutagen.exe` + `config.example.json`.
- Accesos directos: escritorio (opcional) + menú inicio. El inicio con Windows lo gestiona la app (HKCU\Run desde el tray), NO el instalador — una sola fuente de verdad. `AutoStartService.ReconcilePath()` reapunta la clave al exe actual tras reinstalar/mover.
- `AppId` GUID estable → reinstalar **actualiza en sitio**, no duplica. `CloseApplications=yes` cierra la app antes de actualizar para no bloquear ficheros.
- `config.json` lo crea el exe junto a sí mismo en el primer arranque (`ConfigService.EnsureExists`) y abre Ajustes. La carpeta es escribible, así que funciona instalado.

### Pasos del Release
1. `.\build.ps1 -Installer` → genera `dist\MutagenManager.exe`, bundlea `mutagen.exe` y compila `dist\MutagenManager-Setup-3.1.0.exe`.
2. Subir el `setup.exe` a GitHub Releases. Instrucción al usuario: ejecutar, configurar servidores en Ajustes. Nada más.

Detalle y checklist en `.claude/tareas.md`.

---

## Sesiones de mutagen vs config.json (huérfanas)

Las sesiones de mutagen viven en el **daemon por-usuario**, identificadas por **nombre**, independientes del binario y del config.json. `DetectChangedSyncs` (SettingsWindow) compara **por nombre**:

- **Cambiar ruta/ignores/server con el MISMO nombre** → `OnConfigSaved` lo detecta y ofrece recrear (`terminate` + `create`). Limpio.
- **Renombrar el sync** → el nombre viejo NO se termina (queda **huérfano** corriendo) y el nuevo se ve como sync nueva. Borrar en Ajustes (`DeleteSync`) tampoco termina la sesión (deliberado, línea ~136).

Operaciones que SÍ tocan el daemon (tray, por-sync): "Eliminar Sincronización" (`terminate`+quita del config), "Reiniciar Sincronización" (`terminate`+`create`).

**Detección de huérfanas (v3.1):** `StatusWindow` (Ver Estado Global) llama `MutagenService.GetAllSessionNamesAsync()` (parsea `mutagen sync list`), marca las sesiones que no están en config como filas rojas "⚠ Huérfana" y ofrece botón "Terminar huérfanas". También a mano: `mutagen sync list` / `mutagen sync terminate <nombre>`.

---

## Gotchas

- Logs en `%TEMP%\mutagen-manager.log` (no junto al .exe, por permisos). Rotan a `.old` >1MB.
- El usuario final solo necesita `.exe` + `config.json` al lado.
- Detección de mutagen en PATH al arrancar: balloon de warning si falta; versión mostrada en "Acerca de".
