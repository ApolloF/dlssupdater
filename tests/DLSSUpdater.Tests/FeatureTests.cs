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

    [Fact]
    public void NvOption_MatchesPrimaryIdsOnly()
    {
        var sr = NvSettings.Create(global: true).First(o => o.Topic == "nv-sr-preset");
        sr.SetLoaded(new Dictionary<uint, uint?> { [0x10E41DF3] = 12 });   // L, override switch not read
        Assert.Equal("L", sr.Selected!.Label);
        Assert.False(sr.IsDirty);

        sr.Selected = sr.Choices.First(c => c.Label == "K");
        Assert.True(sr.IsDirty);
        Assert.Contains((0x10E41DF3u, 11u), NvOptionViewModel.Pairs(sr.Selected.Value));
        Assert.Contains((0x10E41E01u, 1u), NvOptionViewModel.Pairs(sr.Selected.Value)); // companion override switch
    }

    [Fact]
    public void NvOption_CompositeAndCustom()
    {
        var res = NvSettings.Create(global: false).First(o => o.Topic == "nv-sr-mode");
        res.SetLoaded(new Dictionary<uint, uint?> { [0x10AFB768] = 6, [0x10E41DF5] = 0x4D });
        Assert.Equal("Custom 77%", res.Selected!.Label);

        res.SetLoaded(new Dictionary<uint, uint?> { [0x10AFB768] = 6, [0x10E41DF5] = 0x5A });
        Assert.StartsWith("Custom (", res.Selected!.Label);

        res.SetLoaded(new Dictionary<uint, uint?>());
        Assert.Equal("Use global", res.Selected!.Label);
        Assert.Null(res.Selected.Value);
    }
}
