using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Win32;
using MutagenManager.Models;
using MutagenManager.Services;
using MutagenManager.Views;

namespace MutagenManager;

/// <summary>
/// Main application controller.
/// Owns the NotifyIcon, ContextMenu, and coordinates all services.
/// </summary>
public sealed class TrayApplication : IDisposable
{
    // ── Services ──────────────────────────────────────────────────────────────
    private readonly LogService        _log;
    private readonly ConfigService     _configService;
    private readonly MutagenService    _mutagen;
    private readonly AutoStartService  _autoStart;
    private readonly MonitorService    _monitor;
    private readonly MutagenUpdater    _updater;

    // ── Config ────────────────────────────────────────────────────────────────
    private AppConfig _config = new();
    private readonly string _exeDir;
    private readonly string _exePath;
    private readonly bool   _firstRun;

    // ── Tray ──────────────────────────────────────────────────────────────────
    private NotifyIcon        _tray          = null!;
    private ContextMenuStrip  _menu          = null!;
    private ToolStripMenuItem _autoStartItem = null!;

    /// <summary>Per-sync menu item group: root + pause/resume sub-items for state management.</summary>
    private sealed record SyncMenuEntry(
        ToolStripMenuItem Root,
        ToolStripMenuItem PauseItem,
        ToolStripMenuItem ResumeItem
    );
    private readonly Dictionary<string, SyncMenuEntry> _syncItems = [];

    // ── Windows (singletons) ─────────────────────────────────────────────────
    private ConflictWindow?  _conflictWindow;
    private SettingsWindow?  _settingsWindow;
    private StatusWindow?    _statusWindow;
    private LogViewerWindow? _logWindow;

    public TrayApplication()
    {
        _exePath   = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
        _exeDir    = Path.GetDirectoryName(_exePath)!;

        _log           = new LogService();
        _configService = new ConfigService(_exeDir);
        _mutagen       = new MutagenService(_log);
        _autoStart     = new AutoStartService();
        _monitor       = new MonitorService(_mutagen, _log);
        _updater       = new MutagenUpdater(_log, _mutagen);
        _firstRun      = !_configService.Exists();
    }

    public void Initialize()
    {
        IconRenderer.LoadBaseIcon();
        _configService.EnsureExists();          // create config.json next to exe if missing
        _autoStart.ReconcilePath(_exePath);     // fix stale autostart path after a reinstall/move
        LoadConfig();
        BuildTray();
        StartMonitor();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _ = CheckMutagenOnStartupAsync();

        _log.Log($"MutagenManager v3 started — config: {_configService.ConfigPath}");

        // Auto-open Settings on first run (no config.json found)
        if (_firstRun)
        {
            App.Current.Dispatcher.BeginInvoke(() =>
            {
                ShowBalloon("Bienvenido", "No se encontró configuración. Abriendo ajustes…", ToolTipIcon.Info);
                OnOpenSettings();
            });
        }
    }

    // ── Config ────────────────────────────────────────────────────────────────

    private void LoadConfig() => _config = _configService.Load();

    public void ReloadConfig()
    {
        _config = _configService.Load();
        _monitor.RequestImmediateCheck();
        RebuildSyncMenuItems();
    }

    // ── Tray ──────────────────────────────────────────────────────────────────

    private void BuildTray()
    {
        _menu = new ContextMenuStrip { ShowImageMargin = true };

        // Header (disabled label)
        var header = new ToolStripMenuItem("Mutagen Monitor")
        {
            Enabled = false,
            Font    = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold),
        };
        _menu.Items.Add(header);
        _menu.Items.Add(new ToolStripSeparator());

        // Per-sync items (inserted between the two separators above at indices 2+)
        BuildSyncMenuItems();

        _menu.Items.Add(new ToolStripSeparator());

        // Global actions
        AddMenuItem("Ver Estado Global",
            IconRenderer.MakeGlyphIcon("\uE950", Color.FromArgb(0, 120, 215)),
            OnViewGlobalStatus);
        AddMenuItem("Resolver Conflictos",
            IconRenderer.MakeGlyphIcon("\uE71C", Color.FromArgb(200, 60, 60)),
            OnResolveConflicts);

        _menu.Items.Add(new ToolStripSeparator());

