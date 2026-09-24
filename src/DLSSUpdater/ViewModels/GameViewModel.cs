using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.ViewModels;

public enum RowState { None, Current, Update, Missing }

public sealed partial class ComponentRow(string key, string name) : ObservableObject
{
    public string Key { get; } = key;
    public string Name { get; } = name;
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private string _installed = "—";
    [ObservableProperty] private string _latest = "—";
    [ObservableProperty] private RowState _state;
}

public sealed record DlssRow(string RelPath, string Kind, string Current, string Latest, RowState State);

public sealed record TargetOption(string Dir, string Label, string? Exe)
{
    public override string ToString() => Label;
}

/// <summary>Shared app state the per-game view models read from.</summary>
public sealed class Services
{
    public required AppSettings Settings { get; init; }
    public required ComponentStore Store { get; init; }
    public required Installer Installer { get; init; }
    public string? DlssNrSha { get; set; }
    public string? ReShadeSha { get; set; }
}

public sealed partial class GameViewModel : ObservableObject
{
    private readonly Services _s;
    private bool _loading;

    public GameViewModel(GameInfo info, Services services)
    {
        _s = services;
        Components =
        [
            new ComponentRow("opti", "OptiScaler-NR"),
            new ComponentRow("reshade", "ReShade"),
            new ComponentRow("mfg", "MFG Unlock"),
            new ComponentRow("dlssnr", "DLSSNR runtime"),
            new ComponentRow("dlss", "DLSS SR · RR · FG"),
        ];
        foreach (var c in Components) c.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ComponentRow.Enabled)) OnPropertyChanged(nameof(AnyEnabled));
        };
        Info = info;
        Load(info);
    }

    public GameInfo Info { get; private set; }
    public string Id => Info.Id;
    public string Name => Info.Name;
    public string Source => Info.Source;
    public string Root => Info.Root;
    public bool IsManual => Info.Source is "Manual" or "Library";

    public ObservableCollection<TargetOption> Targets { get; } = [];
    public ObservableCollection<ComponentRow> Components { get; }
    public ObservableCollection<DlssRow> DlssRows { get; } = [];
    public IReadOnlyList<string> ProxyNames => AppSettings.ProxyNames;

    [ObservableProperty] private TargetOption? _selectedTarget;
    [ObservableProperty] private string _proxy = "dxgi.dll";
    [ObservableProperty] private InstallManifest? _manifest;

    public bool HasDlss => Info.Dlss.Count > 0;
    public bool HasAntiCheat => Info.AntiCheat is not null;
    public string? AntiCheat => Info.AntiCheat;
    public bool IsInstalled => Manifest is not null || Info.Installs.Count > 0;
    public bool IsOptiInstalled => Manifest?.Opti == true;
    public bool AnyEnabled => Components.Any(c => c.Enabled);

    public string DlssBadge
    {
        get
        {
            var v = Info.Dlss.Where(d => d.Kind == "SR").Select(d => d.Version).Max()
                    ?? Info.Dlss.Select(d => d.Version).Max();
            return v is null ? "DLSS" : $"DLSS {v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";
        }
    }

    public bool DlssCurrent => LatestDlss is { } l && Info.Dlss.All(d => d.Version is { } v && Trim(v) >= l);

    [ObservableProperty] private bool _needsUpdate;

    public string ActionText => Manifest is null ? "Install" : NeedsUpdate ? "Update" : "Reinstall";
    public string Subtitle => Info.Exes.Count == 0 ? "No executable found" : Path.GetFileName(Info.Exes[0]);

    private Version? LatestDlss => _s.Store.Dlss?.Version is { } v ? Trim(v) : null;
    private static Version Trim(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    public void Load(GameInfo info)
    {
        Info = info;
        _loading = true;
        var over = _s.Settings.Games.GetValueOrDefault(Id);

        Targets.Clear();
        foreach (var t in BuildTargets(info, over?.TargetDir)) Targets.Add(t);
        var preferred = info.Installs.FirstOrDefault() ?? over?.TargetDir;
        SelectedTarget = Targets.FirstOrDefault(t => preferred is not null && t.Dir.Equals(preferred, StringComparison.OrdinalIgnoreCase))
                         ?? Targets.FirstOrDefault();

        _loading = false;
        LoadManifest();
        OnPropertyChanged(string.Empty);
    }

    private static IEnumerable<TargetOption> BuildTargets(GameInfo info, string? custom)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in info.Installs)
            if (seen.Add(dir)) yield return Option(info.Root, dir, info.Exes.FirstOrDefault(e => Path.GetDirectoryName(e)!.Equals(dir, StringComparison.OrdinalIgnoreCase)));
        foreach (var exe in info.Exes.Take(8))
        {
            var dir = Path.GetDirectoryName(exe)!;
            if (seen.Add(dir)) yield return Option(info.Root, dir, exe);
        }
        if (custom is not null && Directory.Exists(custom) && seen.Add(custom)) yield return Option(info.Root, custom, null);
        if (seen.Add(info.Root)) yield return Option(info.Root, info.Root, null);
    }

    private static TargetOption Option(string root, string dir, string? exe)
    {
        var rel = FileUtil.IsUnder(dir, root) ? Path.GetRelativePath(root, dir) : dir;
        var label = rel == "." ? "(game folder)" : rel;
        if (exe is not null) label += "   ·   " + Path.GetFileName(exe);
        return new TargetOption(dir, label, exe);
    }

    public void AddCustomTarget(string dir)
    {
        var opt = Option(Root, dir, null);
        Targets.Add(opt);
        SelectedTarget = opt;
    }

    partial void OnSelectedTargetChanged(TargetOption? value)
    {
        if (_loading) return;
        if (value is not null) _s.Settings.For(Id).TargetDir = value.Dir;
        _s.Settings.Save();
        LoadManifest();
    }

    partial void OnProxyChanged(string value)
    {
        if (_loading) return;
        _s.Settings.For(Id).Proxy = value;
        _s.Settings.Save();
    }

    private void LoadManifest()
    {
        _loading = true;
        Manifest = SelectedTarget is null ? null : InstallManifest.Load(SelectedTarget.Dir);
        Proxy = Manifest?.Proxy ?? _s.Settings.Games.GetValueOrDefault(Id)?.Proxy ?? ExistingOptiProxy() ?? _s.Settings.DefaultProxy;

        var m = Manifest;
        Set("opti", m?.Opti ?? true);
        Set("reshade", m?.ReShade ?? _s.Settings.InstallReShade);
        Set("mfg", m?.Mfg ?? _s.Settings.InstallMfgUnlock);
        Set("dlssnr", m?.DlssNr ?? _s.Settings.InstallDlssNr);
        Set("dlss", m?.Dlss ?? true);
        _loading = false;
        RefreshStatus();

        void Set(string key, bool on) => Components.First(c => c.Key == key).Enabled = on;
    }

    /// <summary>A manual OptiScaler install keeps its proxy name so the update replaces the file the game loads.</summary>
    private string? ExistingOptiProxy() => SelectedTarget is null ? null
        : AppSettings.ProxyNames.FirstOrDefault(n => File.Exists(Path.Combine(SelectedTarget.Dir, n)) && Installer.IsOptiScaler(Path.Combine(SelectedTarget.Dir, n)));

    /// <summary>Recomputes installed-vs-latest for every component and DLSS file.</summary>
    public void RefreshStatus()
    {
        var m = Manifest;
        var store = _s.Store;

        Row("opti", m?.Opti == true ? m.OptiTag : null, store.Opti?.Tag, true);
        Row("reshade", m?.ReShade == true ? InstalledVersion(ComponentStore.ReShadeFile) : null, File.Exists(store.ReShadePath) ? ReShadeVersion() : null,
            m?.ReShade != true || m.ReShadeSha == _s.ReShadeSha);
        Row("mfg", m?.Mfg == true ? m.MfgTag : null, store.Mfg?.Tag, true);
        Row("dlssnr", m?.DlssNr == true ? InstalledVersion(ComponentStore.DlssNrFile) : null, File.Exists(store.DlssNrPath) ? DlssNrLabel() : null,
            m?.DlssNr != true || m.DlssNrSha == _s.DlssNrSha);

        var latest = LatestDlss;
        DlssRows.Clear();
        foreach (var d in Info.Dlss)
        {
            var rel = Path.GetRelativePath(Root, d.Path);
            var state = d.Version is null || latest is null ? RowState.None : Trim(d.Version) >= latest ? RowState.Current : RowState.Update;
            DlssRows.Add(new DlssRow(rel, d.Kind, FileUtil.Format(d.Version), latest is null ? "—" : FileUtil.Format(latest), state));
        }
        var dlssRow = Components.First(c => c.Key == "dlss");
        dlssRow.Installed = m?.Dlss == true ? m.DlssTag ?? "—" : HasDlss ? "game" : "—";
        dlssRow.Latest = store.Dlss?.Tag ?? "—";
        dlssRow.State = !HasDlss ? RowState.Missing : DlssCurrent ? RowState.Current : RowState.Update;

        NeedsUpdate = m is not null && Components.Where(c => c.Key != "dlss" || m.Dlss).Any(c => c.State == RowState.Update && IsTracked(m, c.Key));
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(IsOptiInstalled));
        OnPropertyChanged(nameof(DlssCurrent));
    }

    private static bool IsTracked(InstallManifest m, string key) => key switch
    {
        "opti" => m.Opti,
        "reshade" => m.ReShade,
        "mfg" => m.Mfg,
        "dlssnr" => m.DlssNr,
        _ => m.Dlss,
    };

    private void Row(string key, string? installed, string? latest, bool hashMatches)
    {
        var row = Components.First(c => c.Key == key);
        row.Installed = installed ?? "—";
        row.Latest = latest ?? "—";
        row.State = latest is null ? RowState.Missing
            : installed is null ? RowState.None
            : key is "reshade" or "dlssnr" ? (hashMatches ? RowState.Current : RowState.Update)
            : installed == latest ? RowState.Current : RowState.Update;
    }

    private string ReShadeVersion() => FileUtil.Format(FileUtil.ReadVersion(_s.Store.ReShadePath));

    private string DlssNrLabel()
    {
        var v = FileUtil.Format(FileUtil.ReadVersion(_s.Store.DlssNrPath));
        return _s.DlssNrSha is { } h && ComponentStore.KnownDlssNr.ContainsKey(h) ? v : $"{v} (unverified)";
    }

    private string? InstalledVersion(string file) =>
        SelectedTarget is null ? null : FileUtil.Format(FileUtil.ReadVersion(Path.Combine(SelectedTarget.Dir, file)));

    public InstallOptions BuildOptions(bool dlssOnly, bool acConfirmed) => new()
    {
        Opti = !dlssOnly && On("opti"),
        ReShade = !dlssOnly && On("reshade"),
        Mfg = !dlssOnly && On("mfg"),
        DlssNr = !dlssOnly && On("dlssnr"),
        Dlss = dlssOnly || On("dlss"),
        AddMissingDlss = _s.Settings.AddMissingDlss && !dlssOnly && On("opti"),
        CarryOverIni = _s.Settings.CarryOverGameIni,
        Proxy = Proxy,
        Overrides = _s.Settings.IniOverrides,
        AntiCheatConfirmed = acConfirmed,
    };

    /// <summary>Options that reproduce what is installed, for "Update all".</summary>
    public InstallOptions UpdateOptions(InstallManifest m) => new()
    {
        Opti = m.Opti,
        ReShade = m.ReShade,
        Mfg = m.Mfg,
        DlssNr = m.DlssNr,
        Dlss = m.Dlss,
        AddMissingDlss = false,
        CarryOverIni = _s.Settings.CarryOverGameIni,
        Proxy = m.Proxy ?? Proxy,
        Overrides = _s.Settings.IniOverrides,
        AntiCheatConfirmed = m.AntiCheatConfirmed,
    };

    private bool On(string key) => Components.First(c => c.Key == key).Enabled;

    public bool NeedsInjection(InstallOptions o) => o.Opti || o.ReShade || o.Mfg || o.DlssNr;
}
