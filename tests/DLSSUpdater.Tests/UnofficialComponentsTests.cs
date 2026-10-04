using System.Runtime.InteropServices;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Tests;

/// <summary>DLSSNR runtime and MFG Unlock: off by default, opt-in, and existing users keep what they have.</summary>
public class UnofficialComponentsTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-unofficial-" + Guid.NewGuid().ToString("N")[..8]);

    public UnofficialComponentsTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "appdata");
        AppPaths.Ensure();
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

    /// <summary>What 1.5.1 saved: both components on (the old defaults), no SettingsVersion.</summary>
    private const string LegacySettings = """
        {
          "DefaultProxy": "dxgi.dll",
          "InstallReShade": true,
          "InstallMfgUnlock": true,
          "InstallDlssNr": true,
          "IniMode": "keep"
        }
        """;

    // ---------- defaults ----------

    [Fact]
    public void NewSettings_HaveUnofficialComponentsOff()
    {
        var s = new AppSettings();
        Assert.False(s.AllowUnofficial);
        Assert.False(s.InstallMfgUnlock);
        Assert.False(s.InstallDlssNr);
    }

    [Fact]
    public void FirstRun_StaysOff_WithoutNotice()
    {
        var s = AppSettings.Load();

        Assert.False(s.AllowUnofficial);
        Assert.False(s.UnofficialNoticePending);
        Assert.Equal(UnofficialComponents.SettingsVersion, s.SettingsVersion);

        // Saved and loaded again it is still a new user, not a legacy one.
        s.Save();
        var again = AppSettings.Load();
        Assert.False(again.AllowUnofficial);
        Assert.False(again.UnofficialNoticePending);
    }

    [Fact]
    public void Install_WithDefaults_LeavesUnofficialComponentsOut()
    {
        var vm = Model(new AppSettings(), manifest: null);

        var o = vm.BuildOptions(dlssOnly: false, acConfirmed: false);

        Assert.False(o.Mfg);
        Assert.False(o.DlssNr);
        Assert.True(o.Opti);
        Assert.False(vm.Components.Single(c => c.Key == "mfg").Visible);
        Assert.False(vm.Components.Single(c => c.Key == "dlssnr").Visible);
    }

    [Fact]
    public void OptedOut_IgnoresPerComponentDefaults()
    {
        var vm = Model(new AppSettings { InstallMfgUnlock = true, InstallDlssNr = true }, manifest: null);

        var o = vm.BuildOptions(dlssOnly: false, acConfirmed: false);

        Assert.False(o.Mfg);
        Assert.False(o.DlssNr);
    }

    [Fact]
    public void OptedIn_UsesPerComponentDefaults()
    {
        var vm = Model(new AppSettings { AllowUnofficial = true, InstallMfgUnlock = true, InstallDlssNr = true }, manifest: null);

        var o = vm.BuildOptions(dlssOnly: false, acConfirmed: false);

        Assert.True(o.Mfg);
        Assert.True(o.DlssNr);
    }

    [Fact]
    public void OptedOut_GameWithComponents_KeepsThemButDoesNotUpdate()
    {
        var m = new InstallManifest { Opti = true, Mfg = true, DlssNr = true, MfgTag = "1.0", Proxy = "dxgi.dll" };
        var vm = Model(new AppSettings(), m);

        var update = vm.UpdateOptions(vm.Manifest!);
        Assert.False(update.Mfg);
        Assert.False(update.DlssNr);
        Assert.True(update.KeepDlssNr); // the runtime the game has stays turned on
        Assert.True(update.Opti);

        // Still listed (so nobody wonders where they went), but locked and never flagged as an update.
        var mfg = vm.Components.Single(c => c.Key == "mfg");
        Assert.True(mfg.Visible);
        Assert.False(mfg.Editable);
        Assert.False(mfg.Enabled);
        Assert.NotEqual(RowState.Update, mfg.State);
    }

    // ---------- migration of existing users ----------

    [Fact]
    public void Upgrade_WithDlssNrImported_KeepsComponentsOn_AndShowsNoticeOnce()
    {
        Write(AppPaths.SettingsFile, LegacySettings);
        Write(Path.Combine(AppPaths.Components, ComponentStore.DlssNrFile), "NR");

        var s = AppSettings.Load();

        Assert.True(s.AllowUnofficial);
        Assert.True(s.InstallMfgUnlock);
        Assert.True(s.InstallDlssNr);
        Assert.True(s.UnofficialNoticePending);
        Assert.Equal(UnofficialComponents.SettingsVersion, s.SettingsVersion);

        // The migration was saved: a second start doesn't redo it, the notice waits until shown.
        var again = AppSettings.Load();
        Assert.True(again.AllowUnofficial);
        Assert.True(again.UnofficialNoticePending);
        again.UnofficialNoticePending = false;
        again.Save();
        Assert.False(AppSettings.Load().UnofficialNoticePending);
    }

    [Fact]
    public void Upgrade_WithMfgDownloaded_KeepsComponentsOn()
    {
        Write(AppPaths.SettingsFile, LegacySettings);
        Write(Path.Combine(ComponentStore.TagDir(Component.MfgUnlock, "1.0"), ComponentStore.MfgFile), "MFG");
        ComponentStore.MarkComplete(Component.MfgUnlock, new ReleaseInfo { Tag = "1.0" });

        Assert.True(AppSettings.Load().AllowUnofficial);
    }

    [Fact]
    public void Upgrade_WithoutComponents_TurnsOff_ButStillExplains()
    {
        Write(AppPaths.SettingsFile, LegacySettings);

        var s = AppSettings.Load();

        Assert.False(s.AllowUnofficial);
        Assert.True(s.UnofficialNoticePending);
        Assert.Contains("Nothing was removed", UnofficialComponents.Notice(s.AllowUnofficial));
    }

    [Fact]
    public void Upgraded_ExistingInstall_KeepsUpdating()
    {
        Write(AppPaths.SettingsFile, LegacySettings);
        Write(Path.Combine(AppPaths.Components, ComponentStore.DlssNrFile), "NR");
        var m = new InstallManifest { Opti = true, Mfg = true, DlssNr = true, MfgTag = "1.0", Proxy = "dxgi.dll" };

        var vm = Model(AppSettings.Load(), m);
        var update = vm.UpdateOptions(vm.Manifest!);

        Assert.True(update.Mfg);
        Assert.True(update.DlssNr);
        Assert.False(update.KeepDlssNr);
        Assert.True(vm.Components.Single(c => c.Key == "mfg").Editable);
    }

    [Fact]
    public void Migrate_RunsOnlyOnce()
    {
        var s = new AppSettings { SettingsVersion = UnofficialComponents.SettingsVersion };

        Assert.False(UnofficialComponents.Migrate(s, alreadyHas: () => true));
        Assert.False(s.AllowUnofficial);
        Assert.False(s.UnofficialNoticePending);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "1.0")]
    public async Task Store_OnlyLooksUpMfgUnlock_WhenOptedIn(bool optedIn, string? expected)
    {
        Write(Path.Combine(ComponentStore.TagDir(Component.MfgUnlock, "1.0"), ComponentStore.MfgFile), "MFG");
        ComponentStore.MarkComplete(Component.MfgUnlock, new ReleaseInfo { Tag = "1.0" });
        var store = new ComponentStore(new GitHubClient(), () => true) { AllowUnofficial = () => optedIn };
        store.Mfg = new ReleaseInfo { Tag = "0.9" };

        // A cancelled refresh stays offline: every lookup falls back to the cache, except MFG Unlock when opted out.
        await store.RefreshAsync(new CancellationToken(canceled: true));

        Assert.Equal(expected, store.Mfg?.Tag);
    }

    // ---------- signature check instead of a hash list ----------

    [Fact]
    public void Authenticode_UnsignedFile_IsUnverified()
    {
        var f = Path.Combine(_tmp, "nvngx_dlssnr.dll");
        Write(f, "not a signed binary");

        Assert.False(Authenticode.IsTrusted(f));
        Assert.Equal(SignatureStatus.Unverified, Authenticode.Check(f));
    }

    [Fact]
    public void Authenticode_SignedByOthers_IsUnverified()
    {
        var coreclr = Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "coreclr.dll");

        Assert.True(Authenticode.IsTrusted(coreclr)); // valid Microsoft signature: the trust check itself works
        Assert.Equal(SignatureStatus.Unverified, Authenticode.Check(coreclr));
    }

    [Theory]
    [InlineData("NVIDIA Corporation", true)]
    [InlineData("NVIDIA Corp", false)]
    [InlineData("nvidia corporation", false)]
    [InlineData(".NET", false)]
    [InlineData(null, false)]
    public void Authenticode_OnlyNvidiaCorporationCounts(string? signer, bool expected) =>
        Assert.Equal(expected, Authenticode.IsNvidiaSigner(signer));

    // ---------- helpers ----------

    private GameViewModel Model(AppSettings settings, InstallManifest? manifest)
    {
        var root = Path.Combine(_tmp, "Game");
        var target = Path.Combine(root, @"Game\Binaries\Win64");
        Write(Path.Combine(target, "Game-Win64-Shipping.exe"), "EXE");
        if (manifest is not null) manifest.Save(target);

        var store = new ComponentStore(new GitHubClient(), () => true) { AllowUnofficial = () => settings.AllowUnofficial };
        var services = new Services { Settings = settings, Store = store, Installer = new Installer(store) };
        return new GameViewModel(GameScanner.Inspect(new GameEntry("Game", root, "Manual")), services);
    }
}