        // ── Ajustes sub-menu ──────────────────────────────────────────────────
        var settingsMenu = new ToolStripMenuItem("Ajustes")
        {
            Image = IconRenderer.MakeGlyphIcon("\uE713", Color.FromArgb(80, 80, 80)),
        };

        _autoStartItem = new ToolStripMenuItem("Iniciar con Windows")
        {
            CheckOnClick = false,
            Checked      = _autoStart.IsEnabled(),
            CheckState   = _autoStart.IsEnabled() ? CheckState.Checked : CheckState.Unchecked,
        };
        _autoStartItem.Click += (_, _) => OnToggleAutoStart();
        settingsMenu.DropDownItems.Add(_autoStartItem);

        var restartMonitorItem = new ToolStripMenuItem("Reiniciar Monitor")
        {
            Image = IconRenderer.MakeGlyphIcon("\uE72C", Color.FromArgb(80, 80, 80)),
        };
        restartMonitorItem.Click += (_, _) => OnRestart();
        settingsMenu.DropDownItems.Add(restartMonitorItem);

        var updateMutagenItem = new ToolStripMenuItem("Actualizar Mutagen CLI…")
        {
            Image = IconRenderer.MakeGlyphIcon("", Color.FromArgb(0, 120, 215)),
        };
        updateMutagenItem.Click += (_, _) => OnUpdateMutagen();
        settingsMenu.DropDownItems.Add(updateMutagenItem);

        settingsMenu.DropDownItems.Add(new ToolStripSeparator());

        var configItem = new ToolStripMenuItem("Configuración…")
        {
            Image = IconRenderer.MakeGlyphIcon("\uE713", Color.FromArgb(80, 80, 80)),
        };
        configItem.Click += (_, _) => OnOpenSettings();
        settingsMenu.DropDownItems.Add(configItem);

        var logsItem = new ToolStripMenuItem("Ver Logs")
        {
            Image = IconRenderer.MakeGlyphIcon("\uE7C3", Color.FromArgb(80, 80, 80)),
        };
        logsItem.Click += (_, _) => OnViewLogs();
        settingsMenu.DropDownItems.Add(logsItem);

        var aboutItem = new ToolStripMenuItem("Acerca de…")
        {
            Image = IconRenderer.MakeGlyphIcon("\uE946", Color.FromArgb(80, 80, 80)),
        };
        aboutItem.Click += (_, _) => OnAbout();
        settingsMenu.DropDownItems.Add(aboutItem);

        _menu.Items.Add(settingsMenu);
        _menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Salir")
        {
            Image = IconRenderer.MakeGlyphIcon("\uE8BB", Color.FromArgb(200, 60, 60)),
        };
        exitItem.Click += (_, _) => OnExit();
        _menu.Items.Add(exitItem);

        // ── NotifyIcon ────────────────────────────────────────────────────────
        _tray = new NotifyIcon
        {
            Icon             = IconRenderer.GetTrayIcon(SyncStatusCode.Unknown),
            Text             = "Mutagen Monitor — iniciando…",
            Visible          = true,
            ContextMenuStrip = _menu,
        };

        // Double-click: smart routing — ConflictWindow if problems, else StatusWindow
        _tray.DoubleClick += (_, _) =>
        {
            bool hasProblems = _monitor.CurrentStatuses.Values
                .Any(s => s.Code is SyncStatusCode.Conflict or SyncStatusCode.Error);
            if (hasProblems)
                OnResolveConflicts(null, EventArgs.Empty);
            else
                OnViewGlobalStatus(null, EventArgs.Empty);
        };

