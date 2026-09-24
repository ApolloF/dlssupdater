namespace DLSSUpdater.Scan;

public sealed class DlssDll
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public Version? Version { get; set; }

    public string Kind => Name.ToLowerInvariant() switch
    {
        "nvngx_dlss.dll" => "SR",
        "nvngx_dlssd.dll" => "RR",
        "nvngx_dlssg.dll" => "FG",
        _ => "?",
    };
}

public sealed class GameInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public string Root { get; set; } = "";

    /// <summary>Executables, best candidate first.</summary>
    public List<string> Exes { get; set; } = [];
    public List<DlssDll> Dlss { get; set; } = [];
    public string? AntiCheat { get; set; }
    /// <summary>Directories under the root holding a .dlssupdater manifest.</summary>
    public List<string> Installs { get; set; } = [];
    public DateTime Scanned { get; set; }

    public static string MakeId(string root) => Core.FileUtil.Normalize(root).ToLowerInvariant();
}

/// <summary>A game folder reported by a launcher or the user, before inspection.</summary>
public sealed record GameEntry(string Name, string Root, string Source);
