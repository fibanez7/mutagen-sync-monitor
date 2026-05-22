using System.Windows;
using MutagenManager.Models;

namespace MutagenManager.Views;

public partial class ServerEditDialog : Window
{
    public string?       ResultKey    { get; private set; }
    public ServerConfig? ResultConfig { get; private set; }

    public ServerEditDialog(string? existingKey, ServerConfig? existing)
    {
        InitializeComponent();

        if (existing != null && existingKey != null)
        {
            Title        = $"Editar servidor — {existingKey}";
            KeyBox.Text  = existingKey;
            HostBox.Text = existing.Host;
            PortBox.Text = existing.Port.ToString();
            UserBox.Text = existing.User;
            OwnerBox.Text = existing.DefaultOwner ?? "";
            GroupBox.Text = existing.DefaultGroup ?? "";
        }
        else
        {
            Title = "Nuevo servidor";
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(KeyBox.Text) ||
            string.IsNullOrWhiteSpace(HostBox.Text) ||
            string.IsNullOrWhiteSpace(UserBox.Text))
        {
            MessageBox.Show("Clave, host y usuario son obligatorios.", "Validación",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(PortBox.Text.Trim(), out int port) || port < 1 || port > 65535)
        {
            MessageBox.Show("El puerto debe ser un número entre 1 y 65535.", "Validación",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultKey = KeyBox.Text.Trim();
        ResultConfig = new ServerConfig
        {
            Host         = HostBox.Text.Trim(),
            Port         = port,
            User         = UserBox.Text.Trim(),
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
