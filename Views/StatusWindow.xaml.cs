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
    private readonly ObservableCollection<SyncStatusRow> _rows = [];

    public StatusWindow(MonitorService monitor, AppConfig config)
    {
        _monitor = monitor;
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
