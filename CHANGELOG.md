# Changelog

Todas las versiones notables de Mutagen Manager.
Formato basado en [Keep a Changelog](https://keepachangelog.com/es/1.1.0/).
El CI extrae la sección de cada versión como notas de la Release al empujar su tag `vX.Y.Z`.

## [3.1.4]

### Fixed
- **Resolver Conflictos: la app petaba al pulsar "Comparar VS Code".** `Process.Start("code", …)` no podía arrancar `code.cmd` (UseShellExecute=false) y la excepción, en un handler sin try/catch, cerraba la app. Ahora se lanza vía shell y se captura el error con aviso claro si VS Code no está en el PATH.
- **Botones "Usar Local/Remoto" no hacían nada en silencio.** No se comprobaba si el borrado remoto por SSH funcionaba ni se capturaban errores; si `ssh`/`scp` faltaba o la autenticación fallaba, el conflicto seguía sin avisar. Ahora se valida el resultado, se muestra el motivo del fallo y la UI se bloquea mientras opera.
- Errores en toda la ventana de conflictos ahora se registran y se muestran (antes excepciones no capturadas reventaban la app). "Todos: Local/Remoto" resetea cada sync afectada (antes solo una con "(Todas)").

## [3.1.3]

### Fixed
- **Crear syncs fallaba en máquina/usuario-SSH nuevo** con `unable to locate agent bundle`. El agente POSIX (`mutagen-agents.tar.gz`) no se incluía junto a `mutagen.exe`; ese tarball es el que mutagen sube por SSH al servidor. Ahora se bundlea en `build.ps1`, `installer.iss` y en el auto-actualizador del CLI (con la versión casada).

### Added
- **Releases automáticas** vía GitHub Actions: al empujar un tag `vX.Y.Z` se compila, se genera el instalador y se publica la Release con el `setup.exe` adjunto. La versión la inyecta el tag (ya no se edita a mano en los ficheros).

## [3.1.2]

### Fixed
- Sync nuevo creado desde Ajustes no se materializaba en el daemon (se guardaba en config pero sin `mutagen sync create`).
- "No se pudo crear" sin causa: ahora se muestra la salida real de mutagen y causas habituales (p.ej. permisos de `~/.ssh` en el servidor).
- Servidores nuevos no aparecían en el diálogo de sync hasta reiniciar.
- Ventanas/diálogos que quedaban detrás del escritorio o de Ajustes (z-order/owner).
- "Reiniciar Monitor" dejaba la app cerrada (carrera con el mutex de instancia única).

## [3.1.1]

### Fixed
- Fuga de handles GDI que tras horas dejaba el menú en blanco y cerraba la app (cache de iconos por estado).
- Polling en batch: una sola `mutagen sync list` por ciclo en vez de un proceso por sync.

### Added
- Limpieza de sesiones huérfanas al renombrar/eliminar syncs.
- Watchdog del daemon: lo rearranca si `sync list` falla en 2 ciclos seguidos.
- Sesiones huérfanas visibles en el Estado Global.

## [3.1.0]

### Added
- Instalador Inno Setup per-user (sin admin) en `%LOCALAPPDATA%\Programs\MutagenManager`.

### Fixed
- Cuelgues tras hibernación por loops de monitor acumulados (resume no bloqueante + monitor idempotente).
- Intervalo de polling configurable en vez de hardcoded.
