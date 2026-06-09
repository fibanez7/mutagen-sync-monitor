using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using MutagenManager.Models;
using MutagenManager.Services;

namespace MutagenManager.Views;

public partial class ConflictWindow : Window
{
    private readonly AppConfig      _config;
    private readonly MutagenService _mutagen;
    private readonly LogService     _log;

    private SyncConfig?   _currentSync;
    private SyncStatus?   _currentStatus;
    private List<ConflictPair> _pairs = [];
    private int _index = 0;

    public ConflictWindow(AppConfig config, MutagenService mutagen, LogService log)
    {
        _config  = config;
        _mutagen = mutagen;
        _log     = log;

        InitializeComponent();
        PopulateSyncSelector();
    }

    // ── Initialisation ────────────────────────────────────────────────────────

    private void PopulateSyncSelector()
    {
        SyncSelector.Items.Clear();
        SyncSelector.Items.Add("(Todas)");
        foreach (var s in _config.Syncs)
            SyncSelector.Items.Add(s.Name);

        SyncSelector.SelectedIndex = 0;
    }

    private async void SyncSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => await LoadConflictsAsync();

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        => await LoadConflictsAsync();

    private async Task LoadConflictsAsync()
    {
        ProgressLabel.Text = "Consultando mutagen…";
        _pairs.Clear();
        _index = 0;

        var selectedName = SyncSelector.SelectedItem as string;

        // Determine which syncs to check
        var syncsToCheck = (selectedName == null || selectedName == "(Todas)")
            ? _config.Syncs
            : _config.Syncs.Where(s => s.Name == selectedName).ToList();

        var monitor = new MonitorService(_mutagen, _log);
        var allPairs = new List<(ConflictPair Pair, SyncConfig Sync, SyncStatus Status)>();

        foreach (var sync in syncsToCheck)
        {
            var status = await monitor.GetStatusAsync(sync.Name);
            foreach (var pair in status.ConflictPairs)
                allPairs.Add((pair, sync, status));

            // Also show scan/transition problems in the label
        }

        if (allPairs.Count == 0)
        {
            ProgressLabel.Text = "✔ Sin conflictos detectados.";
            ClearDetail();
            return;
        }

        // Store enriched pairs: we need sync context per pair
        _pairs = allPairs.Select(x => x.Pair).ToList();
        // Keep sync context for each pair
        _pairContext = allPairs.Select(x => (x.Sync, x.Status)).ToList();
        _currentSync   = allPairs[0].Sync;
        _currentStatus = allPairs[0].Status;

        ShowCurrent();
    }

    private List<(SyncConfig Sync, SyncStatus Status)> _pairContext = [];

    // ── Navigation ────────────────────────────────────────────────────────────

    private void ShowCurrent()
    {
        if (_pairs.Count == 0) { ClearDetail(); return; }

        _currentSync   = _pairContext[_index].Sync;
        _currentStatus = _pairContext[_index].Status;
        var pair        = _pairs[_index];

        ProgressLabel.Text = $"Conflicto {_index + 1} de {_pairs.Count}  —  sync: {_currentSync.Name}";
        FilePathLabel.Text = pair.FilePath;
        AlphaRaw.Text      = string.IsNullOrEmpty(pair.RawAlpha) ? "(sin info)" : pair.RawAlpha;
        BetaRaw.Text       = string.IsNullOrEmpty(pair.RawBeta)  ? "(sin info)" : pair.RawBeta;
        AlphaDate.Text     = "Cargando fecha...";
        BetaDate.Text      = "Cargando fecha...";

        LoadDatesAsync(pair).ConfigureAwait(false);
    }

    private async Task LoadDatesAsync(ConflictPair pair)
    {
        try
        {
            var localPath = Path.Combine(_currentSync!.LocalPath, pair.FilePath.Replace('/', '\\'));
            if (File.Exists(localPath))
                AlphaDate.Text = $"Modificado: {File.GetLastWriteTime(localPath):dd/MM/yyyy HH:mm:ss}";
            else
                AlphaDate.Text = "(archivo no encontrado localmente)";

            // Remote date via SSH
            var server = GetServer();
            if (server != null)
            {
                var remotePath = $"{_currentSync.RemotePath}/{pair.FilePath}".Replace('\\', '/');
                var date = await _mutagen.GetRemoteFileDateAsync(server.Host, server.Port, server.User, remotePath);
                BetaDate.Text = date.HasValue
                    ? $"Modificado: {date.Value:dd/MM/yyyy HH:mm:ss}"
                    : "No se pudo obtener fecha remota";
            }
            else
            {
                BetaDate.Text = "(servidor no configurado)";
            }
        }
        catch (Exception ex)
        {
            _log.Log($"LoadDates failed: {ex.Message}");
            BetaDate.Text = "No se pudo obtener fecha remota";
        }
    }

