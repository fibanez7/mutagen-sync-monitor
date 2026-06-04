using System.Threading;
using System.Windows;

namespace MutagenManager;

public partial class App : Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;
    private TrayApplication? _trayApp;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance guard
        _mutex = new Mutex(true, "MutagenManager_SingleInstance_v3", out bool createdNew);

        // When relaunched by "Reiniciar Monitor" the OLD instance is still shutting down
        // and still holds the mutex. Wait briefly for it to release instead of bailing,
        // otherwise the restart leaves nothing running.
        if (!createdNew && e.Args.Contains("--restart"))
        {
            try { createdNew = _mutex.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { createdNew = true; }
        }

        if (!createdNew)
        {
            MessageBox.Show("MutagenManager ya está en ejecución.", "MutagenManager",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        _ownsMutex = true;

        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        _trayApp = new TrayApplication();
        _trayApp.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayApp?.Dispose();
        if (_ownsMutex)
        {
            try { _mutex?.ReleaseMutex(); } catch { }
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
