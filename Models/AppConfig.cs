using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MutagenManager.Models;

public class AppConfig
{
    [JsonPropertyName("servers")]
    public Dictionary<string, ServerConfig> Servers { get; set; } = [];

    [JsonPropertyName("syncs")]
    public List<SyncConfig> Syncs { get; set; } = [];

    [JsonPropertyName("defaults")]
    public DefaultsConfig Defaults { get; set; } = new();

    [JsonPropertyName("notifications")]
    public NotificationsConfig Notifications { get; set; } = new();
}

public class ServerConfig
{
    [JsonPropertyName("host")]
    public string Host { get; set; } = "";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 22;

    [JsonPropertyName("user")]
    public string User { get; set; } = "";

    [JsonPropertyName("defaultOwner")]
    public string? DefaultOwner { get; set; }

    [JsonPropertyName("defaultGroup")]
    public string? DefaultGroup { get; set; }
}

public class SyncConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("server")]
    public string Server { get; set; } = "";

    [JsonPropertyName("localPath")]
    public string LocalPath { get; set; } = "";

    [JsonPropertyName("remotePath")]
    public string RemotePath { get; set; } = "";

    [JsonPropertyName("ignores")]
    public List<string> Ignores { get; set; } = [];

    [JsonPropertyName("defaultOwner")]
    public string? DefaultOwner { get; set; }

    [JsonPropertyName("defaultGroup")]
    public string? DefaultGroup { get; set; }

    // Devuelve el owner efectivo: del sync o del servidor
    public string? ResolveOwner(ServerConfig? server) =>
        !string.IsNullOrEmpty(DefaultOwner) ? DefaultOwner : server?.DefaultOwner;

    public string? ResolveGroup(ServerConfig? server) =>
        !string.IsNullOrEmpty(DefaultGroup) ? DefaultGroup : server?.DefaultGroup;
}

public class DefaultsConfig
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "two-way-safe";

    [JsonPropertyName("fileMode")]
    public string FileMode { get; set; } = "0664";

    [JsonPropertyName("directoryMode")]
    public string DirectoryMode { get; set; } = "0775";

    [JsonPropertyName("checkInterval")]
    public int CheckInterval { get; set; } = 30;
}

public class NotificationsConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("sound")]
    public bool Sound { get; set; } = true;

    [JsonPropertyName("showOnConflict")]
    public bool ShowOnConflict { get; set; } = true;

    [JsonPropertyName("showOnDisconnect")]
    public bool ShowOnDisconnect { get; set; } = true;

    [JsonPropertyName("showOnResume")]
    public bool ShowOnResume { get; set; } = true;
}
