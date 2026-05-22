using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MutagenManager.Models;

namespace MutagenManager.Services;

/// <summary>
/// Executes mutagen CLI commands asynchronously. Never blocks the UI thread.
/// </summary>
public class MutagenService
{
    private readonly LogService _log;

    /// <summary>
    /// Full path to the mutagen executable. Prefers the bundled mutagen.exe sitting
    /// next to the app (offline, version-pinned, no PATH dependency); falls back to
    /// "mutagen" on PATH for portable/dev setups.
    /// </summary>
    public string MutagenPath { get; }

    /// <summary>Directory where a bundled mutagen.exe lives (or should be placed by the updater).</summary>
    public static string AppDirectory =>
        Path.GetDirectoryName(Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule!.FileName)!;

    public MutagenService(LogService log)
    {
        _log = log;
        var bundled = Path.Combine(AppDirectory, "mutagen.exe");
        MutagenPath = File.Exists(bundled) ? bundled : "mutagen";
        _log.Log($"mutagen executable: {MutagenPath}");
    }

    // ── Generic process runner ────────────────────────────────────────────────

    public async Task<(string Output, int ExitCode)> RunAsync(
        string executable, string arguments,
        int timeoutMs = 30_000, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(executable, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask  = process.StandardError.ReadToEndAsync(ct);

        var completedInTime = await Task.Run(
            () => process.WaitForExit(timeoutMs), ct);

        var output = await outputTask;
        var error  = await errorTask;

        if (!completedInTime)
        {
            try { process.Kill(); } catch { }
            _log.Log($"TIMEOUT ({timeoutMs}ms): mutagen {arguments}");
            return (output, -1);
        }

        var combined = string.IsNullOrWhiteSpace(error) ? output : output + "\n" + error;
        return (combined, process.ExitCode);
    }

    public Task<(string Output, int ExitCode)> MutagenAsync(
        string arguments, int timeoutMs = 30_000, CancellationToken ct = default)
        => RunAsync(MutagenPath, arguments, timeoutMs, ct);

    // ── Sync operations ───────────────────────────────────────────────────────

    public Task<(string, int)> SyncListAsync(string name = "", bool longOutput = false, CancellationToken ct = default)
    {
        var args = string.IsNullOrEmpty(name)
            ? $"sync list{(longOutput ? " --long" : "")}"
            : $"sync list {name}{(longOutput ? " --long" : "")}";
        return MutagenAsync(args, ct: ct);
    }

    /// <summary>
    /// Returns the names of ALL sync sessions known to the mutagen daemon, regardless of
    /// whether they're in config.json. Used to surface orphan sessions (e.g. left over
    /// after a sync was renamed/removed only in config).
    /// </summary>
    public async Task<List<string>> GetAllSessionNamesAsync(CancellationToken ct = default)
    {
        var (output, _) = await SyncListAsync(ct: ct);
        var names = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(output, @"^Name:\s*(.+)$",
                     System.Text.RegularExpressions.RegexOptions.Multiline))
        {
            var name = m.Groups[1].Value.Trim();
            if (!string.IsNullOrEmpty(name)) names.Add(name);
        }
        return names;
    }

    public Task<(string, int)> SyncPauseAsync(string name, CancellationToken ct = default)
    {
        _log.Log($"Pause sync: {name}");
        return MutagenAsync($"sync pause {name}", ct: ct);
    }

    public Task<(string, int)> SyncResumeAsync(string name, CancellationToken ct = default)
    {
        _log.Log($"Resume sync: {name}");
        return MutagenAsync($"sync resume {name}", ct: ct);
    }

    public Task<(string, int)> SyncResetAsync(string name, CancellationToken ct = default)
    {
        _log.Log($"Reset sync: {name}");
        return MutagenAsync($"sync reset {name}", ct: ct);
    }

    public Task<(string, int)> SyncFlushAsync(string name, CancellationToken ct = default)
    {
        _log.Log($"Flush sync: {name}");
        return MutagenAsync($"sync flush {name}", 60_000, ct);
    }

