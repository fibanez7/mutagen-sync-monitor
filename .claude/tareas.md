<!-- Tareas / backlog Mutagen Manager. Histórico viejo (v3.0/v3.1) podado — ver git log. -->

## v3.1.1 (2026-06-02) — rendimiento + bugfix GDI

### Estabilidad / bugfix crítico
- [x] Fix fuga de handles GDI en `IconRenderer`: tray icon (`GetHicon()` sin `DestroyIcon`) y status dots se re-renderizaban cada poll → handles GDI hacia el límite 10000/proceso → submenú de sync en blanco, colgado y cierre. Fix: cache por `SyncStatusCode` + clon manejado + `DestroyIcon` del HICON transitorio.
- [x] Cache de `MakeGlyphIcon` (set fijo de glyphs) → cero re-render/fuga al recrear menú.
- [x] `RebuildSyncMenuItems` dispone los items (libera handles de dropdown) anulando antes las imágenes compartidas (`DetachImages`).

### Rendimiento (ligereza + varias conexiones)
- [x] **Polling batch:** `CheckAllAsync` hacía 1 proceso `mutagen.exe` por sync por poll (N procesos/intervalo). Ahora **1 sola** llamada `mutagen sync list --long` por poll y se parsea por nombre (`SplitSessionBlocks`). Coste constante sin importar cuántas syncs → soporta varias conexiones sin penalizar el sistema.

### Ciclo de vida de sesiones / robustez
- [x] **Limpiar huérfana al renombrar/eliminar:** `DetectOrphanedSessions` (SettingsWindow) + `TerminateOrphansAsync` (TrayApplication) terminan la sesión del daemon de syncs que desaparecen del config. Ya no quedan huérfanas.
- [x] **Watchdog del daemon:** `CheckAllAsync` reinicia `mutagen daemon start` tras 2 polls con exit≠0 seguidos (margen anti-falso-positivo). Balloon + force-check.

### Versión
- [x] 3.1.1.0 en `MutagenManager.csproj` + `installer.iss`.

---

## Hecho antes (v3.1.0, ya en producción)
- [x] config.json purgado del historial git (verificado: no aparece en `git log`).
- [x] Primera Release publicada en GitHub (repo `fibanez7/mutagen-sync-monitor`).

---

## Pendiente — lanzar Release 3.1.1
- [ ] `winget install JRSoftware.InnoSetup` (si ISCC.exe no está en el equipo).
- [ ] `.\build.ps1 -Installer` → `dist\MutagenManager-Setup-3.1.1.exe`.
- [ ] Subir el `setup.exe` 3.1.1 a GitHub Releases (changelog: fix GDI + polling batch).
- [ ] Probar update-en-sitio: ejecutar el setup sobre la instalación existente (NO desinstalar; `AppId` estable actualiza encima, `CloseApplications=yes` cierra la app, config.json se conserva).

---

## Backlog (mejoras futuras — no bloquean release)

Criterio: solo cosas que aporten valor real, simples, sin penalizar rendimiento del equipo (tray debe ser ligero).

### Diagnóstico
- [x] Log de handles GDI/USER al arrancar + cada hora (`GetGuiResources`) para cazar regresiones de la fuga sin Task Manager. Coste mínimo. (`TrayApplication.LogHandleCounts`)

### Descartadas (no aportan / no gratis)
- UX: progreso en flush, botón carpeta remota → poco valor, añaden complejidad.
- Firma code-signing → requiere cert de pago.
- Auto-update de la app → complejidad + red al arrancar; ya hay update del CLI a demanda.

---

## Notas de build/release
- `build.ps1` bundlea la **última** mutagen por defecto. Fijar versión: `.\build.ps1 -Installer -MutagenVersion v0.18.1`.
- Subir csproj **e** installer.iss a la vez en cada bump de versión.
