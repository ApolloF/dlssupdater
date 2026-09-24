using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;
using Microsoft.Win32;

namespace DLSSUpdater.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private AppSettings Settings => _main.S.Settings;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        Overrides = new ObservableCollection<IniOverride>(Settings.IniOverrides);
        ReShadeOverrides = new ObservableCollection<IniOverride>(Settings.ReShadeOverrides);
        Folders = new ObservableCollection<FolderEntry>();

        OptiKeybinds = new(KeybindDef.Opti.Select(d => new KeybindViewModel(d, Overrides)));
        ReShadeKeybinds = new(KeybindDef.ReShadeKeys.Select(d => new KeybindViewModel(d, ReShadeOverrides)));

        ReShadeOptions =
        [
            IniOptionViewModel.Toggle("Skip the tutorial", "Start without ReShade's first-run guide.", "OVERLAY", "TutorialProgress", "4", ReShadeOverrides).About("skip-tutorial"),
            IniOptionViewModel.Toggle("Performance mode", "Compile effects without UI variables for more FPS; tweaking needs it off.", "GENERAL", "PerformanceMode", "1", ReShadeOverrides).About("performance-mode"),
            IniOptionViewModel.Toggle("Show FPS", "ReShade's own FPS counter.", "OVERLAY", "ShowFPS", "1", ReShadeOverrides).About("show-fps"),
            IniOptionViewModel.Toggle("Load MFG Unlock early", "Needed by games that start Streamline before ReShade loads add-ons (e.g. Cyberpunk).",
                "ADDON", "LoadFromDllMain", ComponentStore.MfgFile, ReShadeOverrides).About("load-early"),
        ];

        MfgOptions =
        [
            new IniOptionViewModel("Force frame multiplier", "Only for games with just an FG on/off switch.", "RenoDX.MFGUnlock", "ForceMultiplier", ReShadeOverrides,
                new("Game setting", null), new("2x", "2"), new("3x", "3"), new("4x", "4"), new("5x", "5"), new("6x", "6")).About("force-multiplier"),
            new IniOptionViewModel("Max frame count", "Highest multiplier reported to the game.", "RenoDX.MFGUnlock", "MaxCount", ReShadeOverrides,
                new("Default (4)", null), new("3", "3"), new("4", "4"), new("5", "5"), new("6", "6")).About("max-count"),
            IniOptionViewModel.Toggle("Dynamic MFG", "Needs DLSS 310.9.1 FG + the matching Streamline set. Overrides the forced multiplier.",
                "RenoDX.MFGUnlock", "DynamicMFG", "1", ReShadeOverrides).About("dynamic-mfg"),
            new IniOptionViewModel("Dynamic target FPS", "0 follows the display refresh rate.", "RenoDX.MFGUnlock", "DynamicTargetFPS", ReShadeOverrides,
                new("Refresh rate", null), new("60", "60"), new("90", "90"), new("120", "120"), new("144", "144"), new("165", "165"), new("240", "240")).About("dynamic-target"),
            new IniOptionViewModel("Runtime selection", "Prefer local files stops NVIDIA's OTA DLLs from overriding the ones installed here.",
                "RenoDX.MFGUnlock", "RuntimeSelectionMode", ReShadeOverrides,
                new("Game default", null), new("Prefer local files", "1"), new("Force NVIDIA OTA", "2")).About("runtime-selection"),
            new IniOptionViewModel("HDR compatibility", "Try UI Composition if HDR games show broken UI with FG.", "RenoDX.MFGUnlock", "HDRCompatibilityMode", ReShadeOverrides,
                new("Native", null), new("UI Composition", "1"), new("Auto guard + UI", "2"), new("Final color fallback", "3")).About("hdr-compat"),
        ];

        Reload();
    }

    public IReadOnlyList<string> ProxyNames => AppSettings.ProxyNames;
    public ObservableCollection<IniOverride> Overrides { get; }
    public ObservableCollection<IniOverride> ReShadeOverrides { get; }
    public ObservableCollection<FolderEntry> Folders { get; }
    public ObservableCollection<KeybindViewModel> OptiKeybinds { get; }
    public ObservableCollection<KeybindViewModel> ReShadeKeybinds { get; }
    public IReadOnlyList<IniOptionViewModel> ReShadeOptions { get; }
    public IReadOnlyList<IniOptionViewModel> MfgOptions { get; }
    public ObservableCollection<DlssChoice> DlssChoices { get; } = [];

    [ObservableProperty] private string _tab = "General";
    [ObservableProperty] private KeybindViewModel? _capturing;

    // ---------- general ----------

    public string DefaultProxy
    {
        get => Settings.DefaultProxy;
        set { Settings.DefaultProxy = value; Save(); }
    }

    public bool IncludePrereleases
    {
        get => Settings.IncludePrereleases;
        set { Settings.IncludePrereleases = value; Save(); }
    }

    public bool InstallReShade
    {
        get => Settings.InstallReShade;
        set { Settings.InstallReShade = value; Save(); }
    }

    public bool InstallMfgUnlock
    {
        get => Settings.InstallMfgUnlock;
        set { Settings.InstallMfgUnlock = value; Save(); }
    }

    public bool InstallDlssNr
    {
        get => Settings.InstallDlssNr;
        set { Settings.InstallDlssNr = value; Save(); }
    }

    public bool InstallStreamline
    {
        get => Settings.InstallStreamline;
        set { Settings.InstallStreamline = value; Save(); }
    }

    public bool AddMissingDlss
    {
        get => Settings.AddMissingDlss;
        set { Settings.AddMissingDlss = value; Save(); }
    }

    public bool CarryOverGameIni
    {
        get => Settings.CarryOverGameIni;
        set { Settings.CarryOverGameIni = value; Save(); }
    }

    public string GitHubToken
    {
        get => Settings.GitHubToken ?? "";
        set { Settings.GitHubToken = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); Save(); }
    }

    public DlssChoice? SelectedDlss
    {
        get => DlssChoices.FirstOrDefault(c => c.Tag == Settings.DlssTag);
        set
        {
            if (value is null) return;
            Settings.DlssTag = value.Tag;
            Save();
        }
    }

    public string DlssNrInfo => Describe(_main.S.Store.DlssNrPath, _main.S.DlssNrSha, true);
    public string ReShadeInfo => Describe(_main.S.Store.ReShadePath, _main.S.ReShadeSha, false);

    private static string Describe(string path, string? sha, bool known)
    {
        if (!File.Exists(path) || sha is null) return "Not imported";
        var v = FileUtil.Format(FileUtil.ReadVersion(path));
        var size = new FileInfo(path).Length / 1048576.0;
        var variant = known ? (ComponentStore.KnownDlssNr.TryGetValue(sha, out var n) ? n : "unknown build, hash not in INSTALL-DLSSNR.md") : null;
        return $"{v}  ·  {size:0.0} MB  ·  SHA-256 {sha[..12]}…" + (variant is null ? "" : $"\n{variant}");
    }

    private void Save() => Settings.Save();

    /// <summary>Writes the override lists back to settings (called when the panel closes).</summary>
    public void Commit()
    {
        Capturing = null;
        Settings.IniOverrides = Clean(Overrides);
        Settings.ReShadeOverrides = Clean(ReShadeOverrides);
        Save();

        static List<IniOverride> Clean(IEnumerable<IniOverride> list) =>
            list.Where(o => !string.IsNullOrWhiteSpace(o.Section) && !string.IsNullOrWhiteSpace(o.Key)).ToList();
    }

    // ---------- keybinds ----------

    [RelayCommand]
    private void Capture(KeybindViewModel bind)
    {
        if (Capturing is not null) Capturing.Capturing = false;
        Capturing = bind;
        bind.Capturing = true;
    }

    /// <summary>Called by the window while a bind is capturing. Returns true when the key was consumed.</summary>
    public bool HandleKey(int vk, bool ctrl, bool shift, bool alt)
    {
        if (Capturing is not { } bind) return false;
        if (vk is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C) return true; // wait for a real key
        if (vk == 0x1B) // Esc cancels
        {
            bind.Capturing = false;
            Capturing = null;
            return true;
        }
        bind.Set(bind.Def.ReShade ? new Hotkey(vk, ctrl, shift, alt) : new Hotkey(vk));
        Capturing = null;
        return true;
    }

    [RelayCommand]
    private void ResetBind(KeybindViewModel bind) { bind.Set(null); Capturing = null; }

    [RelayCommand]
    private void DisableBind(KeybindViewModel bind) { bind.Set(Hotkey.None); Capturing = null; }

    // ---------- overrides ----------

    [RelayCommand]
    private void AddOverride() => Overrides.Add(new IniOverride("", "", ""));

    [RelayCommand]
    private void AddReShadeOverride() => ReShadeOverrides.Add(new IniOverride("", "", ""));

    [RelayCommand]
    private void RemoveOverride(IniOverride o)
    {
        if (!Overrides.Remove(o)) ReShadeOverrides.Remove(o);
        RefreshEditors();
    }

    [RelayCommand]
    private void ResetOverrides()
    {
        Overrides.Clear();
        foreach (var o in ConfigProfile.Defaults()) Overrides.Add(o);
        ReShadeOverrides.Clear();
        foreach (var o in ConfigProfile.ReShadeDefaults()) ReShadeOverrides.Add(o);
        Commit();
        RefreshEditors();
    }

    private void RefreshEditors()
    {
        foreach (var k in OptiKeybinds.Concat(ReShadeKeybinds)) k.Refresh();
        foreach (var o in ReShadeOptions.Concat(MfgOptions)) o.Refresh();
    }

    // ---------- components / folders ----------

    [RelayCommand]
    private Task ImportDlssNr() => Import(ComponentStore.DlssNrFile, "nvngx_dlssnr.dll|nvngx_dlssnr.dll|DLL|*.dll");

    [RelayCommand]
    private Task ImportReShade() => Import(ComponentStore.ReShadeFile, "ReShade (add-on build)|ReShade64.dll;dxgi.dll;*.dll");

    private async Task Import(string fileName, string filter)
    {
        var dlg = new OpenFileDialog { Filter = filter, Title = $"Select {fileName}" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            await Task.Run(() => _main.S.Store.Import(dlg.FileName, fileName));
            await _main.RefreshLocalComponentsAsync();
            OnPropertyChanged(nameof(DlssNrInfo));
            OnPropertyChanged(nameof(ReShadeInfo));
        }
        catch (IOException ex) { Log.Error($"Import of {fileName} failed", ex); }
    }

    [RelayCommand]
    private void RemoveFolder(FolderEntry f)
    {
        Folders.Remove(f);
        Settings.ManualGames.RemoveAll(p => p.Equals(f.Path, StringComparison.OrdinalIgnoreCase));
        Settings.LibraryRoots.RemoveAll(p => p.Equals(f.Path, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    [RelayCommand]
    private void ShowHidden()
    {
        Settings.HiddenGames.Clear();
        Save();
        _main.RescanCommand.Execute(null);
    }

    [RelayCommand]
    private void OpenDataFolder() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Root}\"") { UseShellExecute = true });

    public void Reload()
    {
        Folders.Clear();
        foreach (var p in Settings.ManualGames) Folders.Add(new FolderEntry(p, "Game"));
        foreach (var p in Settings.LibraryRoots) Folders.Add(new FolderEntry(p, "Library"));

        DlssChoices.Clear();
        var store = _main.S.Store;
        DlssChoices.Add(new DlssChoice(null, $"Latest{(store.Dlss is { } l ? " · " + l.Tag : "")}"));
        foreach (var r in store.DlssReleases) DlssChoices.Add(new DlssChoice(r.Tag, r.Tag));
        if (Settings.DlssTag is { } t && DlssChoices.All(c => c.Tag != t)) DlssChoices.Add(new DlssChoice(t, t));

        RefreshEditors();
        OnPropertyChanged(string.Empty);
    }
}

public sealed record FolderEntry(string Path, string Kind);
