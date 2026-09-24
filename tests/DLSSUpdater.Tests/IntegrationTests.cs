using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>
/// Downloads the real releases and installs them into a throwaway game folder.
/// Runs only with DLSSU_INTEGRATION=1; DLSSU_COMPONENTS may point at a folder with nvngx_dlssnr.dll / ReShade64.dll.
/// </summary>
public class IntegrationTests
{
    [Fact]
    public async Task RealReleases_InstallAndUninstall()
    {
        if (Environment.GetEnvironmentVariable("DLSSU_INTEGRATION") != "1") return;

        var tmp = Path.Combine(Path.GetTempPath(), "dlssu-it");
        var game = Path.Combine(tmp, "Game");
        if (Directory.Exists(game)) Directory.Delete(game, true);
        AppPaths.Root = Path.Combine(tmp, "appdata");
        AppPaths.Ensure();

        var store = new ComponentStore(new GitHubClient(), () => true);
        await store.RefreshAsync(default);
        Assert.NotNull(store.Opti);
        Assert.NotNull(store.Dlss);
        Assert.NotNull(store.Mfg);

        var components = Environment.GetEnvironmentVariable("DLSSU_COMPONENTS");
        var haveLocal = components is not null && File.Exists(Path.Combine(components, ComponentStore.DlssNrFile));
        if (haveLocal)
        {
            store.Import(Path.Combine(components!, ComponentStore.DlssNrFile), ComponentStore.DlssNrFile);
            store.Import(Path.Combine(components!, ComponentStore.ReShadeFile), ComponentStore.ReShadeFile);
        }

        var target = Path.Combine(game, @"G\Binaries\Win64");
        Directory.CreateDirectory(target);
        File.Copy(Environment.ProcessPath!, Path.Combine(target, "G-Win64-Shipping.exe"));
        var info = GameScanner.Inspect(new GameEntry("G", game, "Manual"));

        var o = new InstallOptions
        {
            Opti = true, Mfg = true, Dlss = true, AddMissingDlss = true,
            ReShade = haveLocal, DlssNr = haveLocal,
            Proxy = "dxgi.dll", Overrides = ConfigProfile.Defaults(),
        };
        var installer = new Installer(store);
        await installer.InstallAsync(info, target, o, null, default);

        Assert.True(Installer.IsOptiScaler(Path.Combine(target, "dxgi.dll")));
        var sr = FileUtil.ReadVersion(Path.Combine(target, "nvngx_dlss.dll"));
        Assert.Equal(store.Dlss!.Version!.Major, sr!.Major);
        var ini = IniFile.Load(Path.Combine(target, "OptiScaler.ini"));
        Assert.Equal("true", ini.Get("Plugins", "LoadReshade"));
        Assert.Equal("0x2e", ini.Get("Menu", "ShortcutKey"));
        Assert.Equal("true", ini.Get("DlssNr", "Enabled"));
        Assert.True(File.Exists(Path.Combine(target, ComponentStore.MfgFile)));

        await installer.UninstallAsync(GameScanner.Inspect(new GameEntry("G", game, "Manual")), target, default);
        Assert.Equal(["G-Win64-Shipping.exe"], Directory.EnumerateFileSystemEntries(target).Select(Path.GetFileName).ToArray());
    }
}
