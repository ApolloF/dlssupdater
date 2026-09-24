using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

public class InstallerTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-inst-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root;
    private readonly string _target;
    private readonly ComponentStore _store;

    private const string ReleaseIni = "[Menu]\nShortcutKey=auto\n[Plugins]\nLoadReshade=auto\n[Hotfix]\nManualInputPolling=auto\n";

    public InstallerTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "appdata");
        AppPaths.Ensure();
        _root = Path.Combine(_tmp, "Game");
        _target = Path.Combine(_root, @"Game\Binaries\Win64");
        _store = new ComponentStore(new GitHubClient(), () => true);

        SeedOpti("v9.9.9", "OPTI-1");
        SeedMfg("1.0", "MFG-1");
        SeedDlss("v310.9.1", "NEW");
        Write(Path.Combine(AppPaths.Components, ComponentStore.DlssNrFile), "NR");
        Write(Path.Combine(AppPaths.Components, ComponentStore.ReShadeFile), "RESHADE");

        // A game that already has another mod's dxgi.dll, a hand-edited ini and old DLSS dlls.
        Write(Path.Combine(_target, "Game-Win64-Shipping.exe"), "EXE");
        Write(Path.Combine(_root, "Game.exe"), "BOOT");
        Write(Path.Combine(_target, "dxgi.dll"), "OTHER-MOD");
        Write(Path.Combine(_target, "OptiScaler.ini"), "[Hotfix]\nManualInputPolling=true\n");
        Write(Path.Combine(_target, "nvngx.dll_dlssnr.dll"), "LEGACY");
        Write(Path.Combine(_target, "nvngx_dlssg.dll"), "OLD-FG");
        Write(Path.Combine(_root, @"Engine\Plugins\Runtime\Nvidia\DLSS\Binaries\ThirdParty\Win64\nvngx_dlss.dll"), "OLD-SR");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch (IOException) { }
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void SeedOpti(string tag, string content)
    {
        var pkg = Path.Combine(ComponentStore.TagDir(Component.OptiScaler, tag), "pkg");
        Write(Path.Combine(pkg, "OptiScaler.dll"), content);
        Write(Path.Combine(pkg, "OptiScaler.ini"), ReleaseIni);
        Write(Path.Combine(pkg, @"OptiScaler\libxess.dll"), "XESS");
        Write(Path.Combine(pkg, @"OptiScaler\dlssnr\README.md"), "doc");
        Write(Path.Combine(pkg, @"Licenses\XeSS_LICENSE.txt"), "lic");
        Write(Path.Combine(pkg, "setup_windows.bat"), "bat");
        var r = new ReleaseInfo { Tag = tag };
        ComponentStore.MarkComplete(Component.OptiScaler, r);
        _store.Opti = r;
    }

    private void SeedMfg(string tag, string content)
    {
        Write(Path.Combine(ComponentStore.TagDir(Component.MfgUnlock, tag), ComponentStore.MfgFile), content);
        var r = new ReleaseInfo { Tag = tag };
        ComponentStore.MarkComplete(Component.MfgUnlock, r);
        _store.Mfg = r;
    }

    private void SeedDlss(string tag, string content)
    {
        foreach (var f in ComponentStore.DlssFiles)
            Write(Path.Combine(ComponentStore.TagDir(Component.Dlss, tag), f), $"{content}-{f}");
        var r = new ReleaseInfo { Tag = tag };
        ComponentStore.MarkComplete(Component.Dlss, r);
        _store.Dlss = r;
    }

    private Dictionary<string, string> Snapshot() =>
        Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(_root, f), File.ReadAllText, StringComparer.OrdinalIgnoreCase);

    private GameInfo Game() => GameScanner.Inspect(new GameEntry("Game", _root, "Manual"));

    private static InstallOptions Full => new()
    {
        Opti = true, ReShade = true, Mfg = true, DlssNr = true, Dlss = true, AddMissingDlss = true,
        Proxy = "dxgi.dll", Overrides = ConfigProfile.Defaults(), CarryOverIni = true,
    };

    private string T(string rel) => File.ReadAllText(Path.Combine(_target, rel));

    [Fact]
    public void Scan_PicksShippingDirAsTarget()
    {
        var g = Game();
        Assert.Equal(_target, Path.GetDirectoryName(g.Exes[0]));
        Assert.Equal(2, g.Dlss.Count);
    }

    [Fact]
    public async Task Install_Update_Uninstall_RoundTrip()
    {
        var before = Snapshot();
        var installer = new Installer(_store);

        await installer.InstallAsync(Game(), _target, Full, null, default);

        Assert.Equal("OPTI-1", T("dxgi.dll"));
        Assert.Equal("RESHADE", T("ReShade64.dll"));
        Assert.Equal("MFG-1", T(ComponentStore.MfgFile));
        Assert.Equal("NR", T("nvngx_dlssnr.dll"));
        Assert.Equal("XESS", T(@"OptiScaler\libxess.dll"));
        Assert.False(File.Exists(Path.Combine(_target, @"OptiScaler\dlssnr\README.md")));
        Assert.False(File.Exists(Path.Combine(_target, "setup_windows.bat")));
        Assert.False(File.Exists(Path.Combine(_target, "nvngx.dll_dlssnr.dll")));
        Assert.Equal("NEW-nvngx_dlssg.dll", T("nvngx_dlssg.dll"));
        Assert.Equal("NEW-nvngx_dlss.dll", File.ReadAllText(Path.Combine(_root, @"Engine\Plugins\Runtime\Nvidia\DLSS\Binaries\ThirdParty\Win64\nvngx_dlss.dll")));
        Assert.False(File.Exists(Path.Combine(_target, "nvngx_dlss.dll"))); // game already ships SR

        var ini = IniFile.Load(Path.Combine(_target, "OptiScaler.ini"));
        Assert.Equal("true", ini.Get("Plugins", "LoadReshade"));
        Assert.Equal("0x2e", ini.Get("Menu", "ShortcutKey"));
        Assert.Equal("true", ini.Get("Hotfix", "ManualInputPolling"));

        var m = InstallManifest.Load(_target)!;
        Assert.Equal("v9.9.9", m.OptiTag);
        Assert.Equal("v310.9.1", m.DlssTag);

        // New releases arrive: reinstall must replace our files without clobbering the original backups.
        SeedOpti("v10.0.0", "OPTI-2");
        SeedDlss("v310.10.0", "NEWER");
        await installer.InstallAsync(Game(), _target, Full, null, default);
        Assert.Equal("OPTI-2", T("dxgi.dll"));
        Assert.Equal("NEWER-nvngx_dlssg.dll", T("nvngx_dlssg.dll"));

        // A game patch reverts FG to a new original: that one becomes the backup.
        File.WriteAllText(Path.Combine(_target, "nvngx_dlssg.dll"), "PATCHED-FG");
        await installer.InstallAsync(Game(), _target, Full, null, default);
        Assert.Equal("NEWER-nvngx_dlssg.dll", T("nvngx_dlssg.dll"));

        await installer.UninstallAsync(Game(), _target, default);

        var after = Snapshot();
        var expected = new Dictionary<string, string>(before, StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(@"Game\Binaries\Win64", "nvngx_dlssg.dll")] = "PATCHED-FG",
        };
        Assert.Equal(expected.OrderBy(k => k.Key), after.OrderBy(k => k.Key));
        Assert.False(Directory.Exists(Path.Combine(_target, "OptiScaler")));
        Assert.False(Directory.Exists(InstallManifest.DirFor(_target)));
    }

    [Fact]
    public async Task DlssOnly_ThenRestore_ReturnsOriginals()
    {
        var before = Snapshot();
        var installer = new Installer(_store);
        await installer.InstallAsync(Game(), _target, new InstallOptions { Dlss = true }, null, default);

        Assert.Equal("OTHER-MOD", T("dxgi.dll"));
        Assert.Equal("NEW-nvngx_dlssg.dll", T("nvngx_dlssg.dll"));

        await installer.RestoreDlssAsync(Game(), _target, default);
        Assert.Equal(before.OrderBy(k => k.Key), Snapshot().OrderBy(k => k.Key));
    }

    [Fact]
    public async Task Update_KeepsExistingIniSettings()
    {
        // Hand-made install: its ini has a different menu key and NR tuning.
        Write(Path.Combine(_target, "OptiScaler.ini"), "[Menu]\nShortcutKey=0x24\n[DlssNr]\nLocalTone=0.100000\nEnabled=false\n");
        var installer = new Installer(_store);
        await installer.InstallAsync(Game(), _target, Full, null, default);

        var ini = IniFile.Load(Path.Combine(_target, "OptiScaler.ini"));
        Assert.Equal("0x24", ini.Get("Menu", "ShortcutKey"));
        Assert.Equal("0.100000", ini.Get("DlssNr", "LocalTone"));
        Assert.Equal("false", ini.Get("DlssNr", "Enabled"));
        Assert.Equal("true", ini.Get("Plugins", "LoadReshade"));  // was not set -> profile
        Assert.Equal("0xdc", ini.Get("DlssNr", "ToggleKey"));

        // Settings changed in the OptiScaler overlay survive the next release.
        ini.Set("DlssNr", "Passes", "2");
        ini.Save(Path.Combine(_target, "OptiScaler.ini"));
        SeedOpti("v10.0.0", "OPTI-2");
        await installer.InstallAsync(Game(), _target, Full, null, default);
        ini = IniFile.Load(Path.Combine(_target, "OptiScaler.ini"));
        Assert.Equal("2", ini.Get("DlssNr", "Passes"));
        Assert.Equal("0x24", ini.Get("Menu", "ShortcutKey"));

        // Uninstall brings back the hand-made ini untouched.
        await installer.UninstallAsync(Game(), _target, default);
        Assert.Equal("[Menu]\nShortcutKey=0x24\n[DlssNr]\nLocalTone=0.100000\nEnabled=false\n", T("OptiScaler.ini"));
    }

    [Fact]
    public async Task PreexistingPackageFolder_IsLeftAsItWas()
    {
        Write(Path.Combine(_target, @"Licenses\XeSS_LICENSE.txt"), "old-lic");
        Write(Path.Combine(_target, @"Licenses\Other.txt"), "keep");
        var before = Snapshot();
        var installer = new Installer(_store);

        await installer.InstallAsync(Game(), _target, Full, null, default);
        Assert.Equal("lic", T(@"Licenses\XeSS_LICENSE.txt"));
        Assert.DoesNotContain(@"Game\Binaries\Win64\Licenses", InstallManifest.Load(_target)!.Dirs);

        await installer.UninstallAsync(Game(), _target, default);
        Assert.Equal(before.OrderBy(k => k.Key), Snapshot().OrderBy(k => k.Key));
    }

    [Fact]
    public async Task ReShadeIni_PatchedAndKept()
    {
        Write(Path.Combine(_target, "ReShade.ini"), "[INPUT]\nKeyOverlay=36,0,0,0\n[OVERLAY]\nShowFPS=2\n");
        var installer = new Installer(_store);
        var o = new InstallOptions
        {
            ReShade = true,
            ReShadeOverrides = [new("INPUT", "KeyOverlay", "35,0,0,0"), new("OVERLAY", "TutorialProgress", "4"), new("RenoDX.MFGUnlock", "DynamicMFG", "1")],
        };
        await installer.InstallAsync(Game(), _target, o, null, default);

        var ini = IniFile.Load(Path.Combine(_target, "ReShade.ini"));
        Assert.Equal("36,0,0,0", ini.Get("INPUT", "KeyOverlay"));   // the game's own ReShade.ini wins
        Assert.Equal("2", ini.Get("OVERLAY", "ShowFPS"));
        Assert.Equal("4", ini.Get("OVERLAY", "TutorialProgress"));
        Assert.Equal("1", ini.Get("RenoDX.MFGUnlock", "DynamicMFG"));

        // Profile change for a key we wrote rolls out on the next update.
        o = new InstallOptions { ReShade = true, ReShadeOverrides = [new("RenoDX.MFGUnlock", "DynamicMFG", "0"), new("OVERLAY", "TutorialProgress", "4")] };
        await installer.InstallAsync(Game(), _target, o, null, default);
        Assert.Equal("0", IniFile.Load(Path.Combine(_target, "ReShade.ini")).Get("RenoDX.MFGUnlock", "DynamicMFG"));

        await installer.UninstallAsync(Game(), _target, default);
        Assert.Equal("[INPUT]\nKeyOverlay=36,0,0,0\n[OVERLAY]\nShowFPS=2\n", T("ReShade.ini"));
    }

    [Fact]
    public async Task Streamline_SwapsWholeSet_RestoreBringsBack()
    {
        var slCache = Path.Combine(ComponentStore.TagDir(Component.Streamline, "v2.14.1"), "x64");
        Write(Path.Combine(slCache, "sl.interposer.dll"), "SL-NEW-interposer");
        Write(Path.Combine(slCache, "sl.common.dll"), "SL-NEW-common");
        Write(Path.Combine(slCache, "sl.dlss_g.dll"), "SL-NEW-dlssg");
        var r = new ReleaseInfo { Tag = "v2.14.1" };
        ComponentStore.MarkComplete(Component.Streamline, r);
        _store.Streamline = r;

        var slDir = Path.Combine(_root, @"Game\Plugins\Streamline\Binaries\ThirdParty\Win64");
        Write(Path.Combine(slDir, "sl.interposer.dll"), "SL-OLD-interposer");
        Write(Path.Combine(slDir, "sl.common.dll"), "SL-OLD-common");
        Write(Path.Combine(slDir, "sl.gamespecific.dll"), "SL-CUSTOM");
        var before = Snapshot();

        var installer = new Installer(_store);
        await installer.InstallAsync(Game(), _target, new InstallOptions { Dlss = true, Streamline = true }, null, default);

        Assert.Equal("SL-NEW-interposer", File.ReadAllText(Path.Combine(slDir, "sl.interposer.dll")));
        Assert.Equal("SL-NEW-common", File.ReadAllText(Path.Combine(slDir, "sl.common.dll")));
        Assert.Equal("SL-CUSTOM", File.ReadAllText(Path.Combine(slDir, "sl.gamespecific.dll")));
        Assert.False(File.Exists(Path.Combine(slDir, "sl.dlss_g.dll"))); // never adds plugins the game didn't ship
        Assert.Equal("v2.14.1", InstallManifest.Load(_target)!.StreamlineTag);

        await installer.RestoreDlssAsync(Game(), _target, default);
        Assert.Equal(before.OrderBy(k => k.Key), Snapshot().OrderBy(k => k.Key));
    }

    [Fact]
    public async Task AddsSrWhenGameHasNone()
    {
        File.Delete(Path.Combine(_root, @"Engine\Plugins\Runtime\Nvidia\DLSS\Binaries\ThirdParty\Win64\nvngx_dlss.dll"));
        var installer = new Installer(_store);
        await installer.InstallAsync(Game(), _target, Full, null, default);
        Assert.Equal("NEW-nvngx_dlss.dll", T("nvngx_dlss.dll"));

        await installer.UninstallAsync(Game(), _target, default);
        Assert.False(File.Exists(Path.Combine(_target, "nvngx_dlss.dll")));
    }
}
