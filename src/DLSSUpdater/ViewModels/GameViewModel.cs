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
    /// <summary>Hidden when it doesn't apply to the install mode (OptiScaler / DLSSNR in ReShade-only).</summary>
    [ObservableProperty] private bool _visible = true;
    /// <summary>False when the mode requires this component (ReShade in ReShade-only).</summary>
    [ObservableProperty] private bool _editable = true;
    [ObservableProperty] private string _installed = "—";
    [ObservableProperty] private string _latest = "—";
    [ObservableProperty] private RowState _state;
}

public sealed record DlssRow(string RelPath, string Kind, string Current, string Latest, RowState State);

/// <summary>A DLSS version choice; Tag null = follow the default.</summary>
public sealed record DlssChoice(string? Tag, string Label)
{
    public override string ToString() => Label;
}

public sealed record ModeChoice(InstallMode Mode, string Label, string Description)
{
    public static readonly ModeChoice[] All =
    [
        new(InstallMode.OptiScaler, "OptiScaler-NR", "OptiScaler-NR as the proxy; it loads ReShade and add-ons (and DLSSNR, if enabled)."),
        new(InstallMode.ReShadeOnly, "ReShade + add-ons", "ReShade as the proxy with its add-ons and DLSS updates, without OptiScaler."),
    ];

