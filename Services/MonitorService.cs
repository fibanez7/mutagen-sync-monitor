using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MutagenManager.Models;

namespace MutagenManager.Services;

/// <summary>
/// Smart polling monitor for mutagen sync sessions.
///
/// Strategy:
///   - All syncs OK      → poll every CheckInterval seconds (config, default 30s)
///   - Any conflict/error → poll every 5s until resolved
///   - After any user operation → poll immediately (1s delay)
/// </summary>
public class MonitorService : IDisposable
{
    private readonly MutagenService _mutagen;
    private readonly LogService _log;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    private AppConfig _config = new();

    private readonly Dictionary<string, SyncStatus> _statuses = [];
    private readonly object _statusLock = new();

    // Raised on thread-pool; callers must dispatch to UI thread if needed
    public event Action<SyncStatus>? StatusChanged;
    public event Action<string, string, string>? NotificationRequested;

    public IReadOnlyDictionary<string, SyncStatus> CurrentStatuses
    {
        get { lock (_statusLock) { return new Dictionary<string, SyncStatus>(_statuses); } }
    }

    private volatile bool _forceCheck;
    public void RequestImmediateCheck() => _forceCheck = true;

    /// <summary>True while the polling loop task is alive (not completed/faulted).</summary>
    public bool IsRunning => _loopTask is { IsCompleted: false };

    public MonitorService(MutagenService mutagen, LogService log)
    {
        _mutagen = mutagen;
        _log = log;
    }

