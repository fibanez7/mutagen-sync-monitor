using System.Collections.Generic;
using System.Windows;
using MutagenManager.Models;

namespace MutagenManager.Views;

public partial class SyncEditDialog : Window
{
    public SyncConfig? Result { get; private set; }

    public SyncEditDialog(SyncConfig? existing, List<string> serverKeys)
    {
        InitializeComponent();

        // Populate server combo
        foreach (var key in serverKeys)
            ServerCombo.Items.Add(key);

        if (existing != null)
        {
            Title = $"Editar — {existing.Name}";
            NameBox.Text       = existing.Name;
            LocalPathBox.Text  = existing.LocalPath;
            RemotePathBox.Text = existing.RemotePath;
            IgnoresBox.Text    = string.Join("\n", existing.Ignores);
            OwnerBox.Text      = existing.DefaultOwner ?? "";
            GroupBox.Text      = existing.DefaultGroup ?? "";

            if (!string.IsNullOrEmpty(existing.Server))
                ServerCombo.SelectedItem = existing.Server;
        }
        else
        {
            Title = "Nueva sincronización";
            if (serverKeys.Count > 0)
                ServerCombo.SelectedIndex = 0;
        }
    }

    private void BrowseLocal_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Selecciona la carpeta local a sincronizar",
            SelectedPath = LocalPathBox.Text,
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            LocalPathBox.Text = dialog.SelectedPath;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show("El nombre no puede estar vacío.", "Validación",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(LocalPathBox.Text) || string.IsNullOrWhiteSpace(RemotePathBox.Text))
        {
            MessageBox.Show("Las rutas local y remota son obligatorias.", "Validación",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var ignores = new List<string>();
        foreach (var line in IgnoresBox.Text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                ignores.Add(trimmed);
        }

        Result = new SyncConfig
        {
            Name         = NameBox.Text.Trim(),
            Server       = ServerCombo.SelectedItem as string ?? "",
            LocalPath    = LocalPathBox.Text.Trim(),
            RemotePath   = RemotePathBox.Text.Trim(),
            Ignores      = ignores,
            DefaultOwner = string.IsNullOrWhiteSpace(OwnerBox.Text) ? null : OwnerBox.Text.Trim(),
            DefaultGroup = string.IsNullOrWhiteSpace(GroupBox.Text) ? null : GroupBox.Text.Trim(),
        };

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
