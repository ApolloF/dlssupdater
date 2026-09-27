using System.Text.Json.Serialization;

namespace DLSSUpdater.Scan;

/// <summary>NVIDIA features a game ships the runtime for (from its nvngx_* and Streamline sl.* files).</summary>
[Flags]
public enum GameFeatures
{
    None = 0,
    /// <summary>DLSS Super Resolution (nvngx_dlss.dll / sl.dlss.dll).</summary>
    SR = 1,
    /// <summary>DLSS Ray Reconstruction (nvngx_dlssd.dll / sl.dlss_d.dll).</summary>
    RR = 2,
    /// <summary>DLSS Frame Generation (nvngx_dlssg.dll / sl.dlss_g.dll).</summary>
    FG = 4,
    /// <summary>NVIDIA Reflex through Streamline (sl.reflex.dll).</summary>
    Reflex = 8,
    Streamline = 16,
}

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
        "sl.interposer.dll" => "SL",
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
    /// <summary>Streamline runtime files (sl.*.dll) the game ships.</summary>
    public List<DlssDll> Streamline { get; set; } = [];
    public string? AntiCheat { get; set; }
    /// <summary>Directories under the root holding a .dlssupdater manifest.</summary>
    public List<string> Installs { get; set; } = [];
    public DateTime Scanned { get; set; }

    public static string MakeId(string root) => Core.FileUtil.Normalize(root).ToLowerInvariant();

    [JsonIgnore]
    public GameFeatures Features => FeaturesOf(Dlss, Streamline);

    /// <summary>Newest DLSS Frame Generation dll version, if the game has one.</summary>
    [JsonIgnore]
    public Version? FgVersion => Dlss.Where(d => d.Kind == "FG").Select(d => d.Version).Max();

    private static readonly (string File, GameFeatures Feature)[] FeatureFiles =
    [
        ("nvngx_dlss.dll", GameFeatures.SR), ("nvngx_dlssd.dll", GameFeatures.RR), ("nvngx_dlssg.dll", GameFeatures.FG),
        ("sl.dlss.dll", GameFeatures.SR), ("sl.dlss_d.dll", GameFeatures.RR), ("sl.dlss_g.dll", GameFeatures.FG),
        ("sl.reflex.dll", GameFeatures.Reflex), ("sl.interposer.dll", GameFeatures.Streamline),
    ];

    public static GameFeatures FeaturesOf(IEnumerable<DlssDll> dlss, IEnumerable<DlssDll> streamline)
    {
        var f = GameFeatures.None;
        foreach (var d in dlss.Concat(streamline))
            foreach (var (file, feature) in FeatureFiles)
                if (d.Name.Equals(file, StringComparison.OrdinalIgnoreCase)) f |= feature;
        return f;
    }
}

/// <summary>A game folder reported by a launcher or the user, before inspection.</summary>
public sealed record GameEntry(string Name, string Root, string Source);