    public Task<(string, int)> SyncTerminateAsync(string name, CancellationToken ct = default)
    {
        _log.Log($"Terminate sync: {name}");
        return MutagenAsync($"sync terminate {name}", ct: ct);
    }

    public async Task<bool> SyncExistsAsync(string name, CancellationToken ct = default)
    {
        var (output, code) = await MutagenAsync($"sync list {name}", ct: ct);
        return code == 0 && !string.IsNullOrWhiteSpace(output);
    }

    public async Task<bool> SyncCreateAsync(SyncConfig sync, AppConfig config, CancellationToken ct = default)
    {
        var server = config.Servers.TryGetValue(sync.Server, out var s) ? s : null;
        if (server == null)
        {
            _log.Log($"Error: servidor '{sync.Server}' no encontrado para '{sync.Name}'");
            return false;
        }

        var remoteUrl = $"{server.User}@{server.Host}:{server.Port}:{sync.RemotePath}";
        var sb = new StringBuilder();
        sb.Append($"sync create \"{sync.LocalPath}\" \"{remoteUrl}\"");
        sb.Append($" --name {sync.Name}");
        sb.Append($" --sync-mode {config.Defaults.Mode}");
        sb.Append($" --default-file-mode {config.Defaults.FileMode}");
        sb.Append($" --default-directory-mode {config.Defaults.DirectoryMode}");

        var owner = sync.ResolveOwner(server);
        var group = sync.ResolveGroup(server);
        if (!string.IsNullOrEmpty(owner)) sb.Append($" --default-owner-beta \"{owner}\"");
        if (!string.IsNullOrEmpty(group)) sb.Append($" --default-group-beta \"{group}\"");
        if (config.Defaults.CheckInterval > 0)
            sb.Append($" --watch-polling-interval {config.Defaults.CheckInterval}");

        sb.Append(" --ignore-vcs");
        foreach (var ignore in sync.Ignores)
            sb.Append($" --ignore \"{ignore}\"");

        _log.Log($"Create sync: {sb}");
        var (_, code) = await MutagenAsync(sb.ToString(), 60_000, ct);
        return code == 0;
    }

    /// <summary>Returns mutagen version string, or null if not found in PATH.</summary>
    public async Task<string?> CheckVersionAsync(CancellationToken ct = default)
    {
        try
        {
            var (output, code) = await MutagenAsync("version", 5_000, ct);
            return code == 0 ? output.Trim() : null;
        }
        catch { return null; }
    }

    // ── Remote file operations (for conflict resolution) ──────────────────────

    public async Task<bool> DownloadRemoteFileAsync(
        string host, int port, string user, string remotePath, string localDest,
        CancellationToken ct = default)
    {
        var args = $"-P {port} -o ConnectTimeout=5 -o BatchMode=yes \"{user}@{host}:{remotePath}\" \"{localDest}\"";
        var (_, code) = await RunAsync("scp", args, 15_000, ct);
        return code == 0;
    }

    public async Task<DateTime?> GetRemoteFileDateAsync(
        string host, int port, string user, string remotePath,
        CancellationToken ct = default)
    {
        var cmd = $"stat -c '%Y' '{remotePath}' 2>/dev/null || echo 0";
        var args = $"-p {port} -o ConnectTimeout=3 -o BatchMode=yes {user}@{host} \"{cmd}\"";
        var (output, code) = await RunAsync("ssh", args, 10_000, ct);

        if (code == 0 && long.TryParse(output.Trim(), out var ts) && ts > 0)
            return DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime;

        return null;
    }

    public async Task<bool> DeleteRemoteFileAsync(
        string host, int port, string user, string remotePath,
        CancellationToken ct = default)
    {
        var cmd = $"rm -f '{remotePath}'";
        var args = $"-p {port} -o ConnectTimeout=5 -o BatchMode=yes {user}@{host} \"{cmd}\"";
        var (_, code) = await RunAsync("ssh", args, 10_000, ct);
        return code == 0;
    }
}
