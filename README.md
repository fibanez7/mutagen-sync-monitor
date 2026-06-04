# Mutagen Manager

<p align="center">
  <img src="mutagen.svg" alt="Mutagen Manager" width="120">
</p>

<p align="center">
  <strong>Monitor y gestor visual de Mutagen Sync para Windows</strong>
</p>

<p align="center">
  Aplicación nativa de Windows (.exe) que gestiona las sincronizaciones de <a href="https://mutagen.io/">Mutagen</a> con un icono en la bandeja del sistema, estado en tiempo real, notificaciones, ventana de resolución de conflictos y un panel de configuración completo — sin tener que editar ficheros de configuración a mano.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/plataforma-Windows-blue?style=flat-square" alt="Plataforma">
  <img src="https://img.shields.io/badge/.NET-8.0%20WPF-512BD4?style=flat-square&logo=dotnet&logoColor=white" alt=".NET 8 WPF">
  <img src="https://img.shields.io/badge/licencia-MIT-green?style=flat-square" alt="Licencia">
  <img src="https://img.shields.io/badge/versión-3.1.2-brightgreen?style=flat-square" alt="v3.1.2">
</p>

---

## Características

- **Icono en la bandeja** — Estado en tiempo real con superposición de color (verde/naranja/rojo) y resumen en el tooltip
- **Polling inteligente** — 30 s cuando todo está OK; baja a 5 s automáticamente si hay conflictos activos
- **Panel de configuración** — Añade/edita/elimina syncs y servidores de forma visual, sin tocar JSON
- **Ventana de resolución de conflictos** — Resuelve conflictos con botones (local / remoto / diff en VS Code)
- **Notificaciones de Windows** — Avisos al detectar conflictos, desconexiones y reanudación de sync
- **Inicio automático** — Activa/desactiva el inicio con Windows vía registro (HKCU) desde el menú de la bandeja
- **CLI de Mutagen incluida** — Se distribuye con `mutagen.exe`; sin configurar el PATH. Actualizable a demanda desde el menú
- **Instalador por usuario** — Un solo setup.exe, sin permisos de administrador, acceso directo en el escritorio; o modo portable
- **Resistente a suspensión/hibernación** — Sobrevive a ciclos repetidos de suspender/reanudar sin colgarse
- **UI no bloqueante** — Totalmente async/await; la bandeja nunca se congela por mucho que tarde un comando de mutagen

---

## Instalación (usuarios finales)

1. Descarga `MutagenManager-Setup-x.y.z.exe` desde la página de [Releases](../../releases)
2. Ejecútalo — instala por usuario (sin administrador) en `%LOCALAPPDATA%\Programs\MutagenManager`, con acceso directo opcional en el escritorio y opción de inicio con Windows
3. Ábrelo y añade tus servidores/syncs desde el panel de Ajustes (doble clic en el icono de la bandeja)

Eso es todo — la CLI de Mutagen va incluida, así que no hay nada más que instalar. El `config.json` se crea automáticamente en el primer arranque; actualiza la CLI incluida cuando quieras desde bandeja → Ajustes → "Actualizar Mutagen CLI…".

> **Requisitos:** Windows 10/11. No hace falta .NET, ni PowerShell, ni instalar Mutagen aparte.
>
> **Modo portable:** ¿prefieres sin instalador? Copia `MutagenManager.exe` + `mutagen.exe` en cualquier carpeta y ejecútalo.

> ⚠️ **IMPORTANTE — el .exe suelto necesita `mutagen.exe` al lado.** Si descargas solo `MutagenManager.exe` de Releases, NO sincronizará: la app busca `mutagen.exe` en su propia carpeta. Usa el **setup.exe** (lo incluye) o, en portable, copia los DOS ficheros juntos. Verifica que en `%LOCALAPPDATA%\Programs\MutagenManager` estén **ambos**: `MutagenManager.exe` y `mutagen.exe`.

---

## Configuración

La app lee `config.json` desde la misma carpeta que el exe. Puedes editarlo visualmente desde la propia app (bandeja → doble clic → Ajustes) o a mano usando `config.example.json` como plantilla.

