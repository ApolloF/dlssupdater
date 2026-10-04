using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

public class InstallerTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-inst-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root;
    private readonly string _target;
    private readonly ComponentStore _store;

    private const string ReleaseIni = "[Menu]\nShortcutKey=auto\n[Plugins]\nLoadReshade=auto\n[Hotfix]\nManualInputPolling=auto\n[DlssNr]\nEnabled=auto\n";

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
        Proxy = "dxgi.dll", Overrides = ConfigProfile.Recommended(), CarryOverIni = true,
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

    [Theory]
    [InlineData(true, false, "0x24", "true")]   // keep: game values stay
    [InlineData(true, true, "0x2e", "true")]    // apply: profile wins, other game keys stay
    [InlineData(false, false, "0x2e", null)]    // fresh: only release + profile
    public async Task IniModes(bool carryOver, bool overwrite, string menuKey, string? polling)
    {
        Write(Path.Combine(_target, "OptiScaler.ini"), "[Menu]\nShortcutKey=0x24\n[Hotfix]\nManualInputPolling=true\n");
        var o = new InstallOptions { Opti = true, Proxy = "dxgi.dll", Overrides = ConfigProfile.Recommended(), CarryOverIni = carryOver, OverwriteIni = overwrite };
        await new Installer(_store).InstallAsync(Game(), _target, o, null, default);

        var ini = IniFile.Load(Path.Combine(_target, "OptiScaler.ini"));
        Assert.Equal(menuKey, ini.Get("Menu", "ShortcutKey"));
        Assert.Equal(polling ?? "auto", ini.Get("Hotfix", "ManualInputPolling"));
    }

    [Fact]
    public void Option_MatchesNumbersByValue()
    {
        var list = new System.Collections.ObjectModel.ObservableCollection<IniOverride> { new("DlssNr", "LocalStructure", "0.7") };
        var opt = ViewModels.SettingsOptions.Nr(list).First(x => x.Key == "LocalStructure");
        Assert.Equal("0.7", opt.Selected.Label);
        opt.Selected = opt.Choices.First(c => c.Label == "0.5");
        Assert.Equal("0.500000", list.Single().Value);
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

    [Fact]
    public async Task UpstreamDefaults_OnlyRequiredKeys_FollowComponents()
    {
        File.Delete(Path.Combine(_target, "OptiScaler.ini"));
        var installer = new Installer(_store);
        var o = new InstallOptions { Opti = true, ReShade = true, DlssNr = true, Proxy = "dxgi.dll", Overrides = ConfigProfile.Defaults() };
        await installer.InstallAsync(Game(), _target, o, null, default);

        var expected = IniFile.Parse(ReleaseIni);
        expected.Set("Plugins", "LoadReshade", "true");
        expected.Set("DlssNr", "Enabled", "true");
        Assert.Equal(expected.ToString(), T("OptiScaler.ini"));

        // ReShade and DLSSNR dropped: both keys go back to the release value.
        o = new InstallOptions { Opti = true, Proxy = "dxgi.dll", Overrides = ConfigProfile.Defaults() };
        await installer.InstallAsync(Game(), _target, o, null, default);
        Assert.Equal(ReleaseIni, T("OptiScaler.ini"));

        // An explicit profile value wins over the required one.
        o = new InstallOptions { Opti = true, DlssNr = true, Proxy = "dxgi.dll", Overrides = [new("DlssNr", "Enabled", "false")] };
        await installer.InstallAsync(Game(), _target, o, null, default);
        Assert.Equal("false", IniFile.Load(Path.Combine(_target, "OptiScaler.ini")).Get("DlssNr", "Enabled"));
    }

    [Fact]
    public void RecommendedPreset_IsBuiltIn_AndNotSaved()
    {
        var s = new AppSettings();
        Assert.Empty(s.IniOverrides);
        Assert.Empty(s.ReShadeOverrides);
        var rec = Assert.Single(s.AllPresets);
        Assert.True(rec.BuiltIn);
        Assert.Contains(rec.Opti, x => x.Id == "Menu/ShortcutKey" && x.Value == "0x2e");

        s.Games["g"] = new GameOverride { Preset = ConfigProfile.RecommendedName };
        Assert.Same(rec.Name, s.PresetFor("g")!.Name);
        s.Save();
        Assert.Empty(AppSettings.Load().Presets);
    }

    [Fact]
    public async Task AddsSr_WithoutReplaceToggle_AndKeepsItCurrent()
    {
        File.Delete(Path.Combine(_root, @"Engine\Plugins\Runtime\Nvidia\DLSS\Binaries\ThirdParty\Win64\nvngx_dlss.dll"));
        File.Delete(Path.Combine(_target, "nvngx_dlssg.dll"));
        var installer = new Installer(_store);
        var o = new InstallOptions { Opti = true, AddMissingDlss = true, Proxy = "dxgi.dll" };
        await installer.InstallAsync(Game(), _target, o, null, default);
        Assert.Equal("NEW-nvngx_dlss.dll", T("nvngx_dlss.dll"));
        Assert.False(InstallManifest.Load(_target)!.Dlss);

        SeedDlss("v310.10.0", "NEWER");
        await installer.InstallAsync(Game(), _target, o, null, default);
        Assert.Equal("NEWER-nvngx_dlss.dll", T("nvngx_dlss.dll"));

        await installer.UninstallAsync(Game(), _target, default);
        Assert.False(File.Exists(Path.Combine(_target, "nvngx_dlss.dll")));
    }

    [Fact]
    public async Task ReplaceOff_LeavesGameDlssAlone()
    {
        var installer = new Installer(_store);
        await installer.InstallAsync(Game(), _target, new InstallOptions { Opti = true, AddMissingDlss = true, Proxy = "dxgi.dll" }, null, default);
        Assert.Equal("OLD-FG", T("nvngx_dlssg.dll"));
        Assert.False(File.Exists(Path.Combine(_target, "nvngx_dlss.dll")));  // game ships SR elsewhere
    }

    [Fact]
    public async Task ReShade_DownloadedBuildUsed_WhenNothingImported()
    {
        File.Delete(Path.Combine(AppPaths.Components, ComponentStore.ReShadeFile));
        var r = new ReleaseInfo { Tag = "6.8.0" };
        Write(Path.Combine(ComponentStore.TagDir(Component.ReShade, r.Tag), ComponentStore.ReShadeFile), "RESHADE-AUTO");
        ComponentStore.MarkComplete(Component.ReShade, r);
        _store.ReShade = r;

        var installer = new Installer(_store);
        await installer.InstallAsync(Game(), _target, new InstallOptions { ReShade = true }, null, default);
        Assert.Equal("RESHADE-AUTO", T("ReShade64.dll"));

        // Auto download off and nothing imported: a clear error instead of a silent skip.
        _store.AutoReShade = () => false;
        await Assert.ThrowsAsync<FileNotFoundException>(() => installer.InstallAsync(Game(), _target, new InstallOptions { ReShade = true }, null, default));
    }

    [Fact]
    public void ReShade_ExtractsDllFromSetupWithPrependedExe()
    {
        var zip = new MemoryStream();
        using (var a = new System.IO.Compression.ZipArchive(zip, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, body) in new[] { ("ReShade32.dll", "R32"), ("ReShade64.dll", "R64"), ("ReShade64.json", "{}") })
            {
                using var w = new StreamWriter(a.CreateEntry(name).Open());
                w.Write(body);
            }
        }
        var setup = Path.Combine(_tmp, "ReShade_Setup_6.8.0_Addon.exe");
        using (var f = File.Create(setup))
        {
            f.Write(System.Text.Encoding.ASCII.GetBytes("MZ" + new string('\0', 4094)));
            zip.Position = 0;
            zip.CopyTo(f);
        }
        var dest = Path.Combine(_tmp, "out", "ReShade64.dll");
        ComponentStore.ExtractReShade(setup, dest);
        Assert.Equal("R64", File.ReadAllText(dest));
    }

    [Theory]
    [InlineData("<a href=\"/downloads/ReShade_Setup_6.8.0.exe\">x</a><a href=\"/downloads/ReShade_Setup_6.8.0_Addon.exe\">y</a>", "6.8.0")]
    [InlineData("downloads/ReShade_Setup_6.7.3_Addon.exe downloads/ReShade_Setup_6.10.1_Addon.exe", "6.10.1")]
    [InlineData("<html>no links</html>", null)]
    public void ReShade_ParsesDownloadPage(string html, string? expected) => Assert.Equal(expected, ComponentStore.ParseReShadePage(html));

    private static InstallOptions ReShadeOnly(string proxy = "dxgi.dll") => new()
    {
        Mode = InstallMode.ReShadeOnly, ReShade = true, Mfg = true, Proxy = proxy,
        ReShadeOverrides = [new("OVERLAY", "TutorialProgress", "4")],
    };

    [Fact]
    public async Task ReShadeOnly_InstallsReShadeAsProxy_WithoutOptiScaler()
    {
        var before = Snapshot();
        var installer = new Installer(_store);
        await installer.InstallAsync(Game(), _target, ReShadeOnly(), null, default);

        Assert.Equal("RESHADE", T("dxgi.dll"));                     // other mod's dxgi.dll went to the backup
        Assert.Equal("MFG-1", T(ComponentStore.MfgFile));
        Assert.Equal("4", IniFile.Load(Path.Combine(_target, "ReShade.ini")).Get("OVERLAY", "TutorialProgress"));
        Assert.False(File.Exists(Path.Combine(_target, "ReShade64.dll")));
        Assert.False(Directory.Exists(Path.Combine(_target, "OptiScaler")));
        Assert.Equal("[Hotfix]\nManualInputPolling=true\n", T("OptiScaler.ini")); // untouched
        var m = InstallManifest.Load(_target)!;
        Assert.Equal(InstallMode.ReShadeOnly, m.Mode);
        Assert.False(m.Opti);

        await installer.UninstallAsync(Game(), _target, default);
        Assert.Equal(before.OrderBy(k => k.Key), Snapshot().OrderBy(k => k.Key));
    }

    [Fact]
    public async Task SwitchingModes_LeavesNoStrayFiles()
    {
        var before = Snapshot();
        var installer = new Installer(_store);
        var opti = new InstallOptions
        {
            Opti = true, ReShade = true, Mfg = true, DlssNr = true, Proxy = "winmm.dll", Overrides = ConfigProfile.Defaults(),
        };

        await installer.InstallAsync(Game(), _target, opti, null, default);
        Assert.Equal("OPTI-1", T("winmm.dll"));
        Assert.Equal("RESHADE", T("ReShade64.dll"));

        // OptiScaler -> ReShade-only on dxgi.dll: OptiScaler, its ini and DLSSNR go, the hand-made ini comes back.
        await installer.InstallAsync(Game(), _target, ReShadeOnly(), null, default);
        Assert.False(File.Exists(Path.Combine(_target, "winmm.dll")));
        Assert.False(File.Exists(Path.Combine(_target, "ReShade64.dll")));
        Assert.False(File.Exists(Path.Combine(_target, ComponentStore.DlssNrFile)));
        Assert.False(Directory.Exists(Path.Combine(_target, "OptiScaler")));
        Assert.False(Directory.Exists(Path.Combine(_target, "Licenses")));
        Assert.Equal("[Hotfix]\nManualInputPolling=true\n", T("OptiScaler.ini"));
        Assert.Equal("RESHADE", T("dxgi.dll"));
        var m = InstallManifest.Load(_target)!;
        Assert.False(m.Opti);
        Assert.False(m.DlssNr);
        Assert.Empty(m.OptiIni);

        // Back to OptiScaler on winmm.dll: the ReShade proxy on dxgi.dll is removed and the other mod's dxgi.dll restored.
        await installer.InstallAsync(Game(), _target, opti, null, default);
        Assert.Equal("OTHER-MOD", T("dxgi.dll"));
        Assert.Equal("OPTI-1", T("winmm.dll"));
        Assert.Equal("RESHADE", T("ReShade64.dll"));
        Assert.Equal(InstallMode.OptiScaler, InstallManifest.Load(_target)!.Mode);

        await installer.UninstallAsync(Game(), _target, default);
        Assert.Equal(before.OrderBy(k => k.Key), Snapshot().OrderBy(k => k.Key));
    }

    [Fact]
    public async Task ReShadeOnly_WithoutReShade_Throws()
    {
        var o = new InstallOptions { Mode = InstallMode.ReShadeOnly, Mfg = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new Installer(_store).InstallAsync(Game(), _target, o, null, default));
    }

    // ---------- review 2026-10: crafted, moved or broken install records ----------

    private string Engine(string file) => Path.Combine(_root, @"Engine\Plugins\Runtime\Nvidia\DLSS\Binaries\ThirdParty\Win64", file);

    /// <summary>A folder next to the game that no install record may reach.</summary>
    private string Victim()
    {
        var v = Path.Combine(_tmp, "Victim");
        Write(Path.Combine(v, "keep.txt"), "precious");
        return v;
    }

    private void EditManifest(Action<InstallManifest> edit)
    {
        var m = InstallManifest.Read(_target)!;
        edit(m);
        m.Save(_target);
    }

    private IEnumerable<string> BackupContents() =>
        Directory.EnumerateFiles(InstallManifest.BackupDirFor(_target), "*", SearchOption.AllDirectories).Select(File.ReadAllText);

    private static void Junction(string link, string target)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }

    [Theory]
    [InlineData("dir-up")]
    [InlineData("dir-absolute")]
    [InlineData("dir-outside-install-folder")]
    [InlineData("root-up")]
    [InlineData("file-up")]
    [InlineData("backup-original-up")]
    [InlineData("backup-escapes-backup-folder")]
    public async Task CraftedManifest_UninstallRefuses_AndTouchesNothingOutside(string attack)
    {
        var victim = Victim();
        var installer = new Installer(_store);
        await installer.InstallAsync(Game(), _target, new InstallOptions { Dlss = true }, null, default);
        Write(Path.Combine(InstallManifest.BackupDirFor(_target), "evil.dll"), "EVIL");
        EditManifest(m =>
        {
            switch (attack)
            {
                case "dir-up": m.Dirs.Add(@"..\Victim"); break;
                case "dir-absolute": m.Dirs.Add(victim); break;
                case "dir-outside-install-folder": m.Dirs.Add("Engine"); break;
                case "root-up": m.RootRel = @"..\..\..\.."; m.Dirs.Add("Victim"); break;
                case "file-up": m.Files.Add(@"..\Victim\keep.txt"); break;
                case "backup-original-up": m.Backups.Add(new BackupEntry { Original = @"..\Victim\evil.dll", Backup = "evil.dll" }); break;
                case "backup-escapes-backup-folder": m.Backups.Add(new BackupEntry { Original = "Game.exe", Backup = @"..\..\..\..\..\..\Victim\keep.txt" }); break;
            }
        });
        var before = Snapshot();

        await Assert.ThrowsAsync<UnsafeManifestException>(() => installer.UninstallAsync(Game(), _target, default));
        await Assert.ThrowsAsync<UnsafeManifestException>(() => installer.RestoreDlssAsync(Game(), _target, default));

        Assert.Equal(["keep.txt"], Directory.EnumerateFiles(victim).Select(Path.GetFileName));
        Assert.Equal("precious", File.ReadAllText(Path.Combine(victim, "keep.txt")));
        Assert.Equal(before.OrderBy(k => k.Key), Snapshot().OrderBy(k => k.Key));
    }

    [Fact]
    public async Task CraftedManifest_LaunchStyleReinstall_RefusesProxyOutsideTheGame()
    {
        // Seaglass's beforeLaunch reinstalls from the record: its proxy and files must not reach outside either.
        var victim = Victim();
        var installer = new Installer(_store);
        await installer.InstallAsync(Game(), _target, Full, null, default);
        EditManifest(m =>
        {
            m.Proxy = @"..\..\..\..\Victim\keep.txt";
            m.Files.Add(@"..\Victim\keep.txt");
        });

        await Assert.ThrowsAsync<UnsafeManifestException>(() => installer.InstallAsync(Game(), _target, ReShadeOnly(), null, default));
        Assert.Equal("precious", File.ReadAllText(Path.Combine(victim, "keep.txt")));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            installer.InstallAsync(Game(), _target, new InstallOptions { Opti = true, Proxy = @"..\..\evil.dll" }, null, default));
    }

    [Fact]
    public async Task ManifestPathThroughJunction_IsRefused()
    {
        var victim = Victim();
        var installer = new Installer(_store);
        await installer.InstallAsync(Game(), _target, new InstallOptions { Dlss = true }, null, default);
        var link = Path.Combine(_target, "Link");
        Junction(link, victim);
        try
        {
            EditManifest(m => m.Files.Add(@"Game\Binaries\Win64\Link\keep.txt"));

            await Assert.ThrowsAsync<UnsafeManifestException>(() => installer.UninstallAsync(Game(), _target, default));
            Assert.Equal("precious", File.ReadAllText(Path.Combine(victim, "keep.txt")));
        }
        finally
        {
            Directory.Delete(link); // removes the junction only; a recursive delete of the temp folder can't
        }
    }

    [Fact]
    public async Task SameFolderFromNarrowerRoot_IsRefused_OriginalsStayRestorable()
    {
        var installer = new Installer(_store);
        await installer.InstallAsync(Game(), _target, new InstallOptions { Dlss = true }, null, default);

        // "Add game folder" picked Game\Game: the SR backup at Game\Engine is out of its reach.
        var inner = GameScanner.Inspect(new GameEntry("Game", Path.Combine(_root, "Game"), "Manual"));
        await Assert.ThrowsAsync<UnsafeManifestException>(() => installer.InstallAsync(inner, _target, new InstallOptions { Dlss = true }, null, default));

        await installer.RestoreDlssAsync(Game(), _target, default);
        Assert.Equal("OLD-FG", T("nvngx_dlssg.dll"));
        Assert.Equal("OLD-SR", File.ReadAllText(Engine("nvngx_dlss.dll")));
    }

    [Fact]
    public async Task SameFolderFromWiderRoot_RebasesRecord_RestoreReturnsOriginals()
    {
        var before = Snapshot();
        var installer = new Installer(_store);
        var inner = GameScanner.Inspect(new GameEntry("Game", Path.Combine(_root, "Game"), "Manual"));
        await installer.InstallAsync(inner, _target, new InstallOptions { Dlss = true }, null, default);
        await installer.InstallAsync(Game(), _target, new InstallOptions { Dlss = true }, null, default);

        await installer.RestoreDlssAsync(Game(), _target, default);
        Assert.Equal(before.OrderBy(k => k.Key), Snapshot().OrderBy(k => k.Key));
    }

    [Fact]
    public async Task UnreadableManifest_InstallRefuses_BackupsSurvive()
    {
        var installer = new Installer(_store);
        await installer.InstallAsync(Game(), _target, new InstallOptions { Dlss = true }, null, default);
        File.WriteAllText(InstallManifest.PathFor(_target), "{ truncated");

        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(Game(), _target, new InstallOptions { Dlss = true }, null, default));
        Assert.Contains("OLD-FG", BackupContents());
        Assert.Equal("NEW-nvngx_dlssg.dll", T("nvngx_dlssg.dll"));
        Assert.Null(InstallManifest.Load(_target)); // display code still just sees "not installed"
    }

    [Fact]
    public async Task LostManifest_NewInstallNeverOverwritesSavedOriginals()
    {
        var installer = new Installer(_store);
        await installer.InstallAsync(Game(), _target, new InstallOptions { Dlss = true }, null, default);
        File.Delete(InstallManifest.PathFor(_target));

        await installer.InstallAsync(Game(), _target, new InstallOptions { Dlss = true }, null, default);
        Assert.Contains("OLD-FG", BackupContents());
        Assert.Contains("OLD-SR", BackupContents());
    }

    [Fact]
    public async Task LaunchReinstall_KeepsInstalledOptiAndMfgVersions()
    {
        var installer = new Installer(_store);
        await installer.InstallAsync(Game(), _target, Full, null, default);
        SeedOpti("v10.0.0", "OPTI-2");
        SeedMfg("2.0", "MFG-2");

        var m = InstallManifest.Read(_target)!;
        await installer.InstallAsync(Game(), _target, Full.KeepingInstalledVersions(m), null, default);
        Assert.Equal("OPTI-1", T("dxgi.dll"));
        Assert.Equal("MFG-1", T(ComponentStore.MfgFile));
        Assert.Equal("v9.9.9", InstallManifest.Read(_target)!.OptiTag);
        Assert.Equal("1.0", InstallManifest.Read(_target)!.MfgTag);

        // The installed release left the cache: OptiScaler is left alone rather than upgraded.
        Directory.Delete(ComponentStore.TagDir(Component.OptiScaler, "v9.9.9"), true);
        var o = Full.KeepingInstalledVersions(InstallManifest.Read(_target)!);
        Assert.False(o.Opti);
        Assert.True(o.Mfg);
        await installer.InstallAsync(Game(), _target, o, null, default);
        Assert.Equal("OPTI-1", T("dxgi.dll"));

        // A click in DLSS Updater (no pin) moves to the newest release.
        await installer.InstallAsync(Game(), _target, Full, null, default);
        Assert.Equal("OPTI-2", T("dxgi.dll"));
        Assert.Equal("MFG-2", T(ComponentStore.MfgFile));
    }

    [Fact]
    public async Task RestoreDlss_KeepsAFileTheGameUpdatedSince()
    {
        var installer = new Installer(_store);
        await installer.InstallAsync(Game(), _target, new InstallOptions { Dlss = true }, null, default);
        File.WriteAllText(Path.Combine(_target, "nvngx_dlssg.dll"), "GAME-PATCH-3.8");

        await installer.RestoreDlssAsync(Game(), _target, default);
        Assert.Equal("GAME-PATCH-3.8", T("nvngx_dlssg.dll"));
        Assert.Equal("OLD-SR", File.ReadAllText(Engine("nvngx_dlss.dll")));
    }

    [Fact]
    public void GitHubToken_IsSavedEncrypted_AndPlainTextIsMigrated()
    {
        const string token = "github_pat_TEST_ONLY_not_a_real_token";
        new AppSettings { GitHubToken = token }.Save();
        Assert.DoesNotContain(token, File.ReadAllText(AppPaths.SettingsFile));
        Assert.Equal(token, AppSettings.Load().GitHubToken);

        // settings.json from 1.5.1: the plain token is read, then rewritten encrypted.
        File.WriteAllText(AppPaths.SettingsFile, $"{{ \"GitHubToken\": \"{token}\" }}");
        Assert.Equal(token, AppSettings.Load().GitHubToken);
        Assert.DoesNotContain(token, File.ReadAllText(AppPaths.SettingsFile));
        Assert.Equal(token, AppSettings.Load().GitHubToken);
    }
}
