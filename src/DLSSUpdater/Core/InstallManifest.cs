using System.Text.Json;
using System.Text.Json.Serialization;

namespace DLSSUpdater.Core;

public sealed class UnsafeManifestException(string targetDir, string detail)
    : Exception($"DLSS Updater's install record in {targetDir} points outside the game ({detail}). Nothing was changed.");

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
    /// <summary>Proxy the game loads: OptiScaler's, or ReShade's in ReShade-only mode.</summary>
    public string? Proxy { get; set; }
    public InstallMode Mode { get; set; }

    public bool Opti { get; set; }
    public bool ReShade { get; set; }
    public bool Mfg { get; set; }
    public bool DlssNr { get; set; }
    public bool Dlss { get; set; }

    public string? OptiTag { get; set; }
    public string? MfgTag { get; set; }
    public string? DlssTag { get; set; }
    public bool DlssPinned { get; set; }
    public string? DlssNrSha { get; set; }
    public string? ReShadeSha { get; set; }
    public string? StreamlineTag { get; set; }
    public bool Streamline { get; set; }
    /// <summary>Values we wrote into OptiScaler.ini / ReShade.ini last time, keyed "Section/Key".</summary>
    public Dictionary<string, string> OptiIni { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ReShadeIni { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>For display: the manifest, or null when there is none or it can't be read.</summary>
    public static InstallManifest? Load(string targetDir)
    {
        try { return Read(targetDir); }
        catch (InvalidDataException ex)
        {
            Log.Error(ex.Message, ex.InnerException);
            return null;
        }
    }

    /// <summary>
    /// For changes: the manifest, or null when there is none. One that exists but can't be read throws, so an
    /// install never starts a fresh record over the original-file backups.
    /// </summary>
    public static InstallManifest? Read(string targetDir)
    {
        var path = PathFor(targetDir);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), JsonCtx.Default.InstallManifest)
                   ?? throw new JsonException("empty manifest");
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            throw new InvalidDataException(
                $"DLSS Updater's install record {path} can't be read ({ex.Message}). Nothing was changed; retry, or restore that file.", ex);
        }
    }

    /// <summary>
    /// The manifest travels with the game folder, so every path in it is untrusted. Each one must stay inside
    /// <paramref name="gameRoot"/> and not lead through a junction or symbolic link; owned folders must lie inside
    /// the install folder and backups inside its backup folder. Entries are rebased onto <paramref name="gameRoot"/>,
    /// so the same install folder reached from another game root still finds its originals.
    /// </summary>
    /// <exception cref="UnsafeManifestException">Any entry breaks these rules; nothing is changed.</exception>
    public void Contain(string targetDir, string gameRoot)
    {
        var target = FileUtil.Normalize(targetDir);
        var root = FileUtil.Normalize(gameRoot);
        var backupDir = BackupDirFor(target);
        if (!FileUtil.IsUnder(target, root)) throw new UnsafeManifestException(target, $"the install folder is outside the game folder {root}");
        if (FileUtil.LinkBelow(root, backupDir) is { } link) throw new UnsafeManifestException(target, $"{link} is a link");
        if (!IsSafeRel(RootRel, allowUp: true)) throw new UnsafeManifestException(target, $"game root \"{RootRel}\"");
        if (Proxy is not null && !AppSettings.IsKnownProxy(Proxy)) throw new UnsafeManifestException(target, $"proxy \"{Proxy}\"");
        var oldRoot = FileUtil.Normalize(Path.Combine(target, RootRel));

        string Rebase(string rel, string inside)
        {
            if (!IsSafeRel(rel, allowUp: false)) throw new UnsafeManifestException(target, $"path \"{rel}\"");
            var full = Path.GetFullPath(Path.Combine(oldRoot, rel));
            if (!FileUtil.IsUnder(full, inside) || FileUtil.Normalize(full).Equals(inside, StringComparison.OrdinalIgnoreCase))
                throw new UnsafeManifestException(target, $"\"{rel}\" is outside {inside}");
            if (FileUtil.LinkBelow(root, full) is { } l) throw new UnsafeManifestException(target, $"\"{rel}\" leads through the link {l}");
            return Path.GetRelativePath(root, full);
        }

        var files = Files.Select(f => Rebase(f, root)).ToList();
        var dirs = Dirs.Select(d => Rebase(d, target)).ToList();
        if (dirs.Any(d => FileUtil.IsUnder(Path.Combine(root, d), DirFor(target))))
            throw new UnsafeManifestException(target, "an owned folder is DLSS Updater's own folder");
        var originals = Backups.Select(b => Rebase(b.Original, root)).ToList();
        foreach (var b in Backups)
        {
            if (!IsSafeRel(b.Backup, allowUp: false)) throw new UnsafeManifestException(target, $"backup \"{b.Backup}\"");
            var full = Path.GetFullPath(Path.Combine(backupDir, b.Backup));
            if (!FileUtil.IsUnder(full, backupDir) || FileUtil.Normalize(full).Equals(backupDir, StringComparison.OrdinalIgnoreCase))
                throw new UnsafeManifestException(target, $"backup \"{b.Backup}\" is outside {backupDir}");
            if (FileUtil.LinkBelow(target, full) is { } l) throw new UnsafeManifestException(target, $"backup \"{b.Backup}\" leads through the link {l}");
        }

        Files = files;
        Dirs = dirs;
        for (var i = 0; i < Backups.Count; i++) Backups[i].Original = originals[i];
        RootRel = Path.GetRelativePath(target, root);
    }

    /// <summary>Relative, no drive or stream (':'), and no ".." unless <paramref name="allowUp"/> (then only "." / ".." parts).</summary>
    private static bool IsSafeRel(string? rel, bool allowUp)
    {
        if (string.IsNullOrWhiteSpace(rel) || Path.IsPathRooted(rel) || rel.Contains(':')) return false;
        var parts = rel.Split('\\', '/');
        return allowUp ? parts.All(p => p is "." or "..") : !parts.Any(p => p == "..");
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