    private void ClearDetail()
    {
        FilePathLabel.Text = "";
        AlphaRaw.Text = BetaRaw.Text = AlphaDate.Text = BetaDate.Text = "";
    }

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        if (_index > 0) { _index--; ShowCurrent(); }
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_index < _pairs.Count - 1) { _index++; ShowCurrent(); }
    }

    // ── Conflict resolution ───────────────────────────────────────────────────

    private async void UseLocal_Click(object sender, RoutedEventArgs e)
        => await ResolveCurrentAsync(useLocal: true);

    private async void UseRemote_Click(object sender, RoutedEventArgs e)
        => await ResolveCurrentAsync(useLocal: false);

    private async Task ResolveCurrentAsync(bool useLocal)
    {
        if (_pairs.Count == 0) return;
        var pair = _pairs[_index];

        SetBusy(true, useLocal ? "Aplicando versión local…" : "Aplicando versión remota…");
        try
        {
            if (useLocal)
            {
                // Delete remote file so mutagen propagates local version
                var server = GetServer();
                if (server == null)
                {
                    ShowError("No se puede usar la versión local: el servidor no está configurado para esta sync.");
                    return;
                }
                var remotePath = $"{_currentSync!.RemotePath}/{pair.FilePath}".Replace('\\', '/');
                var ok = await _mutagen.DeleteRemoteFileAsync(server.Host, server.Port, server.User, remotePath);
                if (!ok)
                {
                    ShowError($"No se pudo borrar el archivo remoto vía SSH:\n{remotePath}\n\n" +
                              "Causas habituales: cliente SSH (ssh.exe) no instalado, autenticación por clave no configurada " +
                              "(se usa BatchMode, sin contraseña), o el servidor rechaza la conexión.");
                    return;
                }
            }
            else
            {
                // Delete local file so mutagen propagates remote version
                var localPath = Path.Combine(_currentSync!.LocalPath, pair.FilePath.Replace('/', '\\'));
                if (File.Exists(localPath))
                    File.Delete(localPath);
            }

            await _mutagen.SyncResetAsync(_currentSync!.Name);
            RemoveCurrentAndAdvance();
        }
        catch (Exception ex)
        {
            _log.Log($"ResolveCurrent (useLocal={useLocal}) failed: {ex}");
            ShowError($"Error resolviendo el conflicto:\n{ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void AllLocal_Click(object sender, RoutedEventArgs e)
    {
        if (_pairs.Count == 0) return;
        var result = MessageBox.Show(this,
            $"¿Usar la versión LOCAL para todos los {_pairs.Count} conflictos restantes?",
            "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        SetBusy(true, "Aplicando versión local a todos…");
        try
        {
            int failed = 0;
            var resetSyncs = new HashSet<string>();
            for (int i = _index; i < _pairs.Count; i++)
            {
                var pair   = _pairs[i];
                var sync   = _pairContext[i].Sync;
                var server = _config.Servers.TryGetValue(sync.Server, out var s) ? s : null;
                if (server == null) { failed++; continue; }

                var remotePath = $"{sync.RemotePath}/{pair.FilePath}".Replace('\\', '/');
                var ok = await _mutagen.DeleteRemoteFileAsync(server.Host, server.Port, server.User, remotePath);
                if (ok) resetSyncs.Add(sync.Name); else failed++;
            }

            foreach (var name in resetSyncs)
                await _mutagen.SyncResetAsync(name);

            if (failed > 0)
                ShowError($"{failed} archivo(s) remoto(s) no se pudieron borrar vía SSH. Revisa el cliente SSH y la autenticación por clave.");

            ProgressLabel.Text = failed == 0
                ? "✔ Todos los conflictos resueltos (versión local)."
                : $"⚠ Resueltos con {failed} fallo(s) (versión local).";
            _pairs.Clear();
            _pairContext.Clear();
            ClearDetail();
        }
        catch (Exception ex)
        {
            _log.Log($"AllLocal failed: {ex}");
            ShowError($"Error resolviendo conflictos:\n{ex.Message}");
        }
        finally { SetBusy(false); }
    }

    private async void AllRemote_Click(object sender, RoutedEventArgs e)
    {
        if (_pairs.Count == 0) return;
        var result = MessageBox.Show(this,
            $"¿Usar la versión REMOTA para todos los {_pairs.Count} conflictos restantes?",
            "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        SetBusy(true, "Aplicando versión remota a todos…");
        try
        {
            var resetSyncs = new HashSet<string>();
            for (int i = _index; i < _pairs.Count; i++)
            {
                var pair  = _pairs[i];
                var sync  = _pairContext[i].Sync;
                var localPath = Path.Combine(sync.LocalPath, pair.FilePath.Replace('/', '\\'));
                if (File.Exists(localPath)) File.Delete(localPath);
                resetSyncs.Add(sync.Name);
            }

            foreach (var name in resetSyncs)
                await _mutagen.SyncResetAsync(name);

            ProgressLabel.Text = "✔ Todos los conflictos resueltos (versión remota).";
            _pairs.Clear();
            _pairContext.Clear();
            ClearDetail();
        }
        catch (Exception ex)
        {
            _log.Log($"AllRemote failed: {ex}");
            ShowError($"Error resolviendo conflictos:\n{ex.Message}");
        }
        finally { SetBusy(false); }
    }

    private async void VsCode_Click(object sender, RoutedEventArgs e)
    {
        if (_pairs.Count == 0) return;
        var pair   = _pairs[_index];
        var server = GetServer();
        if (server == null) { ShowError("Servidor no configurado."); return; }

        var localPath = Path.Combine(_currentSync!.LocalPath, pair.FilePath.Replace('/', '\\'));
        var tempDir   = Path.Combine(Path.GetTempPath(), "mutagen-conflicts");

        VsCodeBtn.IsEnabled = false;
        VsCodeBtn.Content   = "Descargando...";
        try
        {
            Directory.CreateDirectory(tempDir);
            var tempFile = Path.Combine(tempDir, $"REMOTE_{Path.GetFileName(pair.FilePath)}");

            var remotePath = $"{_currentSync.RemotePath}/{pair.FilePath}".Replace('\\', '/');
            var ok = await _mutagen.DownloadRemoteFileAsync(server.Host, server.Port, server.User, remotePath, tempFile);
            if (!ok)
            {
                ShowError("No se pudo descargar el archivo remoto vía SSH/SCP.\n\n" +
                          "Causas habituales: cliente SSH (scp.exe) no instalado o autenticación por clave no configurada.");
                return;
            }

            LaunchVsCodeDiff(localPath, tempFile);
        }
        catch (Exception ex)
        {
            _log.Log($"VsCode diff failed: {ex}");
            ShowError($"Error abriendo la comparación:\n{ex.Message}");
        }
        finally
        {
            VsCodeBtn.IsEnabled = true;
            VsCodeBtn.Content   = "⬡ Comparar VS Code";
        }
    }

    /// <summary>
    /// Lanza "code --diff". "code" es code.cmd en el PATH, así que NO se puede arrancar con
    /// UseShellExecute=false (lanza Win32Exception y, en un handler async void sin try/catch,
    /// reventaba la app). Con UseShellExecute=true el shell resuelve el .cmd del PATH.
    /// </summary>
    private void LaunchVsCodeDiff(string localPath, string remoteTemp)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("code")
            {
                Arguments       = $"--diff \"{localPath}\" \"{remoteTemp}\"",
                UseShellExecute = true,
                CreateNoWindow  = true,
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            _log.Log($"No se pudo lanzar VS Code: {ex.Message}");
            ShowError("No se pudo abrir Visual Studio Code.\n\n" +
                      "Asegúrate de que VS Code está instalado y que el comando 'code' está en el PATH " +
                      "(en VS Code: Ctrl+Shift+P → \"Shell Command: Install 'code' command in PATH\").");
        }
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSync == null) return;
        SetBusy(true, $"Reseteando sync '{_currentSync.Name}'…");
        try
        {
            await _mutagen.SyncResetAsync(_currentSync.Name);
            await _mutagen.SyncFlushAsync(_currentSync.Name);
            await LoadConflictsAsync();
        }
        catch (Exception ex)
        {
            _log.Log($"Reset sync failed: {ex}");
            ShowError($"Error reseteando la sync:\n{ex.Message}");
        }
        finally { SetBusy(false); }
    }

    private async void Skip_Click(object sender, RoutedEventArgs e)
    {
        if (_index < _pairs.Count - 1) { _index++; ShowCurrent(); }
        else { ProgressLabel.Text = "No quedan más conflictos."; ClearDetail(); }
        await Task.CompletedTask;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RemoveCurrentAndAdvance()
    {
        _pairs.RemoveAt(_index);
        _pairContext.RemoveAt(_index);

        if (_pairs.Count == 0) { ProgressLabel.Text = "✔ Todos los conflictos resueltos."; ClearDetail(); return; }
        if (_index >= _pairs.Count) _index = _pairs.Count - 1;
        ShowCurrent();
    }

    private ServerConfig? GetServer()
    {
        if (_currentSync == null) return null;
        return _config.Servers.TryGetValue(_currentSync.Server, out var s) ? s : null;
    }

    /// <summary>Deshabilita las acciones mientras corre una operación, para no encadenar clicks ni dejar la UI muda.</summary>
    private void SetBusy(bool busy, string? message = null)
    {
        ActionsPanel.IsEnabled = !busy;
        GlobalPanel.IsEnabled  = !busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
        if (message != null) ProgressLabel.Text = message;
    }

    /// <summary>MessageBox de error con owner (esta ventana) para que nunca quede oculto detrás.</summary>
    private void ShowError(string message)
        => MessageBox.Show(this, message, "Mutagen Manager",
                           MessageBoxButton.OK, MessageBoxImage.Warning);
}
