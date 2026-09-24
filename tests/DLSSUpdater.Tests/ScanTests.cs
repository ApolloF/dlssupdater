using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

public class ScanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dlssu-scan-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void Vdf_ParsesLibraryFolders()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"      "C:\\Program Files (x86)\\Steam"
                    "apps" { "228980" "1" }
                }
                "1" { "path" "R:\\SteamLibrary" }
            }
            """;
        var root = VdfNode.Parse(vdf).Child("libraryfolders")!;
        Assert.Equal(@"C:\Program Files (x86)\Steam", root.Child("0")!["path"]);
        Assert.Equal(@"R:\SteamLibrary", root.Child("1")!["path"]);
        Assert.Equal("1", root.Child("0")!.Child("apps")!["228980"]);
    }

    private string Touch(string rel, int size = 16)
    {
        var p = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[size]);
        return p;
    }

    [Fact]
    public void Inspect_PrefersUnrealShipping_FindsDlssAndAntiCheat()
    {
        Touch(@"MyGame.exe", 100);
        var shipping = Touch(@"MyGame\Binaries\Win64\MyGame-Win64-Shipping.exe", 50);
        Touch(@"Engine\Binaries\Win64\CrashReportClient.exe", 500);
        Touch(@"Engine\Plugins\Runtime\Nvidia\DLSS\Binaries\ThirdParty\Win64\nvngx_dlss.dll");
        Touch(@"MyGame\Binaries\Win64\nvngx_dlssg.dll");
        Touch(@"_CommonRedist\vcredist\vc_redist.x64.exe", 900);
        Directory.CreateDirectory(Path.Combine(_root, "EasyAntiCheat"));

        var g = new GameInfo { Root = _root, Name = "My Game" };
        GameInspector.Inspect(g);

        Assert.Equal(shipping, g.Exes[0]);
        Assert.Equal(2, g.Dlss.Count);
        Assert.Contains(g.Dlss, d => d.Kind == "FG");
        Assert.Equal("EasyAntiCheat", g.AntiCheat);
        Assert.DoesNotContain(g.Exes, e => e.Contains("vc_redist"));
    }

    [Fact]
    public void Inspect_UnityPlayerBeatsBiggerModTool()
    {
        var game = Touch(@"Beat Saber.exe", 10);
        Directory.CreateDirectory(Path.Combine(_root, "Beat Saber_Data"));
        Touch(@"ModAssistant.exe", 4_000_000);
        Touch(@"UnityCrashHandler64.exe", 2_000_000);

        var g = new GameInfo { Root = _root, Name = "Beat Saber" };
        GameInspector.Inspect(g);

        Assert.Equal(game, g.Exes[0]);
    }
}
