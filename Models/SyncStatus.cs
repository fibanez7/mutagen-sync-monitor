using System.Collections.Generic;

namespace MutagenManager.Models;

public enum SyncStatusCode
{
    Unknown,
    Ok,        // Watching for changes
    Paused,
    Conflict,
    Error
}

public class SyncStatus
{
    public string Name { get; set; } = "";
    public SyncStatusCode Code { get; set; } = SyncStatusCode.Unknown;
    public string RawStatus { get; set; } = "";

    public int ScanProblems { get; set; }
    public int TransitionProblems { get; set; }
    public int Conflicts { get; set; }

    public List<string> ScanDetails { get; set; } = [];
    public List<string> TransitionDetails { get; set; } = [];
    public List<string> ConflictDetails { get; set; } = [];

    public bool HasProblems => ScanProblems > 0 || TransitionProblems > 0 || Conflicts > 0;

    public string DisplayStatus => Code switch
    {
        SyncStatusCode.Ok       => "Sincronizado",
        SyncStatusCode.Paused   => "Pausado",
        SyncStatusCode.Conflict => "Conflicto",
        SyncStatusCode.Error    => "Error",
        _                       => "..."
    };

    // Pair parsed from mutagen conflict output
    public List<ConflictPair> ConflictPairs { get; set; } = [];
}

public class ConflictPair
{
    public string FilePath { get; set; } = "";
    public string RawAlpha { get; set; } = "";
    public string RawBeta { get; set; } = "";
}
