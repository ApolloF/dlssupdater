using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

public class IniTests
{
    private const string Release = """
        ; -------------------------------------------------------
        [Upscalers]
        ; comment
        Dx12Upscaler=auto

        ; -------------------------------------------------------
        [Menu]
        ; Default (auto) is 0x2D - Insert
        ShortcutKey=auto
        NewInThisRelease=auto

        ; Comment that belongs to the next section
        [Plugins]
        LoadReshade=auto
        """;

    [Fact]
    public void Set_ReplacesValue_KeepsCommentsAndSpacing()
    {
        var ini = IniFile.Parse("[A]\n; c\nKey = auto\n");
        ini.Set("A", "Key", "true");
        Assert.Equal("[A]\n; c\nKey = true\n", ini.ToString());
    }

    [Fact]
    public void Set_MissingKey_InsertsAfterLastKeyOfSection()
    {
        var ini = IniFile.Parse(Release);
        ini.Set("Menu", "Extra", "1");
        var text = ini.ToString();
        Assert.True(text.IndexOf("Extra=1", StringComparison.Ordinal) < text.IndexOf("; Comment that belongs", StringComparison.Ordinal));
        Assert.Equal("1", ini.Get("menu", "extra"));
    }

    [Fact]
    public void Set_MissingSection_Appends()
    {
        var ini = IniFile.Parse(Release);
        ini.Set("DlssNr", "Enabled", "true");
        Assert.EndsWith("[DlssNr]\nEnabled=true\n", ini.ToString());
    }

    [Fact]
    public void Merge_GameSettingsWin_ProfileFillsAuto_KeepsNewKeys()
    {
        var current = "[Menu]\nShortcutKey=0x24\n[Upscalers]\nDx12Upscaler=xess\n[Plugins]\nLoadReshade=auto\n[Old]\nGone=auto\n";
        var merged = IniFile.Parse(ConfigProfile.Merge(Release, current, ConfigProfile.Defaults(), carryOver: true));

        Assert.Equal("0x24", merged.Get("Menu", "ShortcutKey"));       // existing game setting is kept
        Assert.Equal("xess", merged.Get("Upscalers", "Dx12Upscaler"));
        Assert.Equal("true", merged.Get("Plugins", "LoadReshade"));    // auto in the game -> profile applies
        Assert.Equal("auto", merged.Get("Menu", "NewInThisRelease"));  // new release key survives
        Assert.Null(merged.Get("Old", "Gone"));                        // auto values are not carried
        Assert.Equal("true", merged.Get("DlssNr", "Enabled"));
    }

    [Fact]
    public void Merge_CarriesNonProfileTweaks()
    {
        var current = "[Hotfix]\nManualInputPolling=true\n";
        var merged = IniFile.Parse(ConfigProfile.Merge(Release, current, [], carryOver: true));
        Assert.Equal("true", merged.Get("Hotfix", "ManualInputPolling"));

        var fresh = IniFile.Parse(ConfigProfile.Merge(Release, current, [], carryOver: false));
        Assert.Null(fresh.Get("Hotfix", "ManualInputPolling"));
    }

    [Fact]
    public void Merge_PreservesCrLf()
    {
        var crlf = Release.Replace("\n", "\r\n");
        var merged = ConfigProfile.Merge(crlf, null, ConfigProfile.Defaults(), true);
        Assert.DoesNotContain("\r\r", merged);
        Assert.Contains("LoadReshade=true\r\n", merged);
    }

    [Theory]
    [InlineData("v310.9.1", "310.9.1")]
    [InlineData("1.1.5", "1.1.5")]
    [InlineData("v0.8.91", "0.8.91")]
    [InlineData("1.1", "1.1")]
    public void ParseTag(string tag, string expected) => Assert.Equal(Version.Parse(expected), FileUtil.ParseTag(tag));
}
