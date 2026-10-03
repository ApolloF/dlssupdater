using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using DLSSUpdater.Core;
using DLSSUpdater.ViewModels;
using DLSSUpdater.Views;

namespace DLSSUpdater.Tests;

/// <summary>What's new behind the component chips: offline fallback, seen/announce bookkeeping and keyboard access.</summary>
public class ReleaseNewsTests : IDisposable
{
    private const string DlssReleasesUrl = "https://api.github.com/repos/NVIDIA/DLSS/releases?per_page=40";
    private const string DlssNotes = "- Added DLSS Ray Reconstruction Transformer Mode (Preset F)";
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-news-" + Guid.NewGuid().ToString("N")[..8]);

    public ReleaseNewsTests()
    {
        AppPaths.Root = _tmp;
        AppPaths.Ensure();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch (IOException) { }
    }

    private static void WriteApiCache(string url, string tag, string? body)
    {
        var release = new GhRelease { TagName = tag, Body = body };
        var cache = new Dictionary<string, ApiCacheEntry>
        {
            [url] = new() { ETag = "\"x\"", Body = JsonSerializer.Serialize(new List<GhRelease> { release }, JsonCtx.Default.ListGhRelease) },
        };
        File.WriteAllText(AppPaths.ApiCacheFile, JsonSerializer.Serialize(cache, JsonCtx.Default.DictionaryStringApiCacheEntry));
    }

    private static void MarkDownloaded(Component c, string tag)
    {
        Directory.CreateDirectory(ComponentStore.TagDir(c, tag));
        ComponentStore.MarkComplete(c, new ReleaseInfo { Tag = tag });
    }

    [Fact]
    public void CachedReleases_ReadsTheEtagCacheWithoutARequest()
    {
        WriteApiCache(DlssReleasesUrl, "v310.9.1", DlssNotes);

        var releases = new GitHubClient().CachedReleases(ComponentStore.DlssRepo);

        Assert.Equal(DlssNotes, Assert.Single(releases).Body);
        Assert.Empty(new GitHubClient().CachedReleases(ComponentStore.OptiRepo));
    }

    [Fact]
    public void CachedReleases_CorruptEntryIsSkipped()
    {
        File.WriteAllText(AppPaths.ApiCacheFile, JsonSerializer.Serialize(
            new Dictionary<string, ApiCacheEntry> { [DlssReleasesUrl] = new() { ETag = "\"x\"", Body = "{not json" } },
            JsonCtx.Default.DictionaryStringApiCacheEntry));

        Assert.Empty(new GitHubClient().CachedReleases(ComponentStore.DlssRepo));
    }

    [Fact]
    public async Task Offline_DownloadedReleaseKeepsItsNotesFromTheApiCache()
    {
        WriteApiCache(DlssReleasesUrl, "v310.9.1", DlssNotes);
        MarkDownloaded(Component.Dlss, "v310.9.1");
        MarkDownloaded(Component.Streamline, "v2.12.0");
        var store = new ComponentStore(new GitHubClient(), () => false);

        // A cancelled refresh fails every request before it is sent, like having no network.
        await store.RefreshAsync(new CancellationToken(true));

        Assert.True(store.Dlss!.FromCache);
        Assert.Equal(DlssNotes, store.Dlss.Notes);
        Assert.True(store.Streamline!.FromCache);
        Assert.Null(store.Streamline.Notes);
    }

    [Fact]
    public void Load_FirstSightingIsSilent_NewReleaseIsAnnouncedOnce()
    {
        var settings = new AppSettings();
        var logged = new List<string>();
        void OnLine(string line) => logged.Add(line);
        Log.Line += OnLine;
        try
        {
            new ReleaseNewsViewModel(Component.Dlss, "DLSS", settings).Load(new ReleaseInfo { Tag = "v310.7.0" });
            Assert.Empty(logged);

            var news = new ReleaseNewsViewModel(Component.Dlss, "DLSS", settings);
            news.Load(new ReleaseInfo { Tag = "v310.9.1", Notes = DlssNotes });
            Assert.True(news.IsNew);
            Assert.Contains(logged, l => l.Contains("New DLSS v310.9.1: Added DLSS Ray Reconstruction"));

            // Next start: still unread, but not logged again.
            var nextStart = new ReleaseNewsViewModel(Component.Dlss, "DLSS", settings);
            nextStart.Load(new ReleaseInfo { Tag = "v310.9.1", Notes = DlssNotes });
            Assert.True(nextStart.IsNew);
            Assert.Single(logged);

            nextStart.IsOpen = true;
            nextStart.IsOpen = false;
            Assert.False(nextStart.IsNew);
            Assert.Equal("v310.9.1", settings.SeenReleases["Dlss"]);
            Assert.Equal("v310.9.1", AppSettings.Load().SeenReleases["Dlss"]);
        }
        finally { Log.Line -= OnLine; }
    }

    [Fact]
    public void Load_OfflineReleaseIsNeverFlaggedOrRecorded()
    {
        var settings = new AppSettings { SeenReleases = { ["MfgUnlock"] = "nightly-b" } };
        var news = new ReleaseNewsViewModel(Component.MfgUnlock, "MFG Unlock", settings);

        news.Load(new ReleaseInfo { Tag = "nightly-a", FromCache = true });
        news.IsOpen = true;
        news.IsOpen = false;

        Assert.False(news.IsNew);
        Assert.Equal("nightly-b", settings.SeenReleases["MfgUnlock"]);
        Assert.Empty(settings.AnnouncedReleases);
    }

    [Fact]
    public void PopupKeyboard_FocusesContentOnOpen_EscapeCloses()
    {
        RunOnSta(() =>
        {
            var owner = new Button { Content = "chip" };
            var inside = new Button { Content = "Full release notes" };
            var popup = new Popup { PlacementTarget = owner, Child = new StackPanel { Children = { inside } } };
            PopupKeyboard.SetEnabled(popup, true);
            var window = new Window
            {
                Content = new StackPanel { Children = { owner, popup } },
                Width = 200, Height = 100, Left = -10000, Top = -10000, ShowInTaskbar = false,
            };
            window.Show();
            window.Activate();
            Pump();

            popup.IsOpen = true;
            Pump();
            Assert.True(inside.IsKeyboardFocused);

            var source = PresentationSource.FromVisual(inside)!;
            inside.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.False(popup.IsOpen);
            Assert.True(owner.IsKeyboardFocused);
            window.Close();
        });
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void RunOnSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException($"Failed on the UI thread: {failure}");
    }
}
