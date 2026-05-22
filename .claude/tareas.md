<!-- Tareas pendientes de implementar en Mutagen Manager -->

## Completadas en v3.0 (2026-03-30)

### Mejoras de interfaz
- [x] Iconos en menú tray con Segoe MDL2 Assets (`IconRenderer.MakeGlyphIcon`)
- [x] `Ver Estado Global` → `StatusWindow` WPF nativa (en lugar de PowerShell)
- [x] `Ver Logs` → `LogViewerWindow` WPF con auto-refresh (en lugar de Notepad)
- [x] Pausar/Reanudar deshabilitados según estado real del sync
- [x] Doble-click inteligente: ConflictWindow si hay problemas, StatusWindow si no
- [x] Auto-abrir Configuración en primera ejecución (sin config.json)
- [x] Menú "Acerca de" con info de versión y ruta de logs
- [x] Separador en submenú Ajustes entre "Reiniciar Monitor" y "Configuración"

## Completadas en v3.1 (2026-05-11)

### Estabilidad
- [x] `SystemEvents.PowerModeChanged` — restart monitor + restaurar icono tras hibernación/sleep
- [x] Rotación de logs al superar 1MB (`.old` backup automático)
- [x] Fix: intervalo de polling usaba 30s hardcoded, ahora usa `config.Defaults.CheckInterval`

### Config en caliente
- [x] Si cambian syncs en Settings → ofrecer recrear en Mutagen (`OnConfigSaved` + `RecreateSyncsAsync`)
- [x] Si cambian notificaciones/intervalo → `MonitorService.UpdateConfig()` sin reiniciar

### UX / Settings
- [x] Validación en tiempo real en SettingsWindow: permisos octales (borde rojo si inválido)
- [x] Validación en tiempo real: intervalo ≥ 5s
- [x] Deshabilitar "Añadir sync" si no hay ningún servidor configurado
- [x] Detección de mutagen en PATH al arrancar (balloon de warning si no está)
- [x] "Acerca de" muestra versión de mutagen detectada
- [x] Menú por sync: "Forzar Sincronización Ahora" (`mutagen sync flush`)

### Distribución (replanteada)
- [x] Decisión final: **instalador Inno Setup** per-user (no ZIP). `installer.iss` + `build.ps1 -Installer`

## Completadas en v3.1.1 (2026-05-22)

### Estabilidad / bugfix crítico
- [x] Fix crash tras hibernaciones múltiples: resume no-bloqueante (`RefreshAfterResume`, sin `.Wait` en UI), `MonitorService.Start()` idempotente (`IsRunning`), loop con auto-recuperación, guard `_resumeHandling`

### Integración cliente
- [x] `mutagen.exe` bundleado junto al programa; `MutagenService` lo resuelve por ruta (no PATH)
- [x] `MutagenUpdater`: actualizar CLI a demanda desde GitHub (menú Ajustes → "Actualizar Mutagen CLI…")
- [x] `ConfigService.EnsureExists()`: crea config.json al lado del exe en primer arranque
- [x] Instalador per-user en `%LOCALAPPDATA%\Programs\MutagenManager` (sin admin), accesos directos, opción inicio con Windows
- [x] Versión a 3.1.0.0 (csproj) + texto "Acerca de"
- [x] `config.json` sacado del control de versiones (`git rm --cached`)

---

## Pendientes

- [ ] (Builder) Instalar Inno Setup 6: `winget install JRSoftware.InnoSetup`
- [ ] GitHub Release con `MutagenManager-Setup-3.1.0.exe`
- [ ] (Seguridad) Force-push tras reescribir historial para purgar config.json — ver sección abajo

---

## Pasos GitHub Release (instalador)

1. `.\build.ps1 -Installer` → `dist\MutagenManager.exe` + `mutagen.exe` bundle + `dist\MutagenManager-Setup-3.1.0.exe`
2. Subir el `setup.exe` al Release de GitHub
3. Instrucción al usuario:
   - Ejecutar `MutagenManager-Setup-3.1.0.exe` (no requiere admin)
   - Se instala en la carpeta de usuario; opción de acceso directo e inicio con Windows
   - Abrir, configurar servidores/syncs en Ajustes — listo (mutagen ya viene incluido)
   - Actualizar el CLI cuando se quiera: tray → Ajustes → "Actualizar Mutagen CLI…"

### Pin de versión del CLI
- Por defecto `build.ps1` bundlea la **última** release de mutagen al compilar.
- Para fijar una versión conocida-buena: `.\build.ps1 -Installer -MutagenVersion v0.18.1`
