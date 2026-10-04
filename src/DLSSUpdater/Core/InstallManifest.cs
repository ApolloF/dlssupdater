using System.Text.Json;
using System.Text.Json.Serialization;

namespace DLSSUpdater.Core;

public sealed class UnsafeManifestException(string targetDir, string detail)
    : Exception($"DLSS Updater's install record in {targetDir} points outside the game and install folders ({detail}). Nothing was changed.");

public sealed class BackupEntry
{
    /// <summary>Original location, an entry as in <see cref="InstallManifest.Files"/>.</summary>
    public string Original { get; set; } = "";
    /// <summary>
    /// Backup location, relative to the backup folder. Records from 0ed78f7 may hold "&lt;original&gt;.N" as a full path
    /// instead (an install folder on another drive than the game).
    /// </summary>
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
    /// <summary>Game root relative to the target dir (e.g. "..\..\.."), or a full path when the two are on different drives.</summary>
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

    /// <summary>Files we placed: relative to the game root, or <see cref="InstallTag"/>\… relative to the install folder (see <see cref="Entry"/>).</summary>
    public List<string> Files { get; set; } = [];
    /// <summary>Folders we own, entries as in <see cref="Files"/>.</summary>
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

    /// <summary>Starts an entry that is relative to the install folder. '|' can't occur in a Windows path, so no game file collides.</summary>
    public const string InstallTag = "|install|";

    /// <summary>
    /// How <paramref name="full"/> is recorded: relative to the game root when it lies in the game, otherwise relative to
    /// the install folder behind <see cref="InstallTag"/>. Unlike a path relative to the game root, that also works for an
    /// install folder on another drive or network share.
    /// </summary>
    public static string Entry(string full, string targetDir, string gameRoot)
    {
        if (FileUtil.IsUnder(full, gameRoot)) return Path.GetRelativePath(gameRoot, full);
        if (FileUtil.IsUnder(full, targetDir)) return InstallTag + Path.DirectorySeparatorChar + Path.GetRelativePath(targetDir, full);
        throw new UnsafeManifestException(targetDir, $"{full} is outside {gameRoot} and {targetDir}");
    }

    /// <summary>
    /// Where an entry points; <paramref name="gameRoot"/> is the root it was recorded against. Says nothing about whether
    /// that is a safe place: see <see cref="Contain"/>.
    /// </summary>
    /// <exception cref="UnsafeManifestException">The entry is not a path DLSS Updater writes.</exception>
    public static string Resolve(string entry, string targetDir, string gameRoot)
    {
        if (entry.StartsWith(InstallTag, StringComparison.Ordinal))
        {
            var rel = entry[InstallTag.Length..];
            if (rel.Length > 1 && rel[0] is '\\' or '/' && IsRelative(rel[1..])) return Path.GetFullPath(Path.Combine(targetDir, rel[1..]));
        }
        // Full paths are what 0ed78f7 recorded for an install folder on another drive or share.
        else if (IsFullPath(entry)) return Path.GetFullPath(entry);
        else if (IsRelative(entry)) return Path.GetFullPath(Path.Combine(gameRoot, entry));
        throw new UnsafeManifestException(targetDir, $"path \"{entry}\"");
    }

