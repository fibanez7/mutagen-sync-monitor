using System.Threading;
using System.Windows;

namespace MutagenManager;

public partial class App : Application
{
    private Mutex? _mutex;
    private TrayApplication? _trayApp;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance guard
        _mutex = new Mutex(true, "MutagenManager_SingleInstance_v3", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("MutagenManager ya está en ejecución.", "MutagenManager",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        _trayApp = new TrayApplication();
        _trayApp.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayApp?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
