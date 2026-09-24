using DLSSUpdater.Core;
using DLSSUpdater.ViewModels;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DLSSUpdater.Tests;

public class FeatureTests
{
    [Fact]
    public async Task Compat_FlagsMissingKeysAndNewerLine()
    {
        AppPaths.Root = Path.Combine(Path.GetTempPath(), "dlssu-compat-" + Guid.NewGuid().ToString("N")[..8]);
        AppPaths.Ensure();
        var dir = Path.Combine(AppPaths.Cache, "optiscaler");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "ini-v0100.ini"), "[Menu]\nShortcutKey=auto\n[DlssNr]\nEnabled=auto\n");

        var r = await OptiCompat.CheckAsync(new GitHubClient(), "v0.10.0",
            [("Menu", "ShortcutKey"), ("DlssNr", "Enabled"), ("DlssNr", "ReversibleMode")], default);

        Assert.Equal(["[DlssNr] ReversibleMode"], r.MissingKeys);
        Assert.True(r.NewerThanTested);
        Assert.Contains("ReversibleMode", r.Summary);
    }

    private static Dictionary<uint, uint?> D(params (uint, uint)[] v) => v.ToDictionary(x => x.Item1, x => (uint?)x.Item2);

    [Fact]
    public void NvOption_PresetBlock_AndGlobalLabel()
    {
        var sr = NvSettings.Create().First(o => o.Topic == "nv-sr-preset");
        var global = D((0x10E41DF3, 12), (0x10E41E01, 1));

        sr.SetLoaded(D(), global, gameDefault: false);
        Assert.Equal("Use global (L)", sr.Selected!.Label);
        Assert.Null(sr.Selected.Value);

        sr.SetLoaded(D((0x10E41DF3, 11), (0x10E41E01, 1)), global, false);
        Assert.Equal("K", sr.Selected!.Label);
        Assert.False(sr.IsDirty);

        sr.SetLoaded(D((0x10E41DF3, 0), (0x10E41E01, 0)), global, false);   // what the NVIDIA App stores for "off"
        Assert.Equal("Off", sr.Selected!.Label);

        sr.Selected = sr.DisplayChoices.First(c => c.Label == "M");
        Assert.True(sr.IsDirty);
        Assert.Equal([(0x10E41DF3u, 13u), (0x10E41E01u, 1u)], NvOptionViewModel.Pairs(sr.Selected!.Value).ToArray());

        sr.Selected = null;   // ComboBox churn must not clear the selection
        Assert.Equal("M", sr.Selected!.Label);
    }

    [Fact]
    public void NvOption_FgPresets_AreOnlyRealOnes()
    {
        var fg = NvSettings.Create().First(o => o.Topic == "nv-fg-preset");
        Assert.Equal(["Use global", "Off", "NVIDIA default", "A", "B"], fg.Choices.Select(c => c.Label).ToArray());
        fg.SetLoaded(D(), D((0x10E41DF1, 0xFFFFFE), (0x10E41E03, 1)), false);
        Assert.Equal("Use global (NVIDIA default)", fg.Selected!.Label);
    }

    [Fact]
    public void NvOption_CompositeCustomAndGameDefault()
    {
        var res = NvSettings.Create().First(o => o.Topic == "nv-sr-mode");
        res.SetLoaded(D((0x10AFB768, 6), (0x10E41DF5, 77)), D((0x10AFB768, 3)), false);
        Assert.Equal("Custom 77%", res.Selected!.Label);

        res.SetLoaded(D((0x10AFB768, 6), (0x10E41DF5, 90)), D(), false);
        Assert.StartsWith("Custom (", res.Selected!.Label);

        var sm = NvSettings.Create().First(o => o.Topic == "nv-smooth-motion");
        sm.SetLoaded(D(), D((0xB0D384C0, 1)), gameDefault: true);
        Assert.Equal("Game default (On)", sm.Selected!.Label);
    }
}
