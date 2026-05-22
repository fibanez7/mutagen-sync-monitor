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
        var pair   = _pairs[_index];
        var server = GetServer();

        if (useLocal)
        {
            // Delete remote file so mutagen propagates local version
            if (server != null)
            {
                var remotePath = $"{_currentSync!.RemotePath}/{pair.FilePath}".Replace('\\', '/');
                await _mutagen.DeleteRemoteFileAsync(server.Host, server.Port, server.User, remotePath);
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

    private async void AllLocal_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            $"¿Usar la versión LOCAL para todos los {_pairs.Count} conflictos restantes?",
            "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        for (int i = _index; i < _pairs.Count; i++)
        {
            var pair   = _pairs[i];
            var sync   = _pairContext[i].Sync;
            var server = _config.Servers.TryGetValue(sync.Server, out var s) ? s : null;
            if (server != null)
            {
                var remotePath = $"{sync.RemotePath}/{pair.FilePath}".Replace('\\', '/');
                await _mutagen.DeleteRemoteFileAsync(server.Host, server.Port, server.User, remotePath);
            }
        }
        await _mutagen.SyncResetAsync(_currentSync!.Name);
        ProgressLabel.Text = "✔ Todos los conflictos resueltos (versión local).";
        _pairs.Clear();
        ClearDetail();
    }

    private async void AllRemote_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            $"¿Usar la versión REMOTA para todos los {_pairs.Count} conflictos restantes?",
            "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        for (int i = _index; i < _pairs.Count; i++)
        {
            var pair  = _pairs[i];
            var sync  = _pairContext[i].Sync;
            var localPath = Path.Combine(sync.LocalPath, pair.FilePath.Replace('/', '\\'));
            if (File.Exists(localPath)) File.Delete(localPath);
        }
        await _mutagen.SyncResetAsync(_currentSync!.Name);
        ProgressLabel.Text = "✔ Todos los conflictos resueltos (versión remota).";
        _pairs.Clear();
        ClearDetail();
    }

    private async void VsCode_Click(object sender, RoutedEventArgs e)
    {
        if (_pairs.Count == 0) return;
        var pair   = _pairs[_index];
        var server = GetServer();
        if (server == null) { MessageBox.Show("Servidor no configurado."); return; }

        var localPath = Path.Combine(_currentSync!.LocalPath, pair.FilePath.Replace('/', '\\'));
        var tempDir   = Path.Combine(Path.GetTempPath(), "mutagen-conflicts");
        Directory.CreateDirectory(tempDir);
        var tempFile  = Path.Combine(tempDir, $"REMOTE_{Path.GetFileName(pair.FilePath)}");

        VsCodeBtn.IsEnabled = false;
        VsCodeBtn.Content   = "Descargando...";

        var remotePath = $"{_currentSync.RemotePath}/{pair.FilePath}".Replace('\\', '/');
        var ok = await _mutagen.DownloadRemoteFileAsync(server.Host, server.Port, server.User, remotePath, tempFile);

        VsCodeBtn.IsEnabled = true;
        VsCodeBtn.Content   = "⬡ Comparar VS Code";

        if (!ok) { MessageBox.Show("No se pudo descargar el archivo remoto."); return; }

        System.Diagnostics.Process.Start("code", $"--diff \"{localPath}\" \"{tempFile}\"");
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSync == null) return;
        await _mutagen.SyncResetAsync(_currentSync.Name);
        await _mutagen.SyncFlushAsync(_currentSync.Name);
        await LoadConflictsAsync();
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
}
