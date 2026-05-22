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

    /// <summary>Returns the exe path currently registered for autostart (unquoted), or null.</summary>
    public string? GetRegisteredPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return (key?.GetValue(AppName) as string)?.Trim('"');
    }

    /// <summary>
    /// If autostart is enabled but points to a different exe (e.g. after a reinstall to a new
    /// folder), rewrite it to the current exe so Windows launches the right binary. No-op otherwise.
    /// </summary>
    public void ReconcilePath(string currentExePath)
    {
        var registered = GetRegisteredPath();
        if (registered != null &&
            !string.Equals(registered, currentExePath, System.StringComparison.OrdinalIgnoreCase))
        {
            Enable(currentExePath);
        }
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
