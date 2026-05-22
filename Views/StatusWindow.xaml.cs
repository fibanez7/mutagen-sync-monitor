using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using MutagenManager.Models;
using MutagenManager.Services;
using WpfBrush  = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor  = System.Windows.Media.Color;
using WpfSolidBrush = System.Windows.Media.SolidColorBrush;

namespace MutagenManager.Views;

public partial class StatusWindow : Window
{
    private readonly MonitorService _monitor;
    private readonly MutagenService _mutagen;
    private readonly System.Collections.Generic.HashSet<string> _configNames;
    private readonly ObservableCollection<SyncStatusRow> _rows = [];

    public StatusWindow(MonitorService monitor, AppConfig config, MutagenService mutagen)
    {
        _monitor = monitor;
        _mutagen = mutagen;
        _configNames = [.. config.Syncs.Select(s => s.Name)];
        InitializeComponent();

        // Seed one row per configured sync (Unknown state until first poll)
        foreach (var sync in config.Syncs)
            _rows.Add(new SyncStatusRow(sync.Name, sync.LocalPath));

        StatusList.ItemsSource = _rows;

        // Apply already-known statuses from the monitor
        foreach (var kvp in monitor.CurrentStatuses)
        {
            var row = _rows.FirstOrDefault(r => r.Name == kvp.Key);
            row?.Update(kvp.Value);
        }

        _monitor.StatusChanged += OnStatusChangedExternal;
        Closed += (_, _) => _monitor.StatusChanged -= OnStatusChangedExternal;

        UpdateTimestamp();
        _ = DetectOrphansAsync();
    }

    /// <summary>
    /// Lists every session in the mutagen daemon and surfaces those NOT in config.json as
    /// orphan rows (e.g. left running after a sync was renamed/removed only in config).
    /// </summary>
    private async System.Threading.Tasks.Task DetectOrphansAsync()
    {
        var all = await _mutagen.GetAllSessionNamesAsync();
        var orphans = all.Where(n => !_configNames.Contains(n)).Distinct().ToList();

        Dispatcher.Invoke(() =>
        {
            // Drop orphan rows that no longer apply
            for (int i = _rows.Count - 1; i >= 0; i--)
                if (_rows[i].IsOrphan && !orphans.Contains(_rows[i].Name))
                    _rows.RemoveAt(i);

            // Add newly found orphans
            foreach (var name in orphans)
                if (!_rows.Any(r => r.Name == name))
                {
                    var row = new SyncStatusRow(name, "(sesión activa sin entrada en config.json)");
                    row.MarkOrphan();
                    _rows.Add(row);
                }

            TerminateOrphansBtn.IsEnabled = _rows.Any(r => r.IsOrphan);
        });
    }