```json
{
  "servers": {
    "mi-servidor": {
      "host": "192.168.1.100",
      "port": 22,
      "user": "tu-usuario",
      "defaultOwner": "www-data",
      "defaultGroup": "www-data"
    }
  },
  "syncs": [
    {
      "name": "mi-proyecto",
      "server": "mi-servidor",
      "localPath": "C:\\Proyectos\\MiApp",
      "remotePath": "/var/www/miapp",
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

### Sintaxis de los patrones de ignore

Mutagen usa sintaxis gitignore. Reglas clave:
- `node_modules` — coincide a cualquier nivel (sin barra = cualquier profundidad) ✅
- `**/android/.gradle/` — coincide anidado a cualquier profundidad ✅
- `android/.gradle/` — solo coincide desde la raíz de la sync, **no** en subcarpetas ❌

---

## Configuración SSH (obligatoria — una vez por servidor)

Mutagen conecta por SSH y **no puede** pedir contraseña a mitad de la sincronización. Configura la autenticación por clave una vez por servidor para que la conexión sea sin contraseña. Es el único paso que la app no puede automatizar — necesita acceso al servidor.

### 1. Genera una clave (Windows, una vez)

Si aún no tienes una (`C:\Users\<tú>\.ssh\id_ed25519`):

```powershell
ssh-keygen -t ed25519 -C "mutagen"
# Pulsa Enter para aceptar la ruta por defecto; deja la passphrase VACÍA
# (una passphrase haría que mutagen pidiera contraseña — déjala vacía, o usa un ssh-agent)
```

### 2. Copia la clave pública al servidor

```powershell
# Sustituye user@host y el puerto por los de tu servidor
type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh -p 22 user@host "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 700 ~/.ssh && chmod 600 ~/.ssh/authorized_keys"
```

Introduce la contraseña **esta única vez**. A partir de ahora se usa la clave.

### 3. Permisos en el servidor (¡crítico!)

SSH **ignora la clave** si el directorio home o `~/.ssh` son escribibles por grupo u otros. Si la
sincronización falla con "Permission denied (publickey)" o mutagen no puede usar el certificado,
ejecuta **en el servidor**, como el usuario SSH:

```bash
# Sustituye USUARIO por el usuario SSH (el de "user" en config.json)
chmod 700 /home/USUARIO          # el home NO puede ser escribible por grupo/otros
chmod 700 /home/USUARIO/.ssh     # la carpeta .ssh: solo el dueño
chmod 600 /home/USUARIO/.ssh/authorized_keys
chown -R USUARIO:USUARIO /home/USUARIO/.ssh
```

> Si tu usuario ya tiene sesión iniciada, basta con: `chmod 700 ~ && chmod 700 ~/.ssh && chmod 600 ~/.ssh/authorized_keys`.

### 4. Verifica que es sin contraseña

```powershell
ssh -p 22 user@host "echo OK"
# Debe imprimir OK sin pedir contraseña
```

> **Consejo — puerto/clave personalizados por host:** añade un bloque a `C:\Users\<tú>\.ssh\config`
> para que mutagen elija la clave y el puerto correctos automáticamente:
> ```
> Host miservidor
>     HostName 192.168.1.100
>     User tu-usuario
>     Port 22
>     IdentityFile ~/.ssh/id_ed25519
> ```

Una vez que `ssh user@host` conecta sin pedir nada, Mutagen Manager sincronizará sin interrupciones.

---

## Acciones por sincronización (menú de la bandeja)

Al desplegar un sync en el menú de la bandeja tienes varias acciones. Dos que se parecen pero **no son lo mismo**:

| Acción | Qué hace (mutagen) | Cuándo usarla |
|--------|--------------------|---------------|
| **Forzar Sincronización Ahora** | `sync flush` — empuja YA los cambios pendientes sin tocar la sesión | Tienes prisa y no quieres esperar al siguiente poll. Rápido y seguro |
| **Reiniciar Sincronización** | `sync terminate` + `sync create` — destruye la sesión y la recrea desde `config.json` | Cambiaste rutas/ignores/owner, o la sesión está rota/colgada |
| **Pausar / Reanudar** | `sync pause` / `sync resume` | Detener temporalmente sin borrar la sesión |
| **Eliminar Sincronización** | `sync terminate` + la quita de `config.json` | Ya no quieres ese sync |

> **Resumen:** *Forzar* = "sincroniza ahora mismo" (mantiene la sesión). *Reiniciar* = "reconstruye la sesión" (relee la configuración). Si solo quieres que se sincronice ya, usa **Forzar**; **Reiniciar** solo si cambiaste configuración o algo va mal.

---

## Solución de problemas

### La app no arranca en otro PC (se queda "suspendido" y se cierra)

Síntoma típico en un equipo nuevo: ejecutas `MutagenManager.exe`, en el Administrador de Tareas aparece
un instante como **suspendido** y luego desaparece; `mutagen.exe` nunca llega a salir. Casi siempre es
**Windows bloqueando un ejecutable no firmado descargado de internet**, no un fallo de la app. Causas y solución:

1. **Mark-of-the-Web (MOTW).** Los ficheros descargados quedan "marcados como bloqueados". Windows los
   suspende al ejecutarlos. Desbloquéalo:
   - Clic derecho en el `.exe` → **Propiedades** → marca **Desbloquear** (abajo) → Aceptar.
   - O por PowerShell: `Unblock-File .\MutagenManager.exe` (y también `.\mutagen.exe`).

2. **SmartScreen.** Si sale "Windows protegió tu PC", pulsa **Más información → Ejecutar de todas formas**.
   Si solo cierras el aviso, el proceso muere (eso explica el "abre y se cierra").

3. **Windows Defender / antivirus.** Un .exe self-contained comprimido se auto-extrae al arrancar; el
   antivirus puede congelar el proceso mientras lo escanea (estado "suspendido") y matarlo. Añade una
   exclusión para `%LOCALAPPDATA%\Programs\MutagenManager` o usa el **instalador** (mejor reputación que el exe suelto).

4. **Instancia zombie / no arranca aunque lo relances.** Si una copia anterior quedó colgada, el guardia
   de instancia única ve el mutex tomado y la nueva se cierra. Mata `MutagenManager.exe` en el
   Administrador de Tareas y vuelve a lanzar. (Recuerda: es app de bandeja — **no abre ventana** al
   arrancar; busca el icono junto al reloj.)

> Como el binario no está firmado, lo más limpio para distribuir a compañeros es el **setup.exe**
> (mejor reputación con SmartScreen) + decirles que **Desbloqueen** el fichero si Windows se queja.

### `mutagen.exe` abre una consola y se cierra al instante

Es **normal**. `mutagen.exe` es una herramienta de línea de comandos, no un programa de doble clic. Al
ejecutarlo sin argumentos imprime la ayuda y termina. El que trabaja en segundo plano es el **daemon**,
que la app arranca sola (`mutagen daemon start`). No lo lances a mano.

### Versiones de Mutagen y el daemon (por qué a uno le funciona y a otro no)

- La app usa el `mutagen.exe` **de su propia carpeta** (incluido), no el del PATH del sistema. Por eso
  da igual que `mutagen version` falle en la terminal de otro PC: la app no depende del PATH.
- El **daemon de mutagen es por usuario y único**. Si en un PC ya tienes mutagen instalado globalmente
  (p. ej. `v0.18.1` en el PATH) con su daemon corriendo, y la app trae otra versión incluida, puede haber
  **desajuste de versión cliente/daemon**. Si ves errores raros, para el daemon viejo y deja que la app
  use el suyo:
  ```powershell
  mutagen daemon stop      # para el daemon que esté corriendo
  # vuelve a abrir la app; arrancará el daemon con su mutagen.exe incluido
  ```
- En un PC **sin** mutagen previo no hay conflicto: la app arranca su propio daemon con la versión incluida.

### Procesos de mutagen que siguen en el Administrador de Tareas tras cerrar la app

Es **por diseño**. El daemon de mutagen persiste al cerrar la app para que las sesiones de sync sigan
vivas. Si quieres pararlo del todo: `mutagen daemon stop`. El instalador ya lo para antes de actualizar.

---

## Compilar desde el código

### Requisitos
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Solo la primera vez: `dotnet nuget add source https://api.nuget.org/v3/index.json --name nuget.org`

