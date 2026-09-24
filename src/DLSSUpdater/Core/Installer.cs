using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

public sealed class InstallOptions
{
    public bool Opti { get; init; }
    public bool ReShade { get; init; }
    public bool Mfg { get; init; }
    public bool DlssNr { get; init; }
    public bool Dlss { get; init; }
    public bool AddMissingDlss { get; init; }
    public bool CarryOverIni { get; init; } = true;
    public string Proxy { get; init; } = "dxgi.dll";
    public IReadOnlyList<IniOverride> Overrides { get; init; } = [];
    public bool AntiCheatConfirmed { get; init; }
}

public sealed class NeedsAdminException(string path)
    : Exception($"No write access to {path}. Restart DLSS Updater as administrator.");

public sealed class GameRunningException(string exe)
    : Exception($"{Path.GetFileName(exe)} is running. Close the game first.");

public sealed class Installer(ComponentStore store)
{
    private static readonly string[] LegacyFiles =
        ["OptiScaler.asi", "nvngx.dll_dlssnr.dll", "Remove OptiScaler.bat", "Remove_OptiScaler.bat", "OptiScaler.dll"];

    private static readonly string[] PackageDirs = ["OptiScaler", "Licenses"];
    private static readonly string[] LogFiles = ["OptiScaler.log", "ReShade.log", "ReShade64.log"];

    private sealed record Ctx(string Root, string Target, InstallManifest M)
    {
        public string Rel(string full) => Path.GetRelativePath(Root, full);
        public string Full(string rel) => Path.GetFullPath(Path.Combine(Root, rel));
        public string BackupDir => InstallManifest.BackupDirFor(Target);
    }

    // ---------- install / update ----------

    public async Task InstallAsync(GameInfo game, string targetDir, InstallOptions o, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        targetDir = FileUtil.Normalize(targetDir);
        EnsureNotRunning(game.Root);
        EnsureWritable(targetDir);
        try
        {
            await InstallCoreAsync(game, targetDir, o, progress, ct);
        }
        finally
        {
            FileUtil.TryDeleteEmptyDir(InstallManifest.DirFor(targetDir));
        }
    }

    private async Task InstallCoreAsync(GameInfo game, string targetDir, InstallOptions o, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        // Fetch everything first so a network failure never leaves a half-installed game.
        var pkg = o.Opti ? await store.EnsureOptiAsync(progress, ct) : null;
        var mfg = o.Mfg ? await store.EnsureMfgAsync(progress, ct) : null;
        var dlss = o.Dlss ? await store.EnsureDlssAsync(progress, ct) : null;
        if (o.DlssNr && !File.Exists(store.DlssNrPath))
            throw new FileNotFoundException("nvngx_dlssnr.dll has not been imported (Settings → Components).");
        if (o.ReShade && !File.Exists(store.ReShadePath))
            throw new FileNotFoundException("ReShade64.dll has not been imported (Settings → Components).");

        progress?.Report(new TransferProgress($"Installing to {game.Name}", null));
        var m = InstallManifest.Load(targetDir) ?? new InstallManifest();
        m.RootRel = Path.GetRelativePath(targetDir, game.Root);
        var ctx = new Ctx(FileUtil.Normalize(game.Root), targetDir, m);

        await Task.Run(() =>
        {
            try
            {
                if (pkg is not null)
                {
                    InstallOpti(ctx, pkg, o);
                    m.Opti = true;
                    m.OptiTag = store.Opti?.Tag;
                    m.Proxy = o.Proxy;
                }
                if (o.ReShade)
                {
                    Place(ctx, store.ReShadePath, Path.Combine(targetDir, ComponentStore.ReShadeFile));
                    m.ReShade = true;
                    m.ReShadeSha = HashCache.Get(store.ReShadePath);
                }
                if (mfg is not null)
                {
                    Place(ctx, mfg, Path.Combine(targetDir, ComponentStore.MfgFile));
                    m.Mfg = true;
                    m.MfgTag = store.Mfg?.Tag ?? "local";
                }
                if (o.DlssNr)
                {
                    Place(ctx, store.DlssNrPath, Path.Combine(targetDir, ComponentStore.DlssNrFile));
                    m.DlssNr = true;
                    m.DlssNrSha = HashCache.Get(store.DlssNrPath);
                }
                if (dlss is not null)
                {
                    SwapDlss(ctx, dlss, o.AddMissingDlss);
                    m.Dlss = true;
                    m.DlssTag = store.Dlss?.Tag;
                }
                m.AntiCheatConfirmed |= o.AntiCheatConfirmed;
                Log.Info($"{game.Name}: done ({targetDir})");
            }
            catch (UnauthorizedAccessException) { throw new NeedsAdminException(targetDir); }
            catch (IOException ex) when (IsSharingViolation(ex)) { throw new IOException($"A game file is in use. Close {game.Name} and retry.", ex); }
            finally
            {
                if (!m.IsEmpty) m.Save(targetDir);
            }
        }, ct);
    }

