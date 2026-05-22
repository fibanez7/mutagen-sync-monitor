using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace MutagenManager.Views;

public partial class LogViewerWindow : Window
{
    private readonly string          _logPath;
    private readonly DispatcherTimer _timer;

    public LogViewerWindow(string logPath)
    {
        _logPath = logPath;
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => LoadLog();
        _timer.Start();

        Loaded += (_, _) => LoadLog();
        Closed += (_, _) => _timer.Stop();
    }

    private void LoadLog()
    {
        if (!File.Exists(_logPath))
        {
            LogContent.Text = "(archivo de log no encontrado)";
            return;
        }

        try
        {
            // Open with ReadWrite share so LogService can keep appending
            string content;
            using (var fs     = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
                content = reader.ReadToEnd();

            LogContent.Text = content;

            var lineCount = content.Split('\n').Length;
            LineCountLabel.Text = $"{lineCount} línea(s)";

            if (AutoScrollCheck.IsChecked == true)
                LogContent.ScrollToEnd();
        }
        catch (Exception ex)
        {
            LogContent.Text = $"Error leyendo log: {ex.Message}";
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadLog();

    private void OpenNotepad_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(_logPath))
            System.Diagnostics.Process.Start("notepad", _logPath);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
