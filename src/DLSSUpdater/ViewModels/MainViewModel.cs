using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;
using DLSSUpdater.Views;
using Microsoft.Win32;

namespace DLSSUpdater.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly Dispatcher _ui = Application.Current.Dispatcher;
    private CancellationTokenSource _cts = new();

    public MainViewModel()
    {
        AppPaths.Ensure();
        Log.TrimFile();
        var settings = AppSettings.Load();
        var gh = new GitHubClient { Token = settings.GitHubToken };
        var store = new ComponentStore(gh, () => settings.IncludePrereleases);
        S = new Services { Settings = settings, Store = store, Installer = new Installer(store) };
        Gh = gh;
        SettingsVm = new SettingsViewModel(this);

        GamesView = CollectionViewSource.GetDefaultView(Games);
        GamesView.Filter = o => o is GameViewModel g && Matches(g);
        GamesView.SortDescriptions.Add(new SortDescription(nameof(GameViewModel.HasDlss), ListSortDirection.Descending));
        GamesView.SortDescriptions.Add(new SortDescription(nameof(GameViewModel.Name), ListSortDirection.Ascending));

        Log.Line += line => _ui.BeginInvoke(() =>
        {
            LogLines.Add(line);
            if (LogLines.Count > 400) LogLines.RemoveAt(0);
            LastLog = line;
        });
    }

    public Services S { get; }
    public GitHubClient Gh { get; }
    public SettingsViewModel SettingsVm { get; }
    public ObservableCollection<GameViewModel> Games { get; } = [];
    public ICollectionView GamesView { get; }
    public ObservableCollection<string> LogLines { get; } = [];

    [ObservableProperty] private GameViewModel? _selectedGame;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _filter = "All";
    [ObservableProperty] private string _lastLog = "";
    [ObservableProperty] private string _status = "Ready";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _progressIndeterminate;
    [ObservableProperty] private bool _progressVisible;
    [ObservableProperty] private bool _settingsOpen;
    [ObservableProperty] private bool _logOpen;
    [ObservableProperty] private bool _aboutOpen;
    [ObservableProperty] private string _streamlineVersion = "—";
    /// <summary>Guide entry to scroll to when the About page opens from an ⓘ button.</summary>
    [ObservableProperty] private string? _helpTarget;

    /// <summary>Guide on the About page: every help topic plus the choices of the option it belongs to.</summary>
    public IReadOnlyList<GuideEntry> Guide => _guide ??= HelpTopics.All
        .Select(t => new GuideEntry(t, SettingsVm.AllOptions.Count(o => o.Topic == t.Id) == 1
                                       && SettingsVm.AllOptions.First(o => o.Topic == t.Id) is { IsToggle: false } opt
            ? opt.Choices.Where(c => c.Description is not null && c.Value is not null).ToList()
            : t.Id == "ini-mode" ? SettingsOptions.IniModes.ToList() : []))
        .ToList();
    private List<GuideEntry>? _guide;

    public string AppVersion => "v" + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdates))]
    private int _updateCount;

    public bool HasUpdates => UpdateCount > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(DlssOnlyCommand), nameof(UninstallCommand),
        nameof(RestoreDlssCommand), nameof(UpdateAllCommand), nameof(CheckUpdatesCommand), nameof(RescanCommand))]
    private bool _isBusy;

    // component chips
    [ObservableProperty] private string _optiVersion = "—";
    [ObservableProperty] private string _dlssVersion = "—";
    [ObservableProperty] private string _mfgVersion = "—";
    [ObservableProperty] private string _dlssNrText = "missing";
    [ObservableProperty] private string _reShadeText = "missing";
    [ObservableProperty] private bool _dlssNrOk;
    [ObservableProperty] private bool _reShadeOk;
    [ObservableProperty] private bool _online = true;

    partial void OnSearchChanged(string value) => GamesView.Refresh();

    partial void OnSettingsOpenChanged(bool value)
    {
        if (value)
        {
            AboutOpen = false;
            SettingsVm.Reload();
            return;
        }
        SettingsVm.Commit();
        foreach (var g in Games)
        {
            g.RebuildDlssChoices();
            g.RefreshStatus();
        }
        CountUpdates();
    }

    partial void OnAboutOpenChanged(bool value)
    {
        if (!value) HelpTarget = null;
        if (value && SettingsOpen) SettingsOpen = false;
    }
    partial void OnFilterChanged(string value) => GamesView.Refresh();

    private bool Matches(GameViewModel g)
    {
        if (Search.Length > 0 && !g.Name.Contains(Search, StringComparison.OrdinalIgnoreCase)) return false;
        return Filter switch
        {
            "DLSS" => g.HasDlss,
            "Installed" => g.IsInstalled,
            "Updates" => g.NeedsUpdate,
            _ => true,
        };
    }

    // ---------- startup ----------

    public async Task InitializeAsync()
    {
        S.Store.AutoImport();
        await RefreshLocalComponentsAsync();

        var cached = GameScanner.LoadCache();
        if (cached.Count > 0) ApplyScan(cached);

        await Run("Starting", () => Task.WhenAll(CheckUpdatesCore(), ScanCore()));
        if (UpdateCount > 0) Log.Info($"{UpdateCount} game(s) have updates available");
    }

    public async Task RefreshLocalComponentsAsync()
    {
        var store = S.Store;
        var (nr, rs) = await Task.Run(() => (
            File.Exists(store.DlssNrPath) ? HashCache.Get(store.DlssNrPath) : null,
            File.Exists(store.ReShadePath) ? HashCache.Get(store.ReShadePath) : null));
        S.DlssNrSha = nr;
        S.ReShadeSha = rs;

        DlssNrOk = nr is not null;
        DlssNrText = nr is null ? "missing"
            : FileUtil.Format(FileUtil.ReadVersion(store.DlssNrPath)) +
              (ComponentStore.KnownDlssNr.TryGetValue(nr, out var variant) ? $" · {variant.Split(" · ")[0]}" : " · unverified");
        ReShadeOk = rs is not null;
        ReShadeText = rs is null ? "missing" : FileUtil.Format(FileUtil.ReadVersion(store.ReShadePath));
        foreach (var g in Games) g.RefreshStatus();
        CountUpdates();
    }

    // ---------- commands: global ----------

    private bool CanRun() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task CheckUpdates() => Run("Checking for updates", CheckUpdatesCore);

    private async Task CheckUpdatesCore()
    {
        Gh.Token = S.Settings.GitHubToken;
        await S.Store.RefreshAsync(_cts.Token);
        var s = S.Store;
        Online = s.Opti is { FromCache: false } || s.Dlss is { FromCache: false } || s.Mfg is { FromCache: false };
        OptiVersion = s.Opti is null ? "—" : s.Opti.Tag + (s.Opti.Prerelease ? " pre" : "");
        DlssVersion = s.Dlss?.Tag.TrimStart('v') ?? "—";
        MfgVersion = s.Mfg?.Tag ?? (File.Exists(Path.Combine(AppPaths.Components, ComponentStore.MfgFile)) ? "local" : "—");
        StreamlineVersion = s.Streamline?.Tag.TrimStart('v') ?? "—";
        foreach (var g in Games)
        {
            g.RebuildDlssChoices();
            g.RefreshStatus();
        }
        CountUpdates();
        Log.Info($"Latest: OptiScaler-NR {OptiVersion} · DLSS {DlssVersion} · MFG Unlock {MfgVersion}{(Online ? "" : " (offline)")}");
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task Rescan() => Run("Scanning games", ScanCore);

    private async Task ScanCore()
    {
        var progress = new Progress<int>(p => Status = $"Scanning games  {p}%");
        var games = await GameScanner.ScanAsync(S.Settings, progress, _cts.Token);
        ApplyScan(games);
        Log.Info($"Found {games.Count} games, {games.Count(g => g.Dlss.Count > 0)} with DLSS");
    }

    private void ApplyScan(List<GameInfo> games)
    {
        var selectedId = SelectedGame?.Id;
        var byId = Games.ToDictionary(g => g.Id);
        var ids = games.Select(g => g.Id).ToHashSet();

        foreach (var stale in Games.Where(g => !ids.Contains(g.Id)).ToList()) Games.Remove(stale);
        foreach (var info in games)
        {
            if (byId.TryGetValue(info.Id, out var vm)) vm.Load(info);
            else Games.Add(new GameViewModel(info, S));
        }
        GamesView.Refresh();
        CountUpdates();
        SelectedGame = Games.FirstOrDefault(g => g.Id == selectedId) ?? GamesView.Cast<GameViewModel>().FirstOrDefault();
    }

    private void CountUpdates() => UpdateCount = Games.Count(g => g.NeedsUpdate);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task UpdateAll() => Run("Updating", async () =>
    {
        var targets = Games.Where(g => g.NeedsUpdate && g.Manifest is not null).ToList();
        if (targets.Count == 0)
        {
            Log.Info("Everything is up to date");
            return;
        }
        var failed = 0;
        foreach (var g in targets)
        {
            var m = g.Manifest!;
            var o = g.UpdateOptions(m);
            if (g.HasAntiCheat && g.NeedsInjection(o) && !m.AntiCheatConfirmed)
            {
                Log.Info($"{g.Name}: skipped (anti-cheat, not confirmed)");
                continue;
            }
            try
            {
                Log.Info($"Updating {g.Name}");
                await S.Installer.InstallAsync(g.Info, g.SelectedTarget!.Dir, o, TransferProgress(), _cts.Token);
                await RescanOne(g);
            }
            catch (NeedsAdminException ex) { failed++; Log.Error($"{g.Name}: {ex.Message}"); }
            catch (Exception ex) when (ex is not OperationCanceledException) { failed++; Log.Error(g.Name, ex); }
        }
        Log.Info(failed == 0 ? $"Updated {targets.Count} game(s)" : $"Updated with {failed} failure(s), see log");
    });

    [RelayCommand]
    private async Task AddGameFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Select a game folder" };
        if (dlg.ShowDialog() != true) return;
        var path = FileUtil.Normalize(dlg.FolderName);
        if (!S.Settings.ManualGames.Contains(path, StringComparer.OrdinalIgnoreCase)) S.Settings.ManualGames.Add(path);
        S.Settings.HiddenGames.RemoveAll(h => h.Equals(GameInfo.MakeId(path), StringComparison.OrdinalIgnoreCase));
        S.Settings.Save();

        var existing = Games.FirstOrDefault(g => g.Id == GameInfo.MakeId(path));
        if (existing is not null)
        {
            SelectedGame = existing;
            return;
        }
        var info = await Task.Run(() => GameScanner.Inspect(new GameEntry(Path.GetFileName(path), path, "Manual")));
        var vm = new GameViewModel(info, S);
        Games.Add(vm);
        SaveGameCache();
        Filter = "All";
        SelectedGame = vm;
        Log.Info($"Added {info.Name}");
    }

    [RelayCommand]
    private async Task AddLibraryRoot()
    {
        var dlg = new OpenFolderDialog { Title = "Select a folder that contains game folders" };
        if (dlg.ShowDialog() != true) return;
        var path = FileUtil.Normalize(dlg.FolderName);
        if (!S.Settings.LibraryRoots.Contains(path, StringComparer.OrdinalIgnoreCase)) S.Settings.LibraryRoots.Add(path);
        S.Settings.Save();
        await Run("Scanning games", ScanCore);
    }

    [RelayCommand]
    private void ToggleSettings() => SettingsOpen = !SettingsOpen;

    [RelayCommand]
    private void ToggleLog() => LogOpen = !LogOpen;

    [RelayCommand]
    private void ToggleAbout() => AboutOpen = !AboutOpen;

    [RelayCommand]
    private void ShowHelp(string topic)
    {
        AboutOpen = true;
        HelpTarget = null;
        HelpTarget = topic;
    }

    [RelayCommand]
    private static void OpenUrl(string url)
    {
        if (url.StartsWith("https://", StringComparison.Ordinal))
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();
    }

    // ---------- commands: selected game ----------

    private bool CanRunOnGame() => !IsBusy && SelectedGame is not null;
    private bool CanRunOnInstalled() => !IsBusy && SelectedGame?.Manifest is not null;

    partial void OnSelectedGameChanged(GameViewModel? value)
    {
        InstallCommand.NotifyCanExecuteChanged();
        DlssOnlyCommand.NotifyCanExecuteChanged();
        UninstallCommand.NotifyCanExecuteChanged();
        RestoreDlssCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRunOnGame))]
    private Task Install() => InstallSelected(dlssOnly: false);

    [RelayCommand(CanExecute = nameof(CanRunOnGame))]
    private Task DlssOnly() => InstallSelected(dlssOnly: true);

    private async Task InstallSelected(bool dlssOnly)
    {
        var g = SelectedGame;
        if (g?.SelectedTarget is null) return;
        var o = g.BuildOptions(dlssOnly, acConfirmed: g.Manifest?.AntiCheatConfirmed == true);

        if (g.HasAntiCheat && g.NeedsInjection(o) && !o.AntiCheatConfirmed)
        {
            var ok = Dialog.Confirm($"{g.AntiCheat} detected",
                $"{g.Name} ships {g.AntiCheat}. Injecting OptiScaler, ReShade or add-ons into a game protected by anti-cheat can get your account banned, " +
                "and online modes will usually refuse to start.\n\nOnly continue for offline / single-player use.",
                "Install anyway", danger: true);
            if (!ok) return;
            o = g.BuildOptions(dlssOnly, acConfirmed: true);
        }

        if (!dlssOnly && o.Opti && g.SelectedTarget.Exe is null && !g.Info.Exes.Any())
            Log.Info($"{g.Name}: no executable found in the target folder, OptiScaler must sit next to the game exe");

        await Run(dlssOnly ? $"Updating DLSS for {g.Name}" : $"Installing to {g.Name}", async () =>
        {
            await S.Installer.InstallAsync(g.Info, g.SelectedTarget.Dir, o, TransferProgress(), _cts.Token);
            await RescanOne(g);
        });
    }

    [RelayCommand(CanExecute = nameof(CanRunOnInstalled))]
    private async Task Uninstall()
    {
        var g = SelectedGame;
        if (g?.SelectedTarget is null) return;
        if (!Dialog.Confirm("Uninstall", $"Remove everything DLSS Updater installed in {g.Name} and restore the original files?", "Uninstall", danger: true))
            return;
        await Run($"Uninstalling from {g.Name}", async () =>
        {
            await S.Installer.UninstallAsync(g.Info, g.SelectedTarget.Dir, _cts.Token);
            await RescanOne(g);
        });
    }

    [RelayCommand(CanExecute = nameof(CanRunOnInstalled))]
    private Task RestoreDlss()
    {
        var g = SelectedGame;
        if (g?.SelectedTarget is null) return Task.CompletedTask;
        return Run($"Restoring DLSS for {g.Name}", async () =>
        {
            await S.Installer.RestoreDlssAsync(g.Info, g.SelectedTarget.Dir, _cts.Token);
            await RescanOne(g);
        });
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var dir = SelectedGame?.SelectedTarget?.Dir ?? SelectedGame?.Root;
        if (dir is not null && Directory.Exists(dir)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private void BrowseTarget()
    {
        var g = SelectedGame;
        if (g is null) return;
        var dlg = new OpenFolderDialog { Title = "Folder containing the game executable", InitialDirectory = g.Root };
        if (dlg.ShowDialog() == true) g.AddCustomTarget(FileUtil.Normalize(dlg.FolderName));
    }

    [RelayCommand]
    private void RemoveGame()
    {
        var g = SelectedGame;
        if (g is null) return;
        if (g.IsInstalled && !Dialog.Confirm("Remove from list", $"{g.Name} still has DLSS Updater files installed. Remove it from the list anyway?", "Remove"))
            return;
        if (S.Settings.ManualGames.RemoveAll(p => GameInfo.MakeId(p) == g.Id) == 0)
            S.Settings.HiddenGames.Add(g.Id);
        S.Settings.Save();
        Games.Remove(g);
        SaveGameCache();
        SelectedGame = GamesView.Cast<GameViewModel>().FirstOrDefault();
    }

    // ---------- helpers ----------

    private async Task RescanOne(GameViewModel g)
    {
        var info = await Task.Run(() => GameScanner.Inspect(new GameEntry(g.Name, g.Root, g.Source)));
        g.Load(info);
        GamesView.Refresh();
        CountUpdates();
        SaveGameCache();
    }

    private void SaveGameCache() => GameScanner.SaveCache(Games.Select(g => g.Info).ToList());

    private IProgress<TransferProgress> TransferProgress() => new Progress<TransferProgress>(p =>
    {
        Status = p.Text;
        ProgressIndeterminate = p.Fraction is null;
        Progress = p.Fraction ?? 0;
    });

    private async Task Run(string label, Func<Task> work)
    {
        IsBusy = true;
        Status = label;
        ProgressVisible = true;
        ProgressIndeterminate = true;
        try
        {
            await work();
            Status = "Ready";
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled";
            Log.Info("Cancelled");
        }
        catch (NeedsAdminException ex)
        {
            Status = "Needs administrator rights";
            Log.Error(ex.Message);
            if (Dialog.Confirm("Administrator rights needed", ex.Message, "Restart as admin")) App.RestartElevated();
        }
        catch (Exception ex)
        {
            Status = "Failed";
            Log.Error(label, ex);
            Dialog.Show("Something went wrong", ex.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressVisible = false;
            ProgressIndeterminate = false;
            Progress = 0;
        }
    }
}