    private static void InstallOpti(Ctx ctx, string pkg, InstallOptions o)
    {
        var m = ctx.M;

        // Other proxies that are OptiScaler, plus files the fork no longer uses.
        foreach (var name in AppSettings.ProxyNames.Where(n => !n.Equals(o.Proxy, StringComparison.OrdinalIgnoreCase)))
        {
            var f = Path.Combine(ctx.Target, name);
            if (File.Exists(f) && IsOptiScaler(f)) Displace(ctx, f);
        }
        foreach (var name in LegacyFiles)
        {
            var f = Path.Combine(ctx.Target, name);
            if (File.Exists(f)) Displace(ctx, f);
        }

        Place(ctx, Path.Combine(pkg, "OptiScaler.dll"), Path.Combine(ctx.Target, o.Proxy));

        foreach (var dir in PackageDirs)
        {
            var src = Path.Combine(pkg, dir);
            if (!Directory.Exists(src)) continue;
            var dest = Path.Combine(ctx.Target, dir);
            var owned = m.Dirs.Contains(ctx.Rel(dest), StringComparer.OrdinalIgnoreCase);
            if (owned || !Directory.Exists(dest))
            {
                MirrorDir(src, dest);
                m.AddDir(ctx.Rel(dest));
            }
            else
            {
                // Folder predates us (e.g. an earlier manual install): track file by file so uninstall leaves it as it was.
                foreach (var file in PackageFiles(src))
                    Place(ctx, file, Path.Combine(dest, Path.GetRelativePath(src, file)));
            }
        }

        var iniPath = Path.Combine(ctx.Target, "OptiScaler.ini");
        var releaseIni = File.ReadAllText(Path.Combine(pkg, "OptiScaler.ini"));
        string? currentIni = File.Exists(iniPath) ? File.ReadAllText(iniPath) : null;
        if (currentIni is not null && !m.Owns(ctx.Rel(iniPath))) Backup(ctx, iniPath, "file", copy: true);
        FileUtil.AtomicWriteText(iniPath, ConfigProfile.Merge(releaseIni, currentIni, o.Overrides, o.CarryOverIni));
        m.AddFile(ctx.Rel(iniPath));
        Log.Info($"  {o.Proxy} ← OptiScaler-NR, OptiScaler.ini merged");
    }

