using System;
using System.IO;
using System.Text;

namespace MutagenManager.Services;

public class LogService
{
    private readonly string _logPath;
    private readonly object _lock = new();
    private const long MaxLogBytes = 1 * 1024 * 1024; // 1 MB

    public LogService()
    {
        _logPath = Path.Combine(Path.GetTempPath(), "mutagen-manager.log");
        if (!File.Exists(_logPath))
            File.WriteAllText(_logPath, "", Encoding.UTF8);
    }

    public void Log(string message)
    {
        try
        {
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            lock (_lock)
            {
                RotateIfNeeded();
                File.AppendAllText(_logPath, entry, Encoding.UTF8);
            }
        }
        catch { /* no recursive errors */ }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(_logPath)) return;
        if (new FileInfo(_logPath).Length < MaxLogBytes) return;

        var oldPath = _logPath + ".old";
        if (File.Exists(oldPath)) File.Delete(oldPath);
        File.Move(_logPath, oldPath);
        File.WriteAllText(_logPath,
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Log rotated — previous entries saved as .old{Environment.NewLine}",
            Encoding.UTF8);
    }

    public string LogPath => _logPath;
}