        ShowBalloon("Mutagen Monitor",
            $"Monitorizando {_config.Syncs.Count} sincronización(es)", ToolTipIcon.Info);
    }

    private void BuildSyncMenuItems()
    {
        _syncItems.Clear();
        foreach (var sync in _config.Syncs)
        {
            var entry = BuildSyncMenuItem(sync);
            _syncItems[sync.Name] = entry;
            _menu.Items.Add(entry.Root);
        }
    }

    private void RebuildSyncMenuItems()
    {
        // Remove old sync root items
        foreach (var entry in _syncItems.Values)
            _menu.Items.Remove(entry.Root);
        _syncItems.Clear();

        // Insert after header + separator (index 2)
        int insertAt = 2;
        foreach (var sync in _config.Syncs)
        {
            var entry = BuildSyncMenuItem(sync);
            _syncItems[sync.Name] = entry;
            _menu.Items.Insert(insertAt++, entry.Root);
        }
    }

    private SyncMenuEntry BuildSyncMenuItem(SyncConfig sync)
    {
        var root = new ToolStripMenuItem(sync.Name)
        {
            Image = IconRenderer.GetStatusDot(SyncStatusCode.Unknown),
        };

        // Open local folder
        var folderItem = new ToolStripMenuItem("Abrir Carpeta Local")
        {
            Image = IconRenderer.MakeGlyphIcon("\uE8B7", Color.FromArgb(80, 80, 80)),
        };
        folderItem.Click += (_, _) =>
            System.Diagnostics.Process.Start("explorer", sync.LocalPath);
        root.DropDownItems.Add(folderItem);

        root.DropDownItems.Add(new ToolStripSeparator());

        // Pause — will be disabled while already paused
        var pauseItem = new ToolStripMenuItem("Pausar")
        {
            Image = IconRenderer.MakeGlyphIcon("\uE769", Color.FromArgb(255, 152, 0)),
        };
        pauseItem.Click += async (_, _) =>
        {
            await _mutagen.SyncPauseAsync(sync.Name);
            _monitor.RequestImmediateCheck();
        };
        root.DropDownItems.Add(pauseItem);

        // Resume — starts disabled; enabled only when status is Paused
        var resumeItem = new ToolStripMenuItem("Reanudar")
        {
            Image   = IconRenderer.MakeGlyphIcon("\uE768", Color.FromArgb(76, 175, 80)),
            Enabled = false,
        };
        resumeItem.Click += async (_, _) =>
        {
            await _mutagen.SyncResumeAsync(sync.Name);
            _monitor.RequestImmediateCheck();
        };
        root.DropDownItems.Add(resumeItem);

        root.DropDownItems.Add(new ToolStripSeparator());

        // Force flush
        var flushItem = new ToolStripMenuItem("Forzar Sincronización Ahora")
        {
            Image = IconRenderer.MakeGlyphIcon("", Color.FromArgb(0, 120, 215)),
        };
        flushItem.Click += async (_, _) =>
        {
            ShowBalloon("Mutagen Manager", $"Sincronizando '{sync.Name}'…", ToolTipIcon.Info);
            await _mutagen.SyncFlushAsync(sync.Name);
            _monitor.RequestImmediateCheck();
        };
        root.DropDownItems.Add(flushItem);

        root.DropDownItems.Add(new ToolStripSeparator());

        // Restart sync (re-reads config at click time)
        var restartItem = new ToolStripMenuItem("Reiniciar Sincronización")
        {
            Image = IconRenderer.MakeGlyphIcon("\uE72C", Color.FromArgb(80, 80, 80)),
        };
        restartItem.Click += async (_, _) =>
        {
            var fresh     = _configService.Load();
            var freshSync = fresh.Syncs.Find(s => s.Name == sync.Name);
            if (freshSync == null) return;

            ShowBalloon("Mutagen Manager", $"Reiniciando '{sync.Name}'…", ToolTipIcon.Info);
            if (await _mutagen.SyncExistsAsync(sync.Name))
            {
                await _mutagen.SyncTerminateAsync(sync.Name);
                await Task.Delay(2000);
            }
            var ok = await _mutagen.SyncCreateAsync(freshSync, fresh);
            ShowBalloon(ok ? "Éxito" : "Error",
                ok ? $"'{sync.Name}' creada correctamente" : $"No se pudo crear '{sync.Name}'",
                ok ? ToolTipIcon.Info : ToolTipIcon.Error);
            _monitor.RequestImmediateCheck();
        };
        root.DropDownItems.Add(restartItem);

        // Delete sync
        var deleteItem = new ToolStripMenuItem("Eliminar Sincronización")
        {
            Image = IconRenderer.MakeGlyphIcon("\uE74D", Color.FromArgb(200, 60, 60)),
        };
        deleteItem.Click += async (_, _) =>
        {
            var result = MessageBox.Show(
                $"¿Eliminar la sincronización '{sync.Name}'?\n\n" +
                "Esto terminará la sincronización y la eliminará del config.",
                "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            await _mutagen.SyncTerminateAsync(sync.Name);

            var cfg = _configService.Load();
            cfg.Syncs.RemoveAll(s => s.Name == sync.Name);
            _configService.Save(cfg);
            ReloadConfig();

            ShowBalloon("Eliminada", $"'{sync.Name}' eliminada.", ToolTipIcon.Info);
        };
        root.DropDownItems.Add(deleteItem);

        return new SyncMenuEntry(root, pauseItem, resumeItem);
    }

    private void AddMenuItem(string text, System.Drawing.Bitmap icon, EventHandler handler)
    {
        var item = new ToolStripMenuItem(text) { Image = icon };
        item.Click += handler;
        _menu.Items.Add(item);
    }

    // ── Monitor events ────────────────────────────────────────────────────────

    private void StartMonitor()
    {
        _monitor.StatusChanged         += OnStatusChanged;
        _monitor.NotificationRequested += OnNotificationRequested;
        _monitor.Start(_config);
    }

    private void OnStatusChanged(SyncStatus status)
    {
        // UI updates must run on the UI thread
        App.Current.Dispatcher.Invoke(() =>
        {
            if (_syncItems.TryGetValue(status.Name, out var entry))
            {
                entry.Root.Text  = $"{status.Name}  —  {status.DisplayStatus}";
                entry.Root.Image = IconRenderer.GetStatusDot(status.Code);

                // Pause/Resume availability mirrors current state
                bool ready    = status.Code != SyncStatusCode.Unknown;
                bool isPaused = status.Code == SyncStatusCode.Paused;
                entry.PauseItem.Enabled  = ready && !isPaused;
                entry.ResumeItem.Enabled = isPaused;
            }

            UpdateTrayIcon();
        });
    }

    private void UpdateTrayIcon()
    {
        var worstCode = SyncStatusCode.Unknown;
        int active = 0, total = _config.Syncs.Count;

        lock (_monitor)
        {
            foreach (var s in _monitor.CurrentStatuses.Values)
            {
                if (s.Code == SyncStatusCode.Ok) active++;
                if (s.Code > worstCode) worstCode = s.Code;
            }
        }

        _tray.Icon = IconRenderer.GetTrayIcon(worstCode);
        var statusText = worstCode switch
        {
            SyncStatusCode.Ok       => "Sincronizado",
            SyncStatusCode.Paused   => "Pausado",
            SyncStatusCode.Conflict => "CONFLICTO",
            SyncStatusCode.Error    => "Error",
            _                       => "…",
        };

        var tooltip = $"Mutagen Monitor\nEstado: {statusText}\nActivas: {active}/{total}";
        _tray.Text  = tooltip.Length > 63 ? tooltip[..60] + "…" : tooltip;
    }

    private void OnNotificationRequested(string title, string message, string type)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            var icon = type switch
            {
                "Warning" => ToolTipIcon.Warning,
                "Error"   => ToolTipIcon.Error,
                _         => ToolTipIcon.Info,
            };
            ShowBalloon(title, message, icon);
        });
    }

    // ── Menu handlers ─────────────────────────────────────────────────────────

    private void OnViewGlobalStatus(object? sender, EventArgs e)
    {
        if (_statusWindow == null || !_statusWindow.IsLoaded)
        {
            _statusWindow = new StatusWindow(_monitor, _config, _mutagen);
            _statusWindow.Closed += (_, _) => _statusWindow = null;
            _statusWindow.Show();
        }
        else
        {
            _statusWindow.Activate();
        }
    }

    private void OnResolveConflicts(object? sender, EventArgs e)
    {
        if (_conflictWindow == null || !_conflictWindow.IsLoaded)
        {
            _conflictWindow = new ConflictWindow(_config, _mutagen, _log);
            _conflictWindow.Closed += (_, _) =>
            {
                _conflictWindow = null;
                _monitor.RequestImmediateCheck();
            };
            _conflictWindow.Show();
        }
        else
        {
            _conflictWindow.Activate();
        }
    }

    private void OnToggleAutoStart()
    {
        bool current = _autoStart.IsEnabled();
        bool ok      = current ? _autoStart.Disable() : _autoStart.Enable(_exePath);

        if (ok)
        {
            bool nowEnabled = !current;
            _autoStartItem.Checked    = nowEnabled;
            _autoStartItem.CheckState = nowEnabled ? CheckState.Checked : CheckState.Unchecked;
            ShowBalloon(
                current ? "Auto-inicio desactivado" : "Auto-inicio activado",
                current ? "El monitor ya no iniciará con Windows" : "El monitor iniciará con Windows",
                ToolTipIcon.Info);
            _log.Log($"Autostart {(current ? "disabled" : "enabled")}");
        }
    }

    private void OnRestart()
    {
        _log.Log("Restarting…");
        _tray.Visible = false;
        System.Diagnostics.Process.Start(_exePath);
        Application.Current.Shutdown();
    }

    private void OnOpenSettings()
    {
        if (_settingsWindow == null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new SettingsWindow(_config, _configService);
            _settingsWindow.ConfigSaved += OnConfigSaved;
            _settingsWindow.Show();
        }
        else
        {
            _settingsWindow.Activate();
        }
    }

    private void OnConfigSaved(List<SyncConfig> changedSyncs)
    {
        ReloadConfig();
        _monitor.UpdateConfig(_config); // hot-reload notifications + interval

        if (changedSyncs.Count == 0) return;

        var names  = string.Join(", ", changedSyncs.Select(s => $"'{s.Name}'"));
        var result = MessageBox.Show(
            $"Los siguientes syncs han cambiado y necesitan ser recreados en Mutagen para aplicar los cambios:\n\n{names}\n\n¿Recrear ahora?",
            "Aplicar cambios en Mutagen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        _ = RecreateSyncsAsync(changedSyncs);
    }

    private async Task RecreateSyncsAsync(List<SyncConfig> syncs)
    {
        var freshConfig = _configService.Load();

        foreach (var sync in syncs)
        {
            _log.Log($"Recreating sync '{sync.Name}' due to config change");
            ShowBalloon("Recreando sync", $"Aplicando cambios en '{sync.Name}'…", ToolTipIcon.Info);

            if (await _mutagen.SyncExistsAsync(sync.Name))
                await _mutagen.SyncTerminateAsync(sync.Name);

            await Task.Delay(1500);

            var ok = await _mutagen.SyncCreateAsync(sync, freshConfig);

            ShowBalloon(
                ok ? "Sync recreada" : "Error",
                ok ? $"'{sync.Name}' actualizada correctamente." : $"No se pudo recrear '{sync.Name}'. Ver logs.",
                ok ? ToolTipIcon.Info : ToolTipIcon.Error);

            _log.Log($"Sync '{sync.Name}' recreate: {(ok ? "OK" : "FAILED")}");
        }

        _monitor.RequestImmediateCheck();
    }

    private void OnViewLogs()
    {
        if (_logWindow == null || !_logWindow.IsLoaded)
        {
            _logWindow = new LogViewerWindow(_log.LogPath);
            _logWindow.Closed += (_, _) => _logWindow = null;
            _logWindow.Show();
        }
        else
        {
            _logWindow.Activate();
        }
    }

    private void OnUpdateMutagen() => _ = UpdateMutagenAsync();

    private async Task UpdateMutagenAsync()
    {
        if (!_updater.CanSelfUpdate)
        {
            App.Current.Dispatcher.Invoke(() =>
                MessageBox.Show(
                    "mutagen se está usando desde el PATH del sistema, no desde la copia bundleada.\n\n" +
                    "Actualízalo con tu gestor (p.ej. 'winget upgrade Mutagen.Mutagen').",
                    "Actualizar Mutagen CLI", MessageBoxButton.OK, MessageBoxImage.Information));
            return;
        }

        ShowBalloon("Mutagen CLI", "Comprobando última versión…", ToolTipIcon.Info);
        var current = await _mutagen.CheckVersionAsync();
        var latest  = await _updater.GetLatestVersionAsync();

        if (latest == null)
        {
            App.Current.Dispatcher.Invoke(() =>
                MessageBox.Show("No se pudo consultar la última versión (¿sin conexión?).",
                    "Actualizar Mutagen CLI", MessageBoxButton.OK, MessageBoxImage.Warning));
            return;
        }

        var proceed = false;
        App.Current.Dispatcher.Invoke(() =>
        {
            var result = MessageBox.Show(
                $"Versión instalada: {current ?? "desconocida"}\n" +
                $"Última disponible: {latest}\n\n" +
                "RECOMENDACIÓN: actualiza el CLI solo si tienes fallos o mal funcionamiento. " +
                "La versión incluida es estable y probada; actualizar puede introducir cambios no previstos.\n\n" +
                "¿Descargar e instalar la última versión? Se reiniciará el daemon de mutagen.",
                "Actualizar Mutagen CLI", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            proceed = result == MessageBoxResult.Yes;
        });
        if (!proceed) return;

        var progress = new Progress<string>(msg =>
            App.Current.Dispatcher.Invoke(() => ShowBalloon("Mutagen CLI", msg, ToolTipIcon.Info)));

        var ok = await _updater.UpdateAsync(progress);
        App.Current.Dispatcher.Invoke(() =>
            ShowBalloon("Mutagen CLI",
                ok ? "Actualización completada." : "No se pudo actualizar. Ver logs.",
                ok ? ToolTipIcon.Info : ToolTipIcon.Error));

        _monitor.RequestImmediateCheck();
    }

    private void OnAbout() => _ = ShowAboutAsync();

    private async Task ShowAboutAsync()
    {
        var mutagenVersion = await _mutagen.CheckVersionAsync();
        var mutagenLine = mutagenVersion != null ? $"mutagen: {mutagenVersion}" : "mutagen: no encontrado en PATH";
        App.Current.Dispatcher.Invoke(() =>
            MessageBox.Show(
                "Mutagen Manager v3.1\n\n" +
                "Monitor de sincronización Mutagen para Windows.\n" +
                "Detecta conflictos, gestiona sesiones y notifica cambios de estado.\n\n" +
                $"{mutagenLine}\n" +
                $"Logs: {_log.LogPath}",
                "Acerca de Mutagen Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Information));
    }

    private void OnExit()
    {
        _log.Log("MutagenManager exiting");
        _tray.Visible = false;
        Application.Current.Shutdown();
    }

    // ── Startup checks ────────────────────────────────────────────────────────

    private async Task CheckMutagenOnStartupAsync()
    {
        var version = await _mutagen.CheckVersionAsync();
        if (version == null)
        {
            _log.Log("WARNING: mutagen not found in PATH — sync will not work");
            App.Current.Dispatcher.Invoke(() =>
                ShowBalloon("Mutagen no encontrado",
                    "mutagen no está en el PATH. Instálalo para que funcione la sincronización.",
                    ToolTipIcon.Warning));
        }
        else
        {
            _log.Log($"mutagen {version}");
        }
    }

    // ── Power management ──────────────────────────────────────────────────────

    /// <summary>Guards against overlapping resume handlers when several Resume events fire.</summary>
    private volatile bool _resumeHandling;

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            _log.Log("System suspending");
            return;
        }

        if (e.Mode != PowerModes.Resume) return;

        // Multiple hibernate/sleep cycles can fire Resume repeatedly. Only one
        // handler at a time — previously each one restarted the monitor, leaking
        // concurrent loops that flooded the UI thread and froze the tray.
        if (_resumeHandling)
        {
            _log.Log("Resume already being handled — ignoring duplicate event");
            return;
        }
        _resumeHandling = true;
        _log.Log("System resumed — scheduling tray refresh");

        // Run off the UI thread; delay lets network/SSH recover before polling.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(8000);
                App.Current.Dispatcher.Invoke(RefreshAfterResume);
            }
            catch (Exception ex)
            {
                _log.Log($"Resume handling error: {ex.Message}");
            }
            finally
            {
                _resumeHandling = false;
            }
        });
    }

    /// <summary>
    /// Runs on the UI thread. Never blocks (no .Wait): re-asserts the tray icon and
    /// forces an immediate poll. Only restarts the monitor loop if it actually died —
    /// the loop survives suspend/resume on its own, so no teardown is needed normally.
    /// </summary>
    private void RefreshAfterResume()
    {
        try
        {
            _log.Log("Refreshing tray after resume");

            // Re-register tray icon in case the shell restarted during hibernate
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Visible = true;
            }

            // Watchdog: restart only if the loop faulted/exited during suspend
            if (!_monitor.IsRunning)
            {
                _log.Log("Monitor loop not running after resume — restarting");
                _monitor.Start(_config);
            }

            _monitor.RequestImmediateCheck();
            _log.Log("Tray refreshed after resume");
        }
        catch (Exception ex)
        {
            _log.Log($"RefreshAfterResume error: {ex.Message}");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        _tray.BalloonTipIcon  = icon;
        _tray.BalloonTipTitle = title;
        _tray.BalloonTipText  = text;
        _tray.ShowBalloonTip(5000);
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _monitor.StopAsync().GetAwaiter().GetResult();
        _monitor.Dispose();
        _tray.Dispose();
        _menu.Dispose();
    }
}
