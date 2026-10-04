using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

public class DemoTests : IDisposable
{
    private readonly string _appRoot = AppPaths.Root;
    private readonly string _game = Path.Combine(Path.GetTempPath(), "dlssu-demo-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly ComponentStore _store;

    public DemoTests()
    {
        Demo.Enable();
        _store = new ComponentStore(new GitHubClient(), () => false);
        Demo.Seed(_store);
    }

    public void Dispose()
    {
        Demo.Active = false;
        AppPaths.Root = _appRoot;
        try { Directory.Delete(_game, true); } catch (IOException) { }
    }

    [Fact]
    public void Enable_uses_a_throwaway_data_folder()
    {
        Assert.StartsWith(Path.GetTempPath(), AppPaths.Root, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("DLSSUpdater-demo", AppPaths.Root);
    }

    [Fact]
    public void Library_has_games_to_update()
    {
        var outdated = Demo.Games.Where(g => g.Installs.Count > 0 && g.Dlss.Any(d => d.Version < new Version(310, 10, 0))).ToList();
        Assert.Equal(["Ember Crown", "Iron Veil", "Wicker & Ash"], outdated.Select(g => g.Name));
        Assert.All(Demo.Games, g => Assert.StartsWith(@"D:\Games\", g.Root));
    }

    [Fact]
    public async Task Install_and_uninstall_never_touch_the_game_folder()
    {
        var dll = Path.Combine(_game, "nvngx_dlss.dll");
        Directory.CreateDirectory(_game);
        File.WriteAllText(dll, "ORIGINAL");
        var game = new GameInfo
        {
            Id = GameInfo.MakeId(_game), Name = "Real game", Root = _game,
            Dlss = [new DlssDll { Path = dll, Name = "nvngx_dlss.dll", Version = new Version(3, 7, 10, 0) }],
        };
        var installer = new Installer(_store);

        await installer.InstallAsync(game, _game, new InstallOptions { Dlss = true, Opti = true, ReShade = true }, null, CancellationToken.None);

        Assert.Equal(new Version(310, 10, 0), game.Dlss[0].Version);
        Assert.Equal(Demo.LatestDlss, Demo.ManifestAt(_game)?.DlssTag);

        await installer.UninstallAsync(game, _game, CancellationToken.None);

        Assert.Equal(new Version(3, 7, 10, 0), game.Dlss[0].Version);
        Assert.Null(Demo.ManifestAt(_game));
        Assert.Equal(["nvngx_dlss.dll"], Directory.GetFileSystemEntries(_game).Select(Path.GetFileName));
        Assert.Equal("ORIGINAL", File.ReadAllText(dll));
    }
}
