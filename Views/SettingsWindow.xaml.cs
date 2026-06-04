using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MutagenManager.Models;
using MutagenManager.Services;

namespace MutagenManager.Views;

/// <summary>Row model for the server ListView.</summary>
public class ServerRow
{
    public string Key          { get; set; } = "";
    public string Host         { get; set; } = "";
    public int    Port         { get; set; }
    public string User         { get; set; } = "";
    public string? DefaultOwner { get; set; }
    public string? DefaultGroup { get; set; }
}

public partial class SettingsWindow : Window
{
    private readonly ConfigService _configService;
    private AppConfig _config;

    // Raised when the user saves. Args:
    //   changedSyncs  — existing syncs modified in a way that needs mutagen recreate.
    //   orphanedNames — sync names that existed at open but are gone now (renamed or
    //                   deleted); their daemon sessions should be terminated.
    public event Action<List<SyncConfig>, List<string>>? ConfigSaved;

    private readonly ObservableCollection<SyncConfig> _syncs  = [];
    private readonly ObservableCollection<ServerRow>  _servers = [];

    // Snapshot of syncs as they were when the window opened, for change detection
    private readonly List<SyncConfig> _originalSyncs = [];

    public SettingsWindow(AppConfig config, ConfigService configService)
    {
        _config        = config;
        _configService = configService;

        InitializeComponent();
        PopulateAll();

        // Snapshot for change detection
        _originalSyncs = [.. _config.Syncs.Select(s => new SyncConfig
        {
            Name         = s.Name,
            Server       = s.Server,
            LocalPath    = s.LocalPath,
            RemotePath   = s.RemotePath,
            Ignores      = [.. s.Ignores],
            DefaultOwner = s.DefaultOwner,
            DefaultGroup = s.DefaultGroup,
        })];

        _servers.CollectionChanged += (_, _) => UpdateAddSyncBtnState();
    }

    // ── Populate ──────────────────────────────────────────────────────────────

    private void PopulateAll()
    {
        // Syncs
        _syncs.Clear();
        foreach (var s in _config.Syncs) _syncs.Add(s);
        SyncList.ItemsSource = _syncs;

        // Servers
        _servers.Clear();
        foreach (var kv in _config.Servers)
            _servers.Add(new ServerRow
            {
                Key          = kv.Key,
                Host         = kv.Value.Host,
                Port         = kv.Value.Port,
                User         = kv.Value.User,
                DefaultOwner = kv.Value.DefaultOwner,
                DefaultGroup = kv.Value.DefaultGroup,
            });
        ServerList.ItemsSource = _servers;
        UpdateAddSyncBtnState();

        // General
        SelectComboByTag(ModeCombo, _config.Defaults.Mode);
        FileModeBox.Text  = _config.Defaults.FileMode;
        DirModeBox.Text   = _config.Defaults.DirectoryMode;
        IntervalBox.Text  = _config.Defaults.CheckInterval.ToString();

        // Notifications
        NotifEnabled.IsChecked    = _config.Notifications.Enabled;
        NotifSound.IsChecked      = _config.Notifications.Sound;
        NotifConflict.IsChecked   = _config.Notifications.ShowOnConflict;
        NotifDisconnect.IsChecked = _config.Notifications.ShowOnDisconnect;
        NotifResume.IsChecked     = _config.Notifications.ShowOnResume;
    }

    // ── Sync tab ──────────────────────────────────────────────────────────────

