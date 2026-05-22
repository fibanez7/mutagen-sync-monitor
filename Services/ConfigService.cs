using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MutagenManager.Models;

namespace MutagenManager.Services;

public class ConfigService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ConfigPath { get; }

    public ConfigService(string exeDirectory)
    {
        ConfigPath = Path.Combine(exeDirectory, "config.json");
    }

    public AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new AppConfig();

        var json = File.ReadAllText(ConfigPath);
        return JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
    }

    public void Save(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, Options);
        File.WriteAllText(ConfigPath, json);
    }

    public bool Exists() => File.Exists(ConfigPath);

    /// <summary>
    /// Creates a valid empty config.json next to the exe if none exists, so the user
    /// always has a writable file to edit from Settings. Returns true if it created one.
    /// </summary>
    public bool EnsureExists()
    {
        if (File.Exists(ConfigPath)) return false;
        Save(new AppConfig());
        return true;
    }
}