### Compilar

```powershell
.\build.ps1              # publica el exe self-contained + incluye mutagen.exe → dist\
.\build.ps1 -Run         # + lo ejecuta
.\build.ps1 -Installer   # + compila el instalador Inno Setup → dist\MutagenManager-Setup-*.exe
```

Salida: `dist\MutagenManager.exe` (~70 MB, sin dependencias) + `dist\mutagen.exe` incluido.
Fija una versión concreta de la CLI con `-MutagenVersion v0.18.1` (por defecto: última release al compilar).

El paso `-Installer` solo necesita [Inno Setup 6](https://jrsoftware.org/isdl.php) en la máquina que compila:

```powershell
winget install JRSoftware.InnoSetup
```

`build.ps1` localiza `ISCC.exe` en el PATH o en la ruta estándar de `Program Files` automáticamente.

---

## Estructura del proyecto

```
(raíz del repo)               <- código C# .NET 8 WPF
  App.xaml / App.xaml.cs      <- Punto de entrada, mutex de instancia única
  TrayApplication.cs          <- NotifyIcon, menú contextual, coordinador
  IconRenderer.cs             <- Icono GDI+ con superposición de estado
  GlobalUsings.cs             <- Resolución de alias de namespaces
  Models/
    AppConfig.cs              <- Modelo de config.json (compatible con v2)
    SyncStatus.cs             <- Códigos de estado y pares de conflicto
  Services/
    ConfigService.cs          <- Carga/guarda/auto-crea config.json
    LogService.cs             <- Logger UTF-8 thread-safe con rotación
    MutagenService.cs         <- Todas las llamadas a la CLI mutagen (async); resuelve el exe incluido
    MutagenUpdater.cs         <- Actualización de la CLI a demanda desde GitHub Releases
    AutoStartService.cs       <- Clave Run del registro (HKCU)
    MonitorService.cs         <- Bucle de polling adaptativo inteligente
  Views/
    ConflictWindow            <- Resolución visual de conflictos
    SettingsWindow            <- Panel de configuración (validación en vivo)
    StatusWindow              <- Estado global de todos los syncs
    LogViewerWindow           <- Visor de logs con auto-refresco
    SyncEditDialog            <- Añadir/editar sync
    ServerEditDialog          <- Añadir/editar servidor

build.ps1                     <- Publish + incluir mutagen + compilar instalador
installer.iss                 <- Script del instalador Inno Setup (por usuario)
config.example.json           <- Plantilla de referencia para usuarios
mutagen.ico / mutagen.svg     <- Iconos de la app
```

---

## Menú de la bandeja

```
[Mutagen Monitor]
-----------------------------------------
* mi-proyecto  -  Sincronizado
  |- Abrir Carpeta Local
  |- Pausar / Reanudar
  |- Forzar Sincronizacion Ahora
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

## Iconos de estado

| Superposición | Significado |
|---------------|-------------|
| Punto verde | Todos los syncs vigilando (OK) |
| Punto naranja | Uno o más syncs pausados |
| Punto rojo | Conflicto o error detectado |
| Punto gris | Estado desconocido / iniciando |

---

## Registro de cambios

### v3.1.2 (2026-06-04)
- **Crear sync desde Ajustes:** los syncs nuevos creados en el panel ahora se crean en Mutagen al guardar (antes se guardaban en config.json pero nunca se creaba la sesión)
- **Error de creación con causa:** si `sync create` falla, se muestra la salida real de mutagen + causas habituales (permisos SSH, host key) en lugar de un genérico "no se pudo crear"
- **Servidores en el diálogo de sync:** los servidores añadidos en la sesión aparecen al instante al crear un sync (antes había que reiniciar la app)
- **Ventanas al frente:** las ventanas (Ajustes, Estado, etc.) ya no se abren detrás del escritorio
- **"Reiniciar Monitor":** ya no deja la app cerrada (la instancia nueva espera a que la vieja libere el mutex)

### v3.1 (2026-05-22)
- **CLI de Mutagen incluida** — se distribuye con `mutagen.exe`, resuelto por ruta (sin depender del PATH); actualizable a demanda desde la bandeja
- **Instalador por usuario** (Inno Setup) — sin administrador, instala en `%LOCALAPPDATA%\Programs`, acceso directo en el escritorio, autostart opcional
- **Auto-creación de config** — `config.json` se genera en el primer arranque
- **Fix de suspensión/hibernación** — reanudación resistente; no más cuelgues/menú en blanco tras ciclos repetidos de suspensión
- Rotación de logs, validación de ajustes en vivo, recarga en caliente de notificaciones/intervalo, flush forzado por sync

### v3.0.0 (2026-03-30)
- **Reescritura completa** en C# .NET 8 WPF — adiós a PowerShell y PS2EXE
- Panel de configuración con 4 pestañas: Syncs, Servidores, General, Notificaciones
- Ventana visual de resolución de conflictos (reemplaza resolve-conflicts.ps1)
- Inicio automático vía registro de Windows — fiable, sin problemas de SmartScreen
- Polling adaptativo inteligente: 30 s normal, 5 s con conflictos activos
- Totalmente async — la UI nunca se bloquea por mucho que tarde un comando de mutagen
- Un único .exe self-contained, sin dependencias para el usuario final
- Formato de config.json sin cambios (compatible con configs v2)

### v2.1.1 (2026-03-28)
- "Reiniciar Sincronizacion" relee config.json al hacer clic
- Corregida la resolución lenta de conflictos (eliminado el flush bloqueante)

### v2.1.0
- Migrado el inicio automático del Programador de tareas a la carpeta de Inicio

### v2.0.0
- Soporte multi-servidor, configuración de propietario, icono de bandeja con superposición

---

## Dependencias

- [Mutagen CLI](https://mutagen.io/) — **incluida** con la app (sin instalación aparte). Cae al PATH en uso portable/dev
- SSH configurado para los servidores remotos (se recomienda autenticación por clave)
- VS Code (opcional, para la vista de diff de conflictos)

## Licencia

MIT — ver [LICENSE](LICENSE)