    private void SyncList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool sel = SyncList.SelectedItem != null;
        EditSyncBtn.IsEnabled   = sel;
        DeleteSyncBtn.IsEnabled = sel;
    }

    private void AddSync_Click(object sender, RoutedEventArgs e)
    {
        // Use the live server list (_servers), not _config.Servers — the latter is only
        // updated on Save, so servers added this session wouldn't appear until restart.
        var dialog = new SyncEditDialog(null, _servers.Select(s => s.Key).ToList())
            { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            _syncs.Add(dialog.Result);
            StatusLabel.Text = $"Sync '{dialog.Result.Name}' añadida.";
        }
    }

    private void EditSync_Click(object sender, RoutedEventArgs e)
    {
        if (SyncList.SelectedItem is not SyncConfig selected) return;
        var dialog = new SyncEditDialog(selected, _servers.Select(s => s.Key).ToList())
            { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            var idx = _syncs.IndexOf(selected);
            _syncs[idx] = dialog.Result;
            StatusLabel.Text = $"Sync '{dialog.Result.Name}' actualizada.";
        }
    }

    private void DeleteSync_Click(object sender, RoutedEventArgs e)
    {
        if (SyncList.SelectedItem is not SyncConfig selected) return;
        var result = MessageBox.Show(
            $"¿Eliminar la sincronización '{selected.Name}'?\n(Al guardar se terminará también su sesión de Mutagen.)",
            "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            _syncs.Remove(selected);
            StatusLabel.Text = $"Sync '{selected.Name}' eliminada del config.";
        }
    }

    // ── Server tab ────────────────────────────────────────────────────────────

    private void ServerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool sel = ServerList.SelectedItem != null;
        EditServerBtn.IsEnabled   = sel;
        DeleteServerBtn.IsEnabled = sel;
    }

    private void AddServer_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ServerEditDialog(null, null)
            { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        if (dialog.ShowDialog() == true && dialog.ResultKey != null && dialog.ResultConfig != null)
        {
            _servers.Add(new ServerRow
            {
                Key          = dialog.ResultKey,
                Host         = dialog.ResultConfig.Host,
                Port         = dialog.ResultConfig.Port,
                User         = dialog.ResultConfig.User,
                DefaultOwner = dialog.ResultConfig.DefaultOwner,
                DefaultGroup = dialog.ResultConfig.DefaultGroup,
            });
            StatusLabel.Text = $"Servidor '{dialog.ResultKey}' añadido.";
        }
    }

    private void EditServer_Click(object sender, RoutedEventArgs e)
    {
        if (ServerList.SelectedItem is not ServerRow row) return;
        var cfg = new ServerConfig
        {
            Host = row.Host, Port = row.Port, User = row.User,
            DefaultOwner = row.DefaultOwner, DefaultGroup = row.DefaultGroup,
        };
        var dialog = new ServerEditDialog(row.Key, cfg)
            { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        if (dialog.ShowDialog() == true && dialog.ResultKey != null && dialog.ResultConfig != null)
        {
            var idx = _servers.IndexOf(row);
            _servers[idx] = new ServerRow
            {
                Key          = dialog.ResultKey,
                Host         = dialog.ResultConfig.Host,
                Port         = dialog.ResultConfig.Port,
                User         = dialog.ResultConfig.User,
                DefaultOwner = dialog.ResultConfig.DefaultOwner,
                DefaultGroup = dialog.ResultConfig.DefaultGroup,
            };
            StatusLabel.Text = $"Servidor '{dialog.ResultKey}' actualizado.";
        }
    }

    private void DeleteServer_Click(object sender, RoutedEventArgs e)
    {
        if (ServerList.SelectedItem is not ServerRow row) return;
        var result = MessageBox.Show(
            $"¿Eliminar el servidor '{row.Key}'?",
            "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            _servers.Remove(row);
            StatusLabel.Text = $"Servidor '{row.Key}' eliminado.";
        }
    }

    // ── Save / Cancel ─────────────────────────────────────────────────────────

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Validate interval
        if (!int.TryParse(IntervalBox.Text.Trim(), out int interval) || interval < 5)
        {
            MessageBox.Show("El intervalo de comprobación debe ser un número entero ≥ 5.",
                "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Build updated config
        _config.Syncs = [.. _syncs];

        _config.Servers = [];
        foreach (var row in _servers)
            _config.Servers[row.Key] = new ServerConfig
            {
                Host = row.Host, Port = row.Port, User = row.User,
                DefaultOwner = row.DefaultOwner, DefaultGroup = row.DefaultGroup,
            };

        _config.Defaults = new DefaultsConfig
        {
            Mode             = (ModeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "two-way-safe",
            FileMode         = FileModeBox.Text.Trim(),
            DirectoryMode    = DirModeBox.Text.Trim(),
            CheckInterval    = interval,
        };

        _config.Notifications = new NotificationsConfig
        {
            Enabled         = NotifEnabled.IsChecked   == true,
            Sound           = NotifSound.IsChecked     == true,
            ShowOnConflict  = NotifConflict.IsChecked  == true,
            ShowOnDisconnect= NotifDisconnect.IsChecked== true,
            ShowOnResume    = NotifResume.IsChecked    == true,
        };

        _configService.Save(_config);
        StatusLabel.Text = "✔ Configuración guardada.";

        // Detect which existing syncs changed (need mutagen recreate) and which
        // disappeared (renamed/deleted → orphan daemon session to terminate).
        var changedSyncs  = DetectChangedSyncs(_config.Syncs);
        var orphanedNames = DetectOrphanedSessions(_config.Syncs);
        ConfigSaved?.Invoke(changedSyncs, orphanedNames);
    }

    /// <summary>
    /// Sync names that existed when the window opened but are no longer present
    /// (the user renamed or deleted them). Their mutagen sessions are now orphaned.
    /// </summary>
    private List<string> DetectOrphanedSessions(List<SyncConfig> newSyncs)
    {
        var newNames = new HashSet<string>(newSyncs.Select(s => s.Name));
        return _originalSyncs
            .Where(o => !newNames.Contains(o.Name))
            .Select(o => o.Name)
            .ToList();
    }

    /// <summary>
    /// Returns syncs that mutagen needs to (re)create on save:
    ///   - brand-new syncs (not in _originalSyncs) → must be created in the daemon,
    ///   - existing syncs changed in a way that requires recreate (ignores, paths,
    ///     mode, ownership).
    /// RecreateSyncsAsync terminates-if-exists then creates, so it handles both: a new
    /// sync simply skips the terminate step.
    /// </summary>
    private List<SyncConfig> DetectChangedSyncs(List<SyncConfig> newSyncs)
    {
        var changed = new List<SyncConfig>();
        foreach (var newSync in newSyncs)
        {
            var original = _originalSyncs.Find(o => o.Name == newSync.Name);
            if (original == null) { changed.Add(newSync); continue; } // new sync → create it

            bool differs =
                original.Server       != newSync.Server       ||
                original.LocalPath    != newSync.LocalPath    ||
                original.RemotePath   != newSync.RemotePath   ||
                original.DefaultOwner != newSync.DefaultOwner ||
                original.DefaultGroup != newSync.DefaultGroup ||
                !original.Ignores.SequenceEqual(newSync.Ignores);

            if (differs)
                changed.Add(newSync);
        }
        return changed;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
            if (item.Tag as string == tag)
            { combo.SelectedItem = item; return; }
        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    private void UpdateAddSyncBtnState() =>
        AddSyncBtn.IsEnabled = _servers.Count > 0;

    // ── Real-time validation ──────────────────────────────────────────────────

    private static readonly System.Windows.Media.Brush _errorBrush  = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69));
    private static readonly System.Windows.Media.Brush _normalBrush = System.Windows.SystemColors.ControlDarkBrush;
    private static readonly Regex _octalRegex  = new(@"^0[0-7]{3}$", RegexOptions.Compiled);

    private void FileModeBox_TextChanged(object sender, TextChangedEventArgs e)
        => ValidateOctal(FileModeBox);

    private void DirModeBox_TextChanged(object sender, TextChangedEventArgs e)
        => ValidateOctal(DirModeBox);

    private void IntervalBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        bool valid = int.TryParse(IntervalBox.Text.Trim(), out int v) && v >= 5;
        IntervalBox.BorderBrush = valid ? _normalBrush : _errorBrush;
    }

    private static void ValidateOctal(System.Windows.Controls.TextBox box)
    {
        bool valid = _octalRegex.IsMatch(box.Text.Trim());
        box.BorderBrush = valid ? _normalBrush : _errorBrush;
    }
}
