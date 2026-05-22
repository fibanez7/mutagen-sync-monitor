using Microsoft.Win32;

namespace MutagenManager.Services;

/// <summary>
/// Manages Windows autostart via HKCU registry Run key.
/// No admin rights required. More reliable than PS2EXE + Startup folder.
/// </summary>
public class AutoStartService
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "MutagenManager";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(AppName) != null;
    }

    public bool Enable(string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.SetValue(AppName, $"\"{exePath}\"");
            return true;
        }
        catch { return false; }
    }

    public bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
            return true;
        }
        catch { return false; }
    }
}