    private async void TerminateOrphans_Click(object sender, RoutedEventArgs e)
    {
        var orphans = _rows.Where(r => r.IsOrphan).Select(r => r.Name).ToList();
        if (orphans.Count == 0) return;

        var result = MessageBox.Show(
            $"Estas sesiones de mutagen están activas pero NO en config.json:\n\n  {string.Join("\n  ", orphans)}\n\n" +
            "¿Terminarlas ahora? (mutagen sync terminate)",
            "Sesiones huérfanas", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        TerminateOrphansBtn.IsEnabled = false;
        foreach (var name in orphans)
            await _mutagen.SyncTerminateAsync(name);

        await DetectOrphansAsync();
        _monitor.RequestImmediateCheck();
    }

    // Raised on thread-pool; dispatch to UI thread
    private void OnStatusChangedExternal(SyncStatus status)
    {
        Dispatcher.Invoke(() =>
        {
            var row = _rows.FirstOrDefault(r => r.Name == status.Name);
            row?.Update(status);

            // Refresh detail pane if the updated sync is selected
            if (StatusList.SelectedItem is SyncStatusRow sel && sel.Name == status.Name)
                ShowDetail(row);

            UpdateTimestamp();
        });
    }

    private void StatusList_SelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
        => ShowDetail(StatusList.SelectedItem as SyncStatusRow);

    private void ShowDetail(SyncStatusRow? row)
        => DetailText.Text = row?.DetailText ?? "";

    private void UpdateTimestamp()
        => LastUpdatedLabel.Text = $"Última actualización: {DateTime.Now:HH:mm:ss}";

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _monitor.RequestImmediateCheck();
        LastUpdatedLabel.Text = "Actualizando…";
        _ = DetectOrphansAsync();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

// ── View-model ────────────────────────────────────────────────────────────────

public class SyncStatusRow : INotifyPropertyChanged
{
    private string _displayStatus = "Cargando…";
    private string _conflictsText = "";
    private string _problemsText  = "";
    private string _detailText    = "";
    private WpfBrush _statusBrush = WpfBrushes.Gray;

    public string Name      { get; }
    public string LocalPath { get; }
    public bool   IsOrphan  { get; private set; }

    /// <summary>Marks this row as a daemon session not present in config.json.</summary>
    public void MarkOrphan()
    {
        IsOrphan      = true;
        DisplayStatus = "⚠ Huérfana (no en config)";
        StatusBrush   = new WpfSolidBrush(WpfColor.FromRgb(244, 67, 54));
        DetailText    = "Sesión activa en el daemon de mutagen sin entrada correspondiente en " +
                        "config.json.\nProbablemente quedó tras renombrar o eliminar un sync solo en el " +
                        "config.\nUsa 'Terminar huérfanas' para cerrarla, o recréala en Ajustes si la quieres conservar.";
    }

    public string    DisplayStatus { get => _displayStatus; set { _displayStatus = value; Notify(); } }
    public string    ConflictsText { get => _conflictsText; set { _conflictsText = value; Notify(); } }
    public string    ProblemsText  { get => _problemsText;  set { _problemsText  = value; Notify(); } }
    public string    DetailText    { get => _detailText;    set { _detailText    = value; Notify(); } }
    public WpfBrush  StatusBrush   { get => _statusBrush;   set { _statusBrush   = value; Notify(); } }

    public SyncStatusRow(string name, string localPath)
    {
        Name      = name;
        LocalPath = localPath;
    }

    public void Update(SyncStatus status)
    {
        DisplayStatus = status.DisplayStatus;
        ConflictsText = status.Conflicts > 0
            ? $"{status.Conflicts} conflicto(s)" : "";
        ProblemsText  = (status.ScanProblems + status.TransitionProblems) > 0
            ? $"{status.ScanProblems + status.TransitionProblems} problema(s)" : "";
        StatusBrush   = CodeToBrush(status.Code);

        // Build the detail text shown in the bottom panel
        var sb = new StringBuilder();
        sb.AppendLine($"Estado raw: {status.RawStatus}");

        if (status.ScanProblems > 0)
        {
            sb.AppendLine($"\nProblemas de lectura ({status.ScanProblems}):");
            foreach (var d in status.ScanDetails)
                sb.AppendLine($"  • {d}");
        }
        if (status.TransitionProblems > 0)
        {
            sb.AppendLine($"\nProblemas de escritura ({status.TransitionProblems}):");
            foreach (var d in status.TransitionDetails)
                sb.AppendLine($"  • {d}");
        }
        if (status.Conflicts > 0)
        {
            sb.AppendLine($"\nConflictos ({status.Conflicts}):");
            foreach (var p in status.ConflictPairs)
                sb.AppendLine($"  • {p.FilePath}");
        }

        DetailText = sb.ToString().TrimEnd();
    }

    private static WpfBrush CodeToBrush(SyncStatusCode code) => code switch
    {
        SyncStatusCode.Ok       => new WpfSolidBrush(WpfColor.FromRgb(76,  175, 80)),
        SyncStatusCode.Paused   => new WpfSolidBrush(WpfColor.FromRgb(255, 152, 0)),
        SyncStatusCode.Conflict => new WpfSolidBrush(WpfColor.FromRgb(244, 67,  54)),
        SyncStatusCode.Error    => new WpfSolidBrush(WpfColor.FromRgb(244, 67,  54)),
        _                       => WpfBrushes.Gray,
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? prop = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}