    /// <summary>
    /// The manifest travels with the game folder, so every path in it is untrusted. Each one must stay inside
    /// <paramref name="gameRoot"/> or the install folder (which may be a folder the user picked outside the game, on
    /// any drive or share) and not lead through a junction or symbolic link; owned folders must lie inside the install
    /// folder and backups inside its backup folder. Entries are rewritten the way <see cref="Entry"/> records them for
    /// <paramref name="gameRoot"/>, so the same install folder reached from another game root still finds its
    /// originals, and records from earlier versions are brought up to date.
    /// </summary>
    /// <exception cref="UnsafeManifestException">Any entry breaks these rules; nothing is changed.</exception>
    public void Contain(string targetDir, string gameRoot)
    {
        var target = FileUtil.Normalize(targetDir);
        var root = FileUtil.Normalize(gameRoot);
        var backupDir = BackupDirFor(target);
        // Links are looked for below whichever of the two trusted folders holds the path.
        string Base(string full) => FileUtil.IsUnder(full, root) ? root : target;
        if (FileUtil.LinkBelow(Base(backupDir), backupDir) is { } link) throw new UnsafeManifestException(target, $"{link} is a link");
        if (!IsRelative(RootRel) && !IsFullPath(RootRel)) throw new UnsafeManifestException(target, $"game root \"{RootRel}\"");
        if (Proxy is not null && !AppSettings.IsKnownProxy(Proxy)) throw new UnsafeManifestException(target, $"proxy \"{Proxy}\"");
        var oldRoot = FileUtil.Normalize(Path.Combine(target, RootRel));

        static bool StrictlyUnder(string full, string dir) =>
            FileUtil.IsUnder(full, dir) && !FileUtil.Normalize(full).Equals(dir, StringComparison.OrdinalIgnoreCase);

        string Check(string entry, bool inTargetOnly)
        {
            var full = Resolve(entry, target, oldRoot);
            var inside = StrictlyUnder(full, target) || (!inTargetOnly && StrictlyUnder(full, root));
            if (!inside) throw new UnsafeManifestException(target, $"\"{entry}\" is outside {(inTargetOnly ? target : $"{root} and {target}")}");
            if (FileUtil.LinkBelow(Base(full), full) is { } l) throw new UnsafeManifestException(target, $"\"{entry}\" leads through the link {l}");
            return full;
        }

        var files = Files.Select(f => Check(f, inTargetOnly: false)).ToList();
        var dirs = Dirs.Select(d => Check(d, inTargetOnly: true)).ToList();
        if (dirs.Any(d => FileUtil.IsUnder(d, DirFor(target))))
            throw new UnsafeManifestException(target, "an owned folder is DLSS Updater's own folder");
        var originals = Backups.Select(b => Check(b.Original, inTargetOnly: false)).ToList();
        for (var i = 0; i < Backups.Count; i++)
        {
            var b = Backups[i];
            string full;
            if (IsFullPath(b.Backup))
            {
                // 0ed78f7 joined a full original path onto the backup folder, which left the original beside itself as "<name>.N".
                full = Path.GetFullPath(b.Backup);
                if (!IsSideBackup(full, originals[i])) throw new UnsafeManifestException(target, $"backup \"{b.Backup}\"");
            }
            else
            {
                if (!IsRelative(b.Backup) || b.Backup.Split('\\', '/').Contains("..")) throw new UnsafeManifestException(target, $"backup \"{b.Backup}\"");
                full = Path.GetFullPath(Path.Combine(backupDir, b.Backup));
                if (!StrictlyUnder(full, backupDir)) throw new UnsafeManifestException(target, $"backup \"{b.Backup}\" is outside {backupDir}");
            }
            if (FileUtil.LinkBelow(Base(full), full) is { } l) throw new UnsafeManifestException(target, $"backup \"{b.Backup}\" leads through the link {l}");
        }

        Files = files.Select(f => Entry(f, target, root)).ToList();
        Dirs = dirs.Select(d => Entry(d, target, root)).ToList();
        for (var i = 0; i < Backups.Count; i++) Backups[i].Original = Entry(originals[i], target, root);
        RootRel = Path.GetRelativePath(target, root);
    }

    /// <summary>"&lt;original&gt;.N" in the original's own folder.</summary>
    private static bool IsSideBackup(string full, string original) =>
        full.Length > original.Length + 1
        && full.StartsWith(original + ".", StringComparison.OrdinalIgnoreCase)
        && full[(original.Length + 1)..].All(char.IsAsciiDigit);

    /// <summary>Relative, with no drive or stream (':'). ".." parts are fine: where the path ends up is checked separately.</summary>
    private static bool IsRelative(string? rel) =>
        !string.IsNullOrWhiteSpace(rel) && !Path.IsPathRooted(rel) && !rel.Contains(':');

    /// <summary>A drive or UNC path with no stream (':') after its root. Device paths (\\?\, \\.\) are not accepted.</summary>
    private static bool IsFullPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)
        && !(path.Length > 2 && path[0] is '\\' or '/' && path[1] is '\\' or '/' && path[2] is '?' or '.')
        && !path[Path.GetPathRoot(path)!.Length..].Contains(':');

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
