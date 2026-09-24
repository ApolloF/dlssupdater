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
        Folders = new ObservableCollection<FolderEntry>(
            Settings.ManualGames.Select(p => new FolderEntry(p, "Game"))
                .Concat(Settings.LibraryRoots.Select(p => new FolderEntry(p, "Library"))));
    }

    public IReadOnlyList<string> ProxyNames => AppSettings.ProxyNames;
    public ObservableCollection<IniOverride> Overrides { get; }
    public ObservableCollection<FolderEntry> Folders { get; }

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

    /// <summary>Writes the override grid back to settings (called when the panel closes).</summary>
    public void Commit()
    {
        Settings.IniOverrides = Overrides.Where(o => !string.IsNullOrWhiteSpace(o.Section) && !string.IsNullOrWhiteSpace(o.Key)).ToList();
        Save();
    }

    [RelayCommand]
    private void AddOverride() => Overrides.Add(new IniOverride("", "", ""));

    [RelayCommand]
    private void RemoveOverride(IniOverride o) => Overrides.Remove(o);

    [RelayCommand]
    private void ResetOverrides()
    {
        Overrides.Clear();
        foreach (var o in ConfigProfile.Defaults()) Overrides.Add(o);
        Commit();
    }

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
        OnPropertyChanged(string.Empty);
    }
}

public sealed record FolderEntry(string Path, string Kind);