    /// <summary>
    /// Starts the polling loop. Idempotent: if a loop is already running it just
    /// refreshes the config and returns — never spawns a second concurrent loop.
    /// Disposing the previous CTS only happens when the prior loop has finished,
    /// avoiding the disposed-token race that crashed the app after resume.
    /// </summary>
    public void Start(AppConfig config)
    {
        _config = config;

        if (IsRunning)
        {
            _log.Log("MonitorService.Start ignored — loop already running");
            return;
        }

        _cts?.Dispose();           // safe: prior loop has completed
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => MonitorLoopAsync(_cts.Token));
        _log.Log("MonitorService started");
    }

    /// <summary>Hot-reload config (e.g. when notifications or interval change).</summary>
    public void UpdateConfig(AppConfig config) => _config = config;

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_loopTask != null)
        {
            try { await _loopTask.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch { }
        }
        _log.Log("MonitorService stopped");
    }

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        try { await CheckAllAsync(ct); } catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            bool hasProblems;
            lock (_statusLock)
            {
                hasProblems = false;
                foreach (var s in _statuses.Values)
                    if (s.Code is SyncStatusCode.Conflict or SyncStatusCode.Error)
                    { hasProblems = true; break; }
            }

            // Use configured interval when healthy; 5s when problems active
            int normalIntervalMs = Math.Max(5, _config.Defaults.CheckInterval) * 1000;
            int intervalMs = hasProblems ? 5_000 : normalIntervalMs;

            var deadline = DateTime.UtcNow.AddMilliseconds(intervalMs);
            try
            {
                while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
                {
                    if (_forceCheck) break;
                    await Task.Delay(500, ct).ConfigureAwait(false);
                }
                _forceCheck = false;

                if (!ct.IsCancellationRequested)
                    await CheckAllAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // Never let the loop die on a transient error — log and keep polling
                _log.Log($"Monitor loop error (continuing): {ex.Message}");
                try { await Task.Delay(2000, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        _log.Log("MonitorService loop exited");
    }

    private int _daemonDownPolls;

    private async Task CheckAllAsync(CancellationToken ct)
    {
        if (_config.Syncs.Count == 0) return;

        // ONE daemon query for all sessions per poll, then parse per-name from the
        // combined output. Previously this spawned one mutagen.exe per sync per poll
        // (N processes / interval) — heavy with several connections. Now it's a single
        // process regardless of how many syncs are configured.
        string output;
        int exit;
        try
        {
            (output, exit) = await _mutagen.SyncListAsync(longOutput: true, ct: ct);
        }
        catch (Exception ex)
        {
            _log.Log($"Error listing syncs: {ex.Message}");
            output = "";
            exit = -1;
        }

        // Daemon watchdog: a non-zero exit from `sync list` means the per-user daemon
        // is down (mutagen normally auto-starts it). Require two consecutive bad polls
        // before acting, so a transient hiccup (e.g. right after resume) doesn't trigger
        // a needless restart. `daemon start` is idempotent.
        if (exit != 0)
        {
            if (++_daemonDownPolls >= 2)
            {
                _daemonDownPolls = 0;
                _log.Log("Watchdog: mutagen daemon appears down — restarting");
                NotificationRequested?.Invoke("Daemon mutagen", "Daemon caído — reiniciando…", "Warning");
                try
                {
                    await _mutagen.DaemonStartAsync(ct);
                    _forceCheck = true; // re-check promptly once it's back
                }
                catch (Exception ex) { _log.Log($"Watchdog daemon start failed: {ex.Message}"); }
            }
            return; // nothing useful to parse this poll
        }
        _daemonDownPolls = 0;

        var blocks = SplitSessionBlocks(output);

        foreach (var sync in _config.Syncs)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var status = blocks.TryGetValue(sync.Name, out var block)
                    ? ParseStatus(sync.Name, block)
                    : new SyncStatus { Name = sync.Name, Code = SyncStatusCode.Unknown, RawStatus = "Not found" };
                UpdateStatus(status);
            }
            catch (Exception ex)
            {
                _log.Log($"Error checking {sync.Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Splits the combined `mutagen sync list --long` output into per-session blocks
    /// keyed by session name. A new block starts at each line beginning with "Name:",
    /// so it's robust whether or not the daemon prints dashed separators between entries.
    /// </summary>
    internal static Dictionary<string, string> SplitSessionBlocks(string output)
    {
        var blocks = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(output)) return blocks;

        var nameRegex = new Regex(@"^Name:\s*(.+?)\s*$");
        string? currentName = null;
        var sb = new System.Text.StringBuilder();

        void Flush()
        {
            if (currentName != null) blocks[currentName] = sb.ToString();
            sb.Clear();
        }

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var m = nameRegex.Match(line);
            if (m.Success)
            {
                Flush();
                currentName = m.Groups[1].Value.Trim();
            }
            if (currentName != null) sb.Append(line).Append('\n');
        }
        Flush();
        return blocks;
    }

    private void UpdateStatus(SyncStatus newStatus)
    {
        SyncStatus? prev;
        lock (_statusLock)
        {
            _statuses.TryGetValue(newStatus.Name, out prev);
            _statuses[newStatus.Name] = newStatus;
        }

        StatusChanged?.Invoke(newStatus);

        if (prev == null) return;

        var notif = _config.Notifications;
        if (!notif.Enabled) return;

        if (prev.Code != SyncStatusCode.Conflict && newStatus.Code == SyncStatusCode.Conflict && notif.ShowOnConflict)
            NotificationRequested?.Invoke("Conflicto detectado", $"'{newStatus.Name}' tiene conflictos", "Warning");

        if (prev.Code != SyncStatusCode.Error && newStatus.Code == SyncStatusCode.Error && notif.ShowOnDisconnect)
            NotificationRequested?.Invoke("Error de sincronización", $"'{newStatus.Name}' tiene errores", "Error");

        if (prev.Code != SyncStatusCode.Ok && newStatus.Code == SyncStatusCode.Ok && notif.ShowOnResume)
            NotificationRequested?.Invoke("Sincronizado", $"'{newStatus.Name}' está sincronizado", "Info");
    }

    // ── Mutagen output parser ─────────────────────────────────────────────────

    public async Task<SyncStatus> GetStatusAsync(string name, CancellationToken ct = default)
    {
        var (output, _) = await _mutagen.SyncListAsync(name, longOutput: true, ct: ct);
        return ParseStatus(name, output);
    }

    internal static SyncStatus ParseStatus(string name, string output)
    {
        var status = new SyncStatus { Name = name };

        if (string.IsNullOrWhiteSpace(output))
        {
            status.Code = SyncStatusCode.Error;
            status.RawStatus = "No output";
            return status;
        }

        var statusMatch = Regex.Match(output, @"Status:\s*(.+)", RegexOptions.Multiline);
        status.RawStatus = statusMatch.Success ? statusMatch.Groups[1].Value.Trim() : "Unknown";

        var lines = output.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        string? currentSection = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            if (Regex.IsMatch(line, @"Scan problems:\s*$"))       { currentSection = "scan"; continue; }
            if (Regex.IsMatch(line, @"Transition problems:\s*$")) { currentSection = "transition"; continue; }
            if (Regex.IsMatch(line, @"^\t?Conflicts:\s*$"))       { currentSection = "conflicts"; continue; }
            if (Regex.IsMatch(line, @"^Status:") || line == "---"){ currentSection = null; }

            if (currentSection != null && Regex.IsMatch(line, @"^\t[A-Z][a-z]+") && !line.StartsWith("\t\t"))
                currentSection = null;

            if (currentSection != null && Regex.IsMatch(line, @"^\t+(.+)$"))
            {
                var detail = line.TrimStart('\t').Trim();
                if (!string.IsNullOrEmpty(detail))
                {
                    switch (currentSection)
                    {
                        case "scan":       status.ScanDetails.Add(detail); break;
                        case "transition": status.TransitionDetails.Add(detail); break;
                        case "conflicts":  status.ConflictDetails.Add(detail); break;
                    }
                }
            }
        }

        status.ScanProblems       = status.ScanDetails.Count;
        status.TransitionProblems = status.TransitionDetails.Count;
        status.Conflicts          = status.ConflictDetails.Count / 2;
        status.ConflictPairs      = ParseConflictPairs(status.ConflictDetails);

        if (status.HasProblems)
            status.Code = SyncStatusCode.Conflict;
        else if (Regex.IsMatch(status.RawStatus, @"Watching", RegexOptions.IgnoreCase))
            status.Code = SyncStatusCode.Ok;
        else if (Regex.IsMatch(status.RawStatus, @"Paused.*conflict|conflict.*Paused", RegexOptions.IgnoreCase))
            status.Code = SyncStatusCode.Conflict;
        else if (Regex.IsMatch(status.RawStatus, @"Paused", RegexOptions.IgnoreCase))
            status.Code = SyncStatusCode.Paused;
        else if (Regex.IsMatch(status.RawStatus, @"error", RegexOptions.IgnoreCase))
            status.Code = SyncStatusCode.Error;
        else
            status.Code = SyncStatusCode.Unknown;

        return status;
    }

    private static List<ConflictPair> ParseConflictPairs(List<string> conflictLines)
    {
        var pairs = new Dictionary<string, ConflictPair>();
        var pathRegex = new Regex(@"^\((\w+)\)\s+(.+?)\s+\(");

        foreach (var line in conflictLines)
        {
            var m = pathRegex.Match(line);
            if (!m.Success) continue;

            var side = m.Groups[1].Value;
            var path = m.Groups[2].Value.Trim();

            if (!pairs.TryGetValue(path, out var pair))
            {
                pair = new ConflictPair { FilePath = path };
                pairs[path] = pair;
            }

            if (side == "alpha") pair.RawAlpha = line;
            else                  pair.RawBeta  = line;
        }

        return [.. pairs.Values];
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
