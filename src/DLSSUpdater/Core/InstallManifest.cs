using System.Text.Json;
using System.Text.Json.Serialization;

namespace DLSSUpdater.Core;

public sealed class BackupEntry
{
    /// <summary>Original location, relative to the game root.</summary>
    public string Original { get; set; } = "";
    /// <summary>Backup location, relative to the backup folder.</summary>
    public string Backup { get; set; } = "";
    /// <summary>"dlss" for nvngx swaps, "file" for anything else we displaced.</summary>
    public string Kind { get; set; } = "file";
    /// <summary>Version we put in place, used to detect a game patch replacing our file with a new original.</summary>
    public string? InstalledSha { get; set; }
}

/// <summary>Lives in &lt;target&gt;\.dlssupdater\manifest.json and travels with the game folder.</summary>
public sealed class InstallManifest
{
    public const string DirName = ".dlssupdater";

    public int Schema { get; set; } = 1;
    /// <summary>Game root relative to the target dir (e.g. "..\..\..").</summary>
    public string RootRel { get; set; } = ".";
    public string? Proxy { get; set; }

    public bool Opti { get; set; }
    public bool ReShade { get; set; }
    public bool Mfg { get; set; }
    public bool DlssNr { get; set; }
    public bool Dlss { get; set; }

    public string? OptiTag { get; set; }
    public string? MfgTag { get; set; }
    public string? DlssTag { get; set; }
    public string? DlssNrSha { get; set; }
    public string? ReShadeSha { get; set; }
    public bool AntiCheatConfirmed { get; set; }

    /// <summary>Files we placed, relative to the game root.</summary>
    public List<string> Files { get; set; } = [];
    /// <summary>Folders we own, relative to the game root.</summary>
    public List<string> Dirs { get; set; } = [];
    public List<BackupEntry> Backups { get; set; } = [];
    public DateTime Updated { get; set; }

    public static string DirFor(string targetDir) => Path.Combine(targetDir, DirName);
    public static string PathFor(string targetDir) => Path.Combine(DirFor(targetDir), "manifest.json");
    public static string BackupDirFor(string targetDir) => Path.Combine(DirFor(targetDir), "backup");

    public static InstallManifest? Load(string targetDir)
    {
        var path = PathFor(targetDir);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize(File.ReadAllText(path), JsonCtx.Default.InstallManifest); }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Log.Error($"Unreadable manifest {path}", ex);
            return null;
        }
    }

    public void Save(string targetDir)
    {
        Updated = DateTime.UtcNow;
        FileUtil.AtomicWriteText(PathFor(targetDir), JsonSerializer.Serialize(this, JsonCtx.Default.InstallManifest));
    }

    [JsonIgnore]
    public bool IsEmpty => Files.Count == 0 && Dirs.Count == 0 && Backups.Count == 0;

    public bool Owns(string rel) => Files.Contains(rel, StringComparer.OrdinalIgnoreCase);

    public void AddFile(string rel)
    {
        if (!Owns(rel)) Files.Add(rel);
    }

    public void AddDir(string rel)
    {
        if (!Dirs.Contains(rel, StringComparer.OrdinalIgnoreCase)) Dirs.Add(rel);
    }

    public BackupEntry? BackupOf(string rel) =>
        Backups.FirstOrDefault(b => b.Original.Equals(rel, StringComparison.OrdinalIgnoreCase));
}
