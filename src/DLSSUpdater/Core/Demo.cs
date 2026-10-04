using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

/// <summary>
/// <c>--demo</c>: made-up games and releases for screenshots, store videos and trying the interface.
/// Runs in a throwaway data folder; never scans, downloads, touches game folders or reads driver profiles,
/// and installs only change the in-memory games.
/// </summary>
public static class Demo
{
    public static bool Active { get; internal set; }

    public const string LatestDlss = "v310.10.0";
    public const string LatestStreamline = "v2.14.1";

    private static readonly Dictionary<string, InstallManifest> Manifests = new(StringComparer.OrdinalIgnoreCase);
    private static List<GameInfo> _games = [];

    /// <summary>Switches to a fresh data folder under %TEMP%; call before anything reads settings.</summary>
    public static void Enable()
    {
        var root = Path.Combine(Path.GetTempPath(), "DLSSUpdater-demo");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        AppPaths.Root = root;
        Active = true;
        Reset();
    }

    /// <summary>The releases the demo pretends are the newest.</summary>
    public static void Seed(ComponentStore store)
    {
        var published = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);
        store.Dlss = Release(LatestDlss, published);
        store.DlssReleases = [store.Dlss, Release("v310.9.1", published.AddDays(-41)), Release("v310.4.0", published.AddDays(-150)), Release("v310.2.1", published.AddDays(-230))];
        store.Streamline = Release(LatestStreamline, published.AddDays(-20));
        store.Opti = Release("v0.9.2", published.AddDays(-9));
        store.Mfg = Release("v1.4.0", published.AddDays(-30));
        store.ReShade = Release("6.6.2", published.AddDays(-60));
    }

    private static ReleaseInfo Release(string tag, DateTime published) => new() { Tag = tag, Published = published };

    public static List<GameInfo> Games => _games;

    public static GameInfo? Find(string root) => _games.FirstOrDefault(g => g.Root.Equals(root, StringComparison.OrdinalIgnoreCase));

    public static InstallManifest? ManifestAt(string dir) => Manifests.GetValueOrDefault(dir);

    /// <summary>Pretends to download and install: only the in-memory game and manifest change.</summary>
    public static async Task InstallAsync(ComponentStore store, GameInfo game, string targetDir, InstallOptions o, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var dlss = store.DlssFor(o.DlssTag) ?? throw new InvalidOperationException("No DLSS release");
        // Paced by the clock, not by steps, so a busy UI thread doesn't stretch it.
        var download = System.Diagnostics.Stopwatch.StartNew();
        for (double f; (f = download.ElapsedMilliseconds / 1000.0) < 1;)
        {
            progress?.Report(new TransferProgress($"Downloading DLSS {dlss.Tag}", f));
            await Task.Delay(40, ct);
        }
        progress?.Report(new TransferProgress($"Downloading DLSS {dlss.Tag}", 1));
        progress?.Report(new TransferProgress($"Installing to {game.Name}", null));
        await Task.Delay(500, ct);

        var m = ManifestAt(targetDir) ?? new InstallManifest { RootRel = Path.GetRelativePath(targetDir, game.Root) };
        if (o.Dlss)
        {
            foreach (var d in game.Dlss)
            {
                Originals.TryAdd(d, d.Version);
                d.Version = dlss.Version;
            }
            m.Dlss = true;
            m.DlssTag = dlss.Tag;
            m.DlssPinned = o.DlssTag is not null;
        }
        if (o.Opti)
        {
            m.Opti = true;
            m.OptiTag = store.Opti?.Tag;
            m.Proxy = o.Proxy;
        }
        if (o.Mfg)
        {
            m.Mfg = true;
            m.MfgTag = store.Mfg?.Tag;
        }
        if (o.Streamline && store.Streamline is { } sl)
        {
            foreach (var d in game.Streamline) d.Version = sl.Version;
            m.Streamline = true;
            m.StreamlineTag = sl.Tag;
        }
        m.Updated = DateTime.UtcNow;
        Manifests[targetDir] = m;
        if (!game.Installs.Contains(targetDir, StringComparer.OrdinalIgnoreCase)) game.Installs.Add(targetDir);
        Log.Info($"{game.Name}: DLSS {dlss.Tag} installed");
    }

    public static async Task UninstallAsync(GameInfo game, string targetDir, CancellationToken ct)
    {
        await Task.Delay(400, ct);
        Manifests.Remove(targetDir);
        game.Installs.RemoveAll(d => d.Equals(targetDir, StringComparison.OrdinalIgnoreCase));
        foreach (var d in game.Dlss) d.Version = Originals.GetValueOrDefault(d, d.Version);
        Log.Info($"{game.Name}: uninstalled, originals restored");
    }

    public static Task RestoreDlssAsync(GameInfo game, string targetDir, CancellationToken ct) => UninstallAsync(game, targetDir, ct);

    // [title, launcher, exe folder under the game root, exe, DLSS SR/RR/FG versions (null = not shipped),
    //  Streamline, anti-cheat, installed DLSS tag (null = not installed by the app)]
    private static readonly (string Name, string Source, string Bin, string Exe, string? Sr, string? Rr, string? Fg, bool Sl, string? Ac, string? Tag)[] Catalog =
    [
        ("Ember Crown", "Steam", @"EmberCrown\Binaries\Win64", "EmberCrown-Win64-Shipping.exe", "310.2.1", "310.2.1", "310.2.1", true, null, "v310.2.1"),
        ("Frostline", "Steam", "bin", "Frostline.exe", "3.7.10", null, "3.7.10", false, null, null),
        ("Grimwald", "GOG", "", "Grimwald.exe", "310.10.0", null, null, false, null, LatestDlss),
        ("Hollow Tide", "Epic", @"HollowTide\Binaries\Win64", "HollowTide-Win64-Shipping.exe", "3.5.10", null, null, false, null, null),
        ("Iron Veil", "Steam", @"x64", "IronVeil.exe", "310.9.1", "310.9.1", "310.9.1", true, "EasyAntiCheat", "v310.9.1"),
        ("Lumen Drift", "Steam", "", "LumenDrift.exe", null, null, null, false, null, null),
        ("Neon Meridian", "Epic", @"bin\x64", "NeonMeridian.exe", "310.4.0", "310.4.0", "310.4.0", true, null, null),
        ("Quiet Harbor", "GOG", "", "QuietHarbor.exe", null, null, null, false, null, null),
        ("Sable Run", "Steam", @"SableRun\Binaries\Win64", "SableRun-Win64-Shipping.exe", "2.5.1", null, null, false, null, null),
        ("Starfall Protocol", "Steam", @"Starfall\Binaries\Win64", "Starfall-Win64-Shipping.exe", "310.10.0", "310.10.0", "310.10.0", true, null, LatestDlss),
        ("Tidebreaker", "Manual", "", "Tidebreaker.exe", "3.1.30", null, null, false, null, null),
        ("Wicker & Ash", "Steam", @"bin\win64", "WickerAndAsh.exe", "310.9.1", null, "310.9.1", false, null, "v310.9.1"),
    ];

    private static readonly Dictionary<DlssDll, Version?> Originals = [];

    /// <summary>Rebuilds the made-up library; games live on a drive the demo never writes to.</summary>
    public static void Reset()
    {
        Manifests.Clear();
        Originals.Clear();
        _games = Catalog.Select(c =>
        {
            var root = $@"D:\Games\{c.Name}";
            var bin = c.Bin.Length == 0 ? root : Path.Combine(root, c.Bin);
            var dlss = new List<DlssDll>();
            if (c.Sr is not null) dlss.Add(Dll(bin, "nvngx_dlss.dll", c.Sr));
            if (c.Rr is not null) dlss.Add(Dll(bin, "nvngx_dlssd.dll", c.Rr));
            if (c.Fg is not null) dlss.Add(Dll(bin, "nvngx_dlssg.dll", c.Fg));
            var sl = c.Sl
                ? new List<DlssDll> { Dll(bin, "sl.interposer.dll", "2.7.30"), Dll(bin, "sl.dlss.dll", "2.7.30"), Dll(bin, "sl.reflex.dll", "2.7.30"), Dll(bin, "sl.dlss_g.dll", "2.7.30") }
                : [];
            var game = new GameInfo
            {
                Id = GameInfo.MakeId(root), Name = c.Name, Source = c.Source, Root = root,
                Exes = [Path.Combine(bin, c.Exe)], Dlss = dlss, Streamline = sl, AntiCheat = c.Ac,
                Scanned = new DateTime(2026, 11, 23, 18, 0, 0, DateTimeKind.Utc),
            };
            foreach (var d in dlss) Originals[d] = d.Version;
            if (c.Tag is not null)
            {
                game.Installs.Add(bin);
                Manifests[bin] = new InstallManifest { RootRel = Path.GetRelativePath(bin, root), Dlss = true, DlssTag = c.Tag };
            }
            return game;
        }).ToList();
    }

    private static DlssDll Dll(string dir, string name, string version) =>
        new() { Path = Path.Combine(dir, name), Name = name, Version = Version.Parse(version + ".0") };
}