    public override string ToString() => Label;
}

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
    public SignatureStatus? DlssNrSignature { get; set; }
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
            new ComponentRow("streamline", "Streamline (Dynamic MFG)"),
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
    public IReadOnlyList<string> ProxyNames => IsReShadeOnly ? AppSettings.ReShadeProxyNames : AppSettings.ProxyNames;
    public IReadOnlyList<ModeChoice> Modes => ModeChoice.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReShadeOnly), nameof(ProxyNames), nameof(ModeChoice), nameof(ProxyHint))]
    private InstallMode _mode;

    public bool IsReShadeOnly => Mode == InstallMode.ReShadeOnly;
    public string ProxyHint => IsReShadeOnly
        ? "Files go next to the game executable. Proxy is the name ReShade is loaded as (dxgi.dll for DX10–12, d3d9.dll, opengl32.dll …)."
        : "Files go next to the game executable. Proxy is the name OptiScaler is loaded as.";

    public ModeChoice ModeChoice
    {
        get => ModeChoice.All.First(m => m.Mode == Mode);
        set
        {
            if (value is null) return;
            Mode = value.Mode;
        }
    }

    partial void OnModeChanged(InstallMode value)
    {
        ApplyModeToRows();
        if (!ProxyNames.Contains(Proxy, StringComparer.OrdinalIgnoreCase)) Proxy = "dxgi.dll";
        if (_loading) return;
        _s.Settings.For(Id).Mode = value == _s.Settings.DefaultMode ? null : value;
        _s.Settings.Save();
        RefreshStatus();
    }

    private void ApplyModeToRows()
    {
        foreach (var c in Components)
        {
            // Unofficial components while opted out: shown only where a game already has them, never installed or updated.
            var gated = IsGated(c.Key);
            c.Visible = !(IsReShadeOnly && c.Key is "opti" or "dlssnr") && (!gated || (Manifest is { } m && IsTracked(m, c.Key)));
            c.Editable = !(IsReShadeOnly && c.Key == "reshade") && !gated;
            if (gated) c.Enabled = false;
            else if (!c.Editable) c.Enabled = true;
        }
        OnPropertyChanged(nameof(AnyEnabled));
    }

    private bool AllowUnofficial => _s.Settings.AllowUnofficial;
    private bool IsGated(string key) => !AllowUnofficial && key is "mfg" or "dlssnr";

    /// <summary>Re-reads the rows after the unofficial-components setting changed.</summary>
    public void ReloadComponents() => LoadManifest();

    [ObservableProperty] private TargetOption? _selectedTarget;
    [ObservableProperty] private string _proxy = "dxgi.dll";
    [ObservableProperty] private InstallManifest? _manifest;
    [ObservableProperty] private DlssChoice? _dlssChoice;
    [ObservableProperty] private string _presetChoice = GlobalPreset;
    [ObservableProperty] private string _detailTab = "Install";
    [ObservableProperty] private DriverProfileViewModel? _driver;

    /// <summary>The exe the driver profile is matched on.</summary>
    public string? DriverExe => SelectedTarget?.Exe ?? Info.Exes.FirstOrDefault();

    partial void OnDetailTabChanged(string value)
    {
        if (value != "Nvidia") return;
        if (Driver is null && DriverExe is { } exe) Driver = new DriverProfileViewModel(exe, Name, Features, Info.FgVersion);
        if (Driver is { Loaded: false, Busy: false } d) d.LoadCommand.Execute(null);
    }

    public const string GlobalPreset = "Current settings";
    public ObservableCollection<string> PresetChoices { get; } = [];

    public void RebuildPresetChoices()
    {
        var wasLoading = _loading;
        _loading = true;
        PresetChoices.Clear();
        PresetChoices.Add(GlobalPreset);
        foreach (var p in _s.Settings.AllPresets) PresetChoices.Add(p.Name);
        PresetChoice = _s.Settings.PresetFor(Id)?.Name ?? GlobalPreset;
        _loading = wasLoading;
    }

    partial void OnPresetChoiceChanged(string value)
    {
        if (_loading) return;
        _s.Settings.For(Id).Preset = value == GlobalPreset ? null : value;
        _s.Settings.Save();
    }

    private IReadOnlyList<IniOverride> OptiProfile => _s.Settings.PresetFor(Id)?.Opti ?? _s.Settings.IniOverrides;
    private IReadOnlyList<IniOverride> ReShadeProfile => _s.Settings.PresetFor(Id)?.ReShade ?? _s.Settings.ReShadeOverrides;

    public ObservableCollection<DlssChoice> DlssChoices { get; } = [];
    public bool HasStreamline => Info.Streamline.Count > 0;

    /// <summary>Tag to install: this game's pin, else the global pin, else null (latest).</summary>
    public string? EffectiveDlssTag => DlssChoice?.Tag ?? _s.Settings.DlssTag;

    public bool HasDlss => Info.Dlss.Count > 0;

    /// <summary>What the game ships, plus DLSS SR when OptiScaler (which can run DLSS in any game) is installed.</summary>
    public GameFeatures Features => Info.Features | (IsOptiInstalled && HasDlssForOpti ? GameFeatures.SR : GameFeatures.None);
    private bool HasDlssForOpti => Manifest?.Files.Any(f => Path.GetFileName(f).Equals("nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase)) == true;

    /// <summary>Short list for the game list / header, e.g. "RR · FG · Reflex" (SR is implied by the DLSS badge).</summary>
    public string FeatureBadge => string.Join(" · ", new[]
    {
        Info.Features.HasFlag(GameFeatures.RR) ? "RR" : null,
        Info.Features.HasFlag(GameFeatures.FG) ? Info.FgVersion is { Major: >= 310 } ? "FG/MFG" : "FG" : null,
        Info.Features.HasFlag(GameFeatures.Reflex) ? "Reflex" : null,
    }.OfType<string>());
    public bool HasFeatureBadge => FeatureBadge.Length > 0;
    public bool HasAntiCheat => Info.AntiCheat is not null;
    public string? AntiCheat => Info.AntiCheat;
    public bool IsInstalled => Manifest is not null || Info.Installs.Count > 0;
    public bool IsOptiInstalled => Manifest?.Opti == true;
    public bool IsReShadeOnlyInstalled => Manifest is { Mode: InstallMode.ReShadeOnly, ReShade: true };
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

    public bool DlssCurrent => LatestDlss is { } l && Info.Dlss.All(d => d.Version is { } v && (DlssPinned ? Trim(v) == l : Trim(v) >= l));

    [ObservableProperty] private bool _needsUpdate;

    public string ActionText => Manifest is null ? "Install" : NeedsUpdate ? "Update" : "Reinstall";
    public string Subtitle => Info.Exes.Count == 0 ? "No executable found" : Path.GetFileName(Info.Exes[0]);

    private Version? LatestDlss => (_s.Store.DlssFor(EffectiveDlssTag)?.Version) is { } v ? Trim(v) : null;
    private bool DlssPinned => EffectiveDlssTag is not null;
    private static Version Trim(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    public void Load(GameInfo info)
    {
        if (Driver is not null && Driver.Features != Features) Driver = null; // rebuilt with the new scan when the tab opens
        Info = info;
        _loading = true;
        var over = _s.Settings.Games.GetValueOrDefault(Id);

        Targets.Clear();
        foreach (var t in BuildTargets(info, over?.TargetDir)) Targets.Add(t);
        var preferred = info.Installs.FirstOrDefault() ?? over?.TargetDir;
        SelectedTarget = Targets.FirstOrDefault(t => preferred is not null && t.Dir.Equals(preferred, StringComparison.OrdinalIgnoreCase))
                         ?? Targets.FirstOrDefault();

        RebuildDlssChoices();
        RebuildPresetChoices();
        _loading = false;
        LoadManifest();
        OnPropertyChanged(string.Empty);
    }

    /// <summary>"Default" plus every DLSS SDK release; keeps the game's pin selected.</summary>
    public void RebuildDlssChoices()
    {
        var wasLoading = _loading;
        _loading = true;
        var pinned = _s.Settings.Games.GetValueOrDefault(Id)?.DlssTag;
        DlssChoices.Clear();
        var global = _s.Settings.DlssTag ?? $"latest{(_s.Store.Dlss is { } l ? " · " + l.Tag : "")}";
        DlssChoices.Add(new DlssChoice(null, $"Default ({global})"));
        foreach (var r in _s.Store.DlssReleases) DlssChoices.Add(new DlssChoice(r.Tag, r.Tag));
        if (pinned is not null && DlssChoices.All(c => c.Tag != pinned)) DlssChoices.Add(new DlssChoice(pinned, pinned));
        DlssChoice = DlssChoices.FirstOrDefault(c => c.Tag == pinned) ?? DlssChoices[0];
        _loading = wasLoading;
    }

    partial void OnDlssChoiceChanged(DlssChoice? value)
    {
        if (_loading) return;
        _s.Settings.For(Id).DlssTag = value?.Tag;
        _s.Settings.Save();
        RefreshStatus();
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
        var over = _s.Settings.Games.GetValueOrDefault(Id);
        Mode = Manifest is { } im && (im.Opti || im.ReShade) ? im.Mode : over?.Mode ?? _s.Settings.DefaultMode;
        Proxy = Manifest?.Proxy ?? _s.Settings.Games.GetValueOrDefault(Id)?.Proxy ?? ExistingOptiProxy() ?? _s.Settings.DefaultProxy;

        var m = Manifest;
        Set("opti", m?.Opti ?? true);
        Set("reshade", m?.ReShade ?? _s.Settings.InstallReShade);
        Set("mfg", m?.Mfg ?? (AllowUnofficial && _s.Settings.InstallMfgUnlock));
        Set("dlssnr", m?.DlssNr ?? (AllowUnofficial && _s.Settings.InstallDlssNr));
        Set("dlss", m?.Dlss ?? HasDlss); // nothing to replace in games that don't ship DLSS
        Set("streamline", m?.Streamline ?? (_s.Settings.InstallStreamline && HasStreamline));
        ApplyModeToRows();
        if (!ProxyNames.Contains(Proxy, StringComparer.OrdinalIgnoreCase)) Proxy = "dxgi.dll";
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
        Row("reshade", m?.ReShade == true ? InstalledVersion(ReShadeInstalledName(m)) : null, ReShadeLatest(), ReShadeCurrent(m));
        Row("mfg", m?.Mfg == true ? m.MfgTag : null, store.Mfg?.Tag, true);
        Row("dlssnr", m?.DlssNr == true ? InstalledVersion(ComponentStore.DlssNrFile) : null, File.Exists(store.DlssNrPath) ? DlssNrLabel() : null,
            m?.DlssNr != true || m.DlssNrSha == _s.DlssNrSha);
        foreach (var c in Components.Where(c => IsGated(c.Key)))
        {
            c.Latest = "opted out";
            c.State = RowState.None;
        }

        var latest = LatestDlss;
        DlssRows.Clear();
        foreach (var d in Info.Dlss)
        {
            var rel = Path.GetRelativePath(Root, d.Path);
            var ok = d.Version is { } v && latest is not null && (DlssPinned ? Trim(v) == latest : Trim(v) >= latest);
            var state = d.Version is null || latest is null ? RowState.None : ok ? RowState.Current : RowState.Update;
            DlssRows.Add(new DlssRow(rel, d.Kind, FileUtil.Format(d.Version), latest is null ? "—" : FileUtil.Format(latest), state));
        }
        var slLatest = store.Streamline?.Version;
        var interposer = Info.Streamline.FirstOrDefault(d => d.Kind == "SL");
        if (interposer is not null)
        {
            var extra = Info.Streamline.Count - 1;
            var rel = Path.GetRelativePath(Root, interposer.Path) + (extra > 0 ? $"  +{extra} sl.*.dll" : "");
            var cur = interposer.Version is { } v ? Trim(v) : null;
            var state = cur is null || slLatest is null ? RowState.None : cur == Trim(slLatest) ? RowState.Current : RowState.Update;
            DlssRows.Add(new DlssRow(rel, "SL", FileUtil.Format(cur), slLatest is null ? "—" : FileUtil.Format(slLatest), state));
        }

        var dlssRow = Components.First(c => c.Key == "dlss");
        dlssRow.Installed = m?.Dlss == true ? m.DlssTag ?? "—" : HasDlss ? "game" : "—";
        dlssRow.Latest = _s.Store.DlssFor(EffectiveDlssTag)?.Tag ?? "—";
        dlssRow.State = !HasDlss ? RowState.Missing : DlssCurrent ? RowState.Current : RowState.Update;

        var slRow = Components.First(c => c.Key == "streamline");
        slRow.Installed = interposer?.Version is { } iv ? FileUtil.Format(Trim(iv)) : HasStreamline ? "game" : "not used";
        slRow.Latest = store.Streamline?.Tag ?? "—";
        slRow.State = !HasStreamline ? RowState.None
            : interposer?.Version is { } sv && slLatest is not null && Trim(sv) == Trim(slLatest) ? RowState.Current
            : m?.Streamline == true ? RowState.Update : RowState.None;

        NeedsUpdate = m is not null && Components.Where(c => c.Key != "dlss" || m.Dlss).Any(c => c.State == RowState.Update && IsTracked(m, c.Key));
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(IsOptiInstalled));
        OnPropertyChanged(nameof(IsReShadeOnlyInstalled));
        OnPropertyChanged(nameof(DlssCurrent));
    }

    private static bool IsTracked(InstallManifest m, string key) => key switch
    {
        "opti" => m.Opti,
        "reshade" => m.ReShade,
        "mfg" => m.Mfg,
        "dlssnr" => m.DlssNr,
        "streamline" => m.Streamline,
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

    /// <summary>Version installs would use: the imported dll, the downloaded one, or the reshade.me release not downloaded yet.</summary>
    private string? ReShadeLatest()
    {
        var store = _s.Store;
        if (store.CurrentReShadePath is { } path) return FileUtil.Format(Trim3(FileUtil.ReadVersion(path)));
        return _s.Settings.AutoDownloadReShade && store.ReShade is { } r ? r.Tag : null;
    }

    private bool ReShadeCurrent(InstallManifest? m)
    {
        if (m?.ReShade != true) return true;
        if (_s.Store.ReShadeImported || _s.Store.CurrentReShadePath is not null) return m.ReShadeSha == _s.ReShadeSha;
        // Auto download not fetched yet: compare the installed file with the release version.
        var installed = SelectedTarget is null ? null : FileUtil.ReadVersion(Path.Combine(SelectedTarget.Dir, ReShadeInstalledName(m)));
        return installed is null || _s.Store.ReShade?.Version is not { } latest || Trim(installed) >= Trim(latest);
    }

    private static Version? Trim3(Version? v) => v is null ? null : Trim(v);

    /// <summary>ReShade64.dll beside OptiScaler, or the proxy name when ReShade is the proxy.</summary>
    private static string ReShadeInstalledName(InstallManifest m) =>
        m.Mode == InstallMode.ReShadeOnly && m.Proxy is { } p ? p : ComponentStore.ReShadeFile;

    private string DlssNrLabel()
    {
        var v = FileUtil.Format(FileUtil.ReadVersion(_s.Store.DlssNrPath));
        return _s.DlssNrSignature == SignatureStatus.NvidiaSigned ? v : $"{v} (unverified)";
    }

    private string? InstalledVersion(string file) =>
        SelectedTarget is null ? null : FileUtil.Format(FileUtil.ReadVersion(Path.Combine(SelectedTarget.Dir, file)));

    public InstallOptions BuildOptions(bool dlssOnly, bool acConfirmed) => new()
    {
        Mode = Mode,
        Opti = !dlssOnly && !IsReShadeOnly && On("opti"),
        ReShade = !dlssOnly && (IsReShadeOnly || On("reshade")),
        Mfg = !dlssOnly && AllowUnofficial && On("mfg"),
        DlssNr = !dlssOnly && AllowUnofficial && !IsReShadeOnly && On("dlssnr"),
        KeepDlssNr = !AllowUnofficial && Manifest?.DlssNr == true,
        Dlss = dlssOnly || On("dlss"),
        AddMissingDlss = _s.Settings.AddMissingDlss && !dlssOnly && !IsReShadeOnly && On("opti"),
        DlssTag = EffectiveDlssTag,
        Streamline = On("streamline") && HasStreamline,
        ReShadeOverrides = ReShadeProfile,
        CarryOverIni = _s.Settings.IniMode != "fresh",
        OverwriteIni = _s.Settings.IniMode == "apply",
        Proxy = Proxy,
        Overrides = OptiProfile,
        AntiCheatConfirmed = acConfirmed,
    };

    /// <summary>Options that reproduce what is installed, for "Update all".</summary>
    public InstallOptions UpdateOptions(InstallManifest m) => new()
    {
        Mode = m.Mode,
        Opti = m.Opti,
        ReShade = m.ReShade,
        Mfg = AllowUnofficial && m.Mfg,
        DlssNr = AllowUnofficial && m.DlssNr,
        KeepDlssNr = !AllowUnofficial && m.DlssNr,
        Dlss = m.Dlss,
        AddMissingDlss = m.Opti && m.Files.Any(f => Path.GetFileName(f).Equals("nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase)),
        DlssTag = EffectiveDlssTag,
        Streamline = m.Streamline && HasStreamline,
        ReShadeOverrides = ReShadeProfile,
        CarryOverIni = _s.Settings.IniMode != "fresh",
        OverwriteIni = _s.Settings.IniMode == "apply",
        Proxy = m.Proxy ?? Proxy,
        Overrides = OptiProfile,
        AntiCheatConfirmed = m.AntiCheatConfirmed,
    };

    private bool On(string key) => Components.First(c => c.Key == key).Enabled;

    public bool NeedsInjection(InstallOptions o) => o.Opti || o.ReShade || o.Mfg || o.DlssNr;
}
