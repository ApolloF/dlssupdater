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

        ReShadeOptions = SettingsOptions.ReShade(ReShadeOverrides);
        MfgOptions = SettingsOptions.Mfg(ReShadeOverrides);
        NrOptions = SettingsOptions.Nr(Overrides);
        TonemapOptions = SettingsOptions.Tonemap(Overrides);
        HdrOptions = SettingsOptions.HdrOutput(Overrides);

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
    public IReadOnlyList<IniOptionViewModel> NrOptions { get; }
    public IReadOnlyList<IniOptionViewModel> TonemapOptions { get; }
    public IReadOnlyList<IniOptionViewModel> HdrOptions { get; }
    public IEnumerable<IniOptionViewModel> AllOptions => ReShadeOptions.Concat(MfgOptions).Concat(NrOptions).Concat(TonemapOptions).Concat(HdrOptions);
    public IReadOnlyList<OptionChoice> IniModes => SettingsOptions.IniModes;

    public OptionChoice IniMode
    {
        get => IniModes.FirstOrDefault(m => m.Value == Settings.IniMode) ?? IniModes[0];
        set
        {
            if (value?.Value is null) return;
            Settings.IniMode = value.Value;
            Save();
        }
    }
    public ObservableCollection<DlssChoice> DlssChoices { get; } = [];

    [ObservableProperty] private string _tab = "General";

    [ObservableProperty] private string? _selectedPreset;

    public ObservableCollection<string> PresetNames { get; } = [];

    partial void OnSelectedPresetChanged(string? value)
    {
        if (value is null || _reloading) return;
        var p = Settings.Presets.FirstOrDefault(x => x.Name == value);
        if (p is null) return;
        Replace(Overrides, p.Opti);
        Replace(ReShadeOverrides, p.ReShade);
        Commit();
        RefreshEditors();
        Log.Info($"Loaded preset '{value}'");
    }

    private bool _reloading;

    private static void Replace(ObservableCollection<IniOverride> target, IEnumerable<IniOverride> source)
    {
        target.Clear();
        foreach (var o in source) target.Add(new IniOverride(o.Section, o.Key, o.Value));
    }

    private static List<IniOverride> Copy(IEnumerable<IniOverride> list) =>
        list.Where(o => !string.IsNullOrWhiteSpace(o.Section) && !string.IsNullOrWhiteSpace(o.Key))
            .Select(o => new IniOverride(o.Section.Trim(), o.Key.Trim(), o.Value.Trim())).ToList();

    [RelayCommand]
    private void SavePreset()
    {
        var name = Views.Dialog.Prompt("Save preset",
            "Saves the current OptiScaler.ini and ReShade.ini settings, keybinds and options under a name. " +
            "Presets can be loaded here or assigned to single games.", SelectedPreset ?? "My preset");
        if (name is null) return;
        Settings.Presets.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        Settings.Presets.Add(new ConfigPreset { Name = name, Opti = Copy(Overrides), ReShade = Copy(ReShadeOverrides) });
        Save();
        ReloadPresets(name);
        _main.RefreshPresets();
        Log.Info($"Saved preset '{name}'");
    }

    [RelayCommand]
    private void DeletePreset()
    {
        if (SelectedPreset is not { } name) return;
        if (!Views.Dialog.Confirm("Delete preset", $"Delete preset '{name}'? Games using it fall back to the current settings.", "Delete", danger: true)) return;
        Settings.Presets.RemoveAll(p => p.Name == name);
        foreach (var g in Settings.Games.Values.Where(g => g.Preset == name)) g.Preset = null;
        Save();
        ReloadPresets(null);
        _main.RefreshPresets();
    }

    private void ReloadPresets(string? select)
    {
        _reloading = true;
        PresetNames.Clear();
        foreach (var p in Settings.Presets) PresetNames.Add(p.Name);
        SelectedPreset = select;
        _reloading = false;
    }
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
    private void ResetAll()
    {
        if (!Views.Dialog.Confirm("Reset to defaults",
                "Reset all OptiScaler.ini and ReShade.ini settings, keybinds and options to the app defaults? Saved presets are kept.", "Reset", danger: true))
            return;
        ResetOverrides();
        _reloading = true;
        SelectedPreset = null;
        _reloading = false;
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
        foreach (var o in AllOptions) o.Refresh();
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

    // ---------- WaterLauncher ----------

    [ObservableProperty] private bool _waterLauncherConnected = Addon.AddonRegistration.Registered;

    public string WaterLauncherHint => WaterLauncherConnected
        ? "Connected. Turn DLSS Updater on in WaterLauncher: Settings, Add-ons. It then shows each game's DLSS version and puts DLSS back before a game starts when an update replaced it."
        : Addon.AddonRegistration.WaterLauncherFound
            ? "Show DLSS versions in WaterLauncher and put DLSS back automatically before a game starts."
            : "WaterLauncher isn't on this PC. It's a game launcher that can use DLSS Updater as an add-on.";

    [RelayCommand]
    private void ToggleWaterLauncher()
    {
        try
        {
            if (WaterLauncherConnected) Addon.AddonRegistration.Unregister();
            else Addon.AddonRegistration.Register();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Views.Dialog.Show("WaterLauncher", ex.Message);
        }
        WaterLauncherConnected = Addon.AddonRegistration.Registered;
        OnPropertyChanged(nameof(WaterLauncherHint));
    }

    [RelayCommand]
    private void OpenDataFolder() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Root}\"") { UseShellExecute = true });

    public void Reload()
    {
        Folders.Clear();
        foreach (var p in Settings.ManualGames) Folders.Add(new FolderEntry(p, "Game"));
        foreach (var p in Settings.LibraryRoots) Folders.Add(new FolderEntry(p, "Library"));

        ReloadPresets(SelectedPreset is { } sp && Settings.Presets.Any(p => p.Name == sp) ? sp : null);

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
