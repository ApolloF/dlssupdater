using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

public class IniTests
{
    // Normalized so the fixture is LF regardless of how git checked the file out.
    private static readonly string Release = """
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
        """.ReplaceLineEndings("\n");

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
        var merged = IniFile.Parse(ConfigProfile.Merge(Release, current, ConfigProfile.Defaults(), carryOver: true).Text);

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
        var merged = IniFile.Parse(ConfigProfile.Merge(Release, current, [], carryOver: true).Text);
        Assert.Equal("true", merged.Get("Hotfix", "ManualInputPolling"));

        var fresh = IniFile.Parse(ConfigProfile.Merge(Release, current, [], carryOver: false).Text);
        Assert.Null(fresh.Get("Hotfix", "ManualInputPolling"));
    }

    [Fact]
    public void Merge_PreservesCrLf()
    {
        var crlf = Release.Replace("\n", "\r\n");
        var merged = ConfigProfile.Merge(crlf, null, ConfigProfile.Defaults(), true).Text;
        Assert.DoesNotContain("\r\r", merged);
        Assert.Contains("LoadReshade=true\r\n", merged);
    }

    [Fact]
    public void Merge_ThreeWay_ProfileChangesRollOut_UserEditsStay()
    {
        var profile1 = new List<IniOverride> { new("Menu", "ShortcutKey", "0x2e"), new("Plugins", "LoadReshade", "true") };
        var first = ConfigProfile.Merge(Release, null, profile1, carryOver: true);
        Assert.Equal("0x2e", first.Applied["Menu/ShortcutKey"]);

        // User flips LoadReshade in-game; app profile moves the menu key to F1.
        var edited = IniFile.Parse(first.Text);
        edited.Set("Plugins", "LoadReshade", "false");
        var profile2 = new List<IniOverride> { new("Menu", "ShortcutKey", "0x70"), new("Plugins", "LoadReshade", "true") };
        var second = ConfigProfile.Merge(Release, edited.ToString(), profile2, carryOver: true, first.Applied);
        var ini = IniFile.Parse(second.Text);

        Assert.Equal("0x70", ini.Get("Menu", "ShortcutKey"));   // ours -> follows the profile
        Assert.Equal("false", ini.Get("Plugins", "LoadReshade")); // user's -> kept
        Assert.False(second.Applied.ContainsKey("Plugins/LoadReshade"));
    }

    [Fact]
    public void Merge_DroppedProfileKey_ReturnsToReleaseDefault()
    {
        var first = ConfigProfile.Merge(Release, null, [new IniOverride("Menu", "ShortcutKey", "0x2e")], true);
        var second = ConfigProfile.Merge(Release, first.Text, [], true, first.Applied);
        Assert.Equal("auto", IniFile.Parse(second.Text).Get("Menu", "ShortcutKey"));
    }

    [Theory]
    [InlineData(0x2E, false, false, false, "0x2e", "46,0,0,0")]
    [InlineData(0x70, true, false, true, "0x70", "112,1,0,1")]
    public void Hotkey_Encodings(int vk, bool ctrl, bool shift, bool alt, string opti, string reshade)
    {
        var k = new Hotkey(vk, ctrl, shift, alt);
        Assert.Equal(opti, k.ToOpti());
        Assert.Equal(reshade, k.ToReShade());
        Assert.Equal(k, Hotkey.ParseReShade(reshade));
        Assert.Equal(vk, Hotkey.ParseOpti(opti)!.Value.Vk);
        Assert.Null(Hotkey.ParseOpti("auto"));
        Assert.True(Hotkey.ParseOpti("-1")!.Value.IsNone);
        Assert.Equal("Delete", Hotkey.KeyName(0x2E));
    }

    [Theory]
    [InlineData("v310.9.1", "310.9.1")]
    [InlineData("1.1.5", "1.1.5")]
    [InlineData("v0.8.91", "0.8.91")]
    [InlineData("1.1", "1.1")]
    public void ParseTag(string tag, string expected) => Assert.Equal(Version.Parse(expected), FileUtil.ParseTag(tag));
}