    /// <summary>Copies the package folder in place, skipping unchanged files and docs.</summary>
    private static IEnumerable<string> PackageFiles(string src) =>
        Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".md", StringComparison.OrdinalIgnoreCase));

    private static void MirrorDir(string src, string dest)
    {
        foreach (var file in PackageFiles(src))
        {
            var target = Path.Combine(dest, Path.GetRelativePath(src, file));
            if (FileUtil.SameFile(file, target)) continue;
            FileUtil.AtomicCopy(file, target);
        }
    }

    private static void SwapDlss(Ctx ctx, IReadOnlyDictionary<string, string> latest, bool addMissing)
    {
        var m = ctx.M;
        var scan = new GameInfo { Root = ctx.Root };
        GameInspector.Inspect(scan);

        foreach (var dll in scan.Dlss)
        {
            if (FileUtil.IsUnder(dll.Path, InstallManifest.DirFor(ctx.Target))) continue;
            if (!latest.TryGetValue(dll.Name, out var src)) continue;
            var newVer = FileUtil.ReadVersion(src);
            var rel = ctx.Rel(dll.Path);
            if (dll.Version is not null && newVer is not null && dll.Version >= newVer) continue;

            var srcSha = HashCache.Get(src);
            var entry = m.BackupOf(rel);
            var ours = m.Owns(rel) || (entry?.InstalledSha is { } s && s == HashCache.Get(dll.Path));
            if (!ours) Backup(ctx, dll.Path, "dlss");
            FileUtil.AtomicCopy(src, dll.Path);
            if (m.BackupOf(rel) is { } b) b.InstalledSha = srcSha;
            Log.Info($"  {rel}: {FileUtil.Format(dll.Version)} → {FileUtil.Format(newVer)}");
        }

        if (addMissing && !scan.Dlss.Any(d => d.Name.Equals("nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase)))
        {
            var dest = Path.Combine(ctx.Target, "nvngx_dlss.dll");
            Place(ctx, latest["nvngx_dlss.dll"], dest);
            Log.Info($"  added {ctx.Rel(dest)}");
        }
    }

    /// <summary>Puts <paramref name="src"/> at <paramref name="dest"/>, backing up anything we don't own.</summary>
    private static void Place(Ctx ctx, string src, string dest)
    {
        var rel = ctx.Rel(dest);
        if (File.Exists(dest))
        {
            if (ctx.M.Owns(rel) && FileUtil.SameFile(src, dest)) return;
            if (!ctx.M.Owns(rel)) Backup(ctx, dest, "file");
        }
        FileUtil.AtomicCopy(src, dest);
        ctx.M.AddFile(rel);
    }

    /// <summary>Removes a conflicting file: deleted if we placed it, otherwise moved to the backup.</summary>
    private static void Displace(Ctx ctx, string full)
    {
        var rel = ctx.Rel(full);
        if (ctx.M.Owns(rel))
        {
            File.Delete(full);
            ctx.M.Files.RemoveAll(f => f.Equals(rel, StringComparison.OrdinalIgnoreCase));
        }
        else Backup(ctx, full, "file");
        Log.Info($"  removed {rel}");
    }

    private static void Backup(Ctx ctx, string full, string kind, bool copy = false)
    {
        var rel = ctx.Rel(full);
        var entry = ctx.M.BackupOf(rel);
        var backupRel = entry?.Backup ?? rel.Replace("..", "_up");
        var dest = Path.Combine(ctx.BackupDir, backupRel);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        if (copy) File.Copy(full, dest, true);
        else File.Move(full, dest, true);
        if (entry is null) ctx.M.Backups.Add(new BackupEntry { Original = rel, Backup = backupRel, Kind = kind });
        else entry.InstalledSha = null;
    }

    // ---------- uninstall / restore ----------

    public Task UninstallAsync(GameInfo game, string targetDir, CancellationToken ct) => Task.Run(() =>
    {
        targetDir = FileUtil.Normalize(targetDir);
        EnsureNotRunning(game.Root);
        var m = InstallManifest.Load(targetDir) ?? throw new InvalidOperationException("No DLSS Updater install found here.");
        var ctx = new Ctx(FileUtil.Normalize(Path.Combine(targetDir, m.RootRel)), targetDir, m);
        try
        {
            foreach (var rel in m.Files) TryDelete(ctx.Full(rel));
            foreach (var rel in m.Dirs)
            {
                var d = ctx.Full(rel);
                if (Directory.Exists(d)) Directory.Delete(d, true);
            }
            foreach (var b in m.Backups) Restore(ctx, b);
            foreach (var log in LogFiles) TryDelete(Path.Combine(targetDir, log));
            Directory.Delete(InstallManifest.DirFor(targetDir), true);
            Log.Info($"{game.Name}: uninstalled, originals restored");
        }
        catch (UnauthorizedAccessException) { throw new NeedsAdminException(targetDir); }
    }, ct);

    public Task RestoreDlssAsync(GameInfo game, string targetDir, CancellationToken ct) => Task.Run(() =>
    {
        targetDir = FileUtil.Normalize(targetDir);
        EnsureNotRunning(game.Root);
        var m = InstallManifest.Load(targetDir) ?? throw new InvalidOperationException("No DLSS Updater install found here.");
        var ctx = new Ctx(FileUtil.Normalize(Path.Combine(targetDir, m.RootRel)), targetDir, m);
        try
        {
            foreach (var b in m.Backups.Where(b => b.Kind == "dlss").ToList())
            {
                Restore(ctx, b);
                m.Backups.Remove(b);
            }
            foreach (var rel in m.Files.Where(f => ComponentStore.DlssFiles.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)).ToList())
            {
                TryDelete(ctx.Full(rel));
                m.Files.Remove(rel);
            }
            m.Dlss = false;
            m.DlssTag = null;
            if (m.IsEmpty) Directory.Delete(InstallManifest.DirFor(targetDir), true);
            else m.Save(targetDir);
            Log.Info($"{game.Name}: original DLSS files restored");
        }
        catch (UnauthorizedAccessException) { throw new NeedsAdminException(targetDir); }
    }, ct);

    private static void Restore(Ctx ctx, BackupEntry b)
    {
        var src = Path.Combine(ctx.BackupDir, b.Backup);
        if (!File.Exists(src)) return;
        var dest = ctx.Full(b.Original);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Move(src, dest, true);
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    // ---------- checks ----------

    public static bool IsOptiScaler(string path) =>
        string.Equals(FileUtil.OriginalFilename(path), "OptiScaler.dll", StringComparison.OrdinalIgnoreCase);

    private static bool IsSharingViolation(IOException ex) => (ex.HResult & 0xFFFF) is 32 or 33;

    private static void EnsureWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(InstallManifest.DirFor(dir));
            var probe = Path.Combine(InstallManifest.DirFor(dir), ".probe");
            File.WriteAllText(probe, "");
            File.Delete(probe);
        }
        catch (UnauthorizedAccessException) { throw new NeedsAdminException(dir); }
    }

    public static void EnsureNotRunning(string root)
    {
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (p.Id <= 4) continue;
                var path = ProcessPath(p.Id);
                if (path is not null && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && FileUtil.IsUnder(path, root))
                    throw new GameRunningException(path);
            }
        }
    }

    private static string? ProcessPath(int pid)
    {
        var h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString(0, size) : null;
        }
        finally { CloseHandle(h); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}

/// <summary>SHA-256 memo keyed by path + size + mtime; big dlls get hashed once per session.</summary>
public static class HashCache
{
    private static readonly Dictionary<(string, long, DateTime), string> Cache = new();

    public static string Get(string path)
    {
        var fi = new FileInfo(path);
        var key = (fi.FullName.ToLowerInvariant(), fi.Length, fi.LastWriteTimeUtc);
        lock (Cache)
            if (Cache.TryGetValue(key, out var h)) return h;
        var hash = FileUtil.Sha256(path);
        lock (Cache) Cache[key] = hash;
        return hash;
    }
}
