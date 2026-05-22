using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MutagenManager.Services;

/// <summary>
/// Downloads and installs the official mutagen CLI from GitHub Releases on demand.
///
/// Design: the app ships with a pinned, known-good mutagen.exe (stable by default).
/// This updater only runs when the user explicitly asks, so unknown future CLI
/// changes never break a working install silently.
/// </summary>
public sealed class MutagenUpdater
{
    private const string LatestApi = "https://api.github.com/repos/mutagen-io/mutagen/releases/latest";

    private readonly LogService _log;
    private readonly MutagenService _mutagen;

    public MutagenUpdater(LogService log, MutagenService mutagen)
    {
        _log = log;
        _mutagen = mutagen;
    }

    /// <summary>True only if mutagen.exe is bundled next to the app (a PATH install we don't manage).</summary>
    public bool CanSelfUpdate =>
        Path.IsPathRooted(_mutagen.MutagenPath) && File.Exists(_mutagen.MutagenPath);

    private static HttpClient NewClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        // GitHub API requires a User-Agent
        c.DefaultRequestHeaders.UserAgent.ParseAdd("MutagenManager/3.1");
        return c;
    }

    /// <summary>Returns the latest release tag (e.g. "v0.18.1"), or null on failure.</summary>
    public async Task<string?> GetLatestVersionAsync()
    {
        try
        {
            using var client = NewClient();
            var release = await client.GetFromJsonAsync<GitHubRelease>(LatestApi);
            return release?.TagName;
        }
        catch (Exception ex)
        {
            _log.Log($"GetLatestVersion failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Downloads the latest windows/amd64 mutagen, stops the running daemon, and
    /// replaces the bundled mutagen.exe. Reports human-readable progress.
    /// </summary>
    public async Task<bool> UpdateAsync(IProgress<string>? progress = null)
    {
        if (!CanSelfUpdate)
        {
            progress?.Report("mutagen no está bundleado junto al programa; no se puede actualizar automáticamente.");
            return false;
        }

        try
        {
            progress?.Report("Consultando última versión…");
            using var client = NewClient();
            var release = await client.GetFromJsonAsync<GitHubRelease>(LatestApi);
            if (release?.Assets == null)
            {
                progress?.Report("No se pudo obtener la información de la release.");
                return false;
            }

            // Asset name like: mutagen_windows_amd64_v0.18.1.zip
            var asset = release.Assets.FirstOrDefault(a =>
                a.Name.Contains("windows", StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains("amd64", StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

            if (asset == null)
            {
                progress?.Report("No se encontró el binario de Windows en la release.");
                return false;
            }

            _log.Log($"Updating mutagen → {release.TagName} ({asset.Name})");
            progress?.Report($"Descargando {release.TagName}…");

            var tmpDir = Path.Combine(Path.GetTempPath(), "mutagen-update");
            Directory.CreateDirectory(tmpDir);
            var zipPath = Path.Combine(tmpDir, asset.Name);

            var bytes = await client.GetByteArrayAsync(asset.DownloadUrl);
            await File.WriteAllBytesAsync(zipPath, bytes);

            progress?.Report("Extrayendo…");
            var extractDir = Path.Combine(tmpDir, "extract");
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            var newExe = Directory.GetFiles(extractDir, "mutagen.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (newExe == null)
            {
                progress?.Report("El paquete no contenía mutagen.exe.");
                return false;
            }

            // The daemon holds mutagen.exe open — stop it before replacing the file
            progress?.Report("Deteniendo daemon de mutagen…");
            try { await _mutagen.MutagenAsync("daemon stop", 10_000); } catch { }
            await Task.Delay(1500);

            progress?.Report("Reemplazando ejecutable…");
            var target = _mutagen.MutagenPath;
            var backup = target + ".old";
            if (File.Exists(backup)) File.Delete(backup);
            if (File.Exists(target)) File.Move(target, backup);   // keep a rollback copy
            File.Copy(newExe, target, overwrite: true);
            if (File.Exists(backup)) { try { File.Delete(backup); } catch { } }

            // Restart daemon so syncs resume
            try { await _mutagen.MutagenAsync("daemon start", 10_000); } catch { }

            var installed = await _mutagen.CheckVersionAsync();
            progress?.Report($"Actualizado a {release.TagName}. mutagen: {installed}");
            _log.Log($"mutagen updated to {release.TagName}");
            return true;
        }
        catch (Exception ex)
        {
            _log.Log($"mutagen update failed: {ex.Message}");
            progress?.Report($"Error actualizando: {ex.Message}");
            return false;
        }
    }

    // ── GitHub API DTOs ───────────────────────────────────────────────────────
    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("assets")]   public GitHubAsset[]? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]                 public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string DownloadUrl { get; set; } = "";
    }
}
