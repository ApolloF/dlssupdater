using System.Text.Json.Nodes;
using DLSSUpdater.Addon;
using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

public class AddonTests
{
    private static string TempDir(string name)
    {
        var d = Path.Combine(Path.GetTempPath(), $"dlssu-addon-{name}-{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(d);
        return d;
    }

    private static async Task<List<JsonObject>> Talk(params string[] lines)
    {
        AppPaths.Root = TempDir("root");
        var output = new StringWriter();
        await new AddonServer(output).RunAsync(new StringReader(string.Join('\n', lines) + "\n"));
        return output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => (JsonObject)JsonNode.Parse(l)!).ToList();
    }

    private static string Req(int id, string method, string gameDir) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["method"] = method,
        ["params"] = new JsonObject { ["game"] = new JsonObject { ["title"] = "G", ["dir"] = gameDir } },
    }.ToJsonString();

    private static JsonObject Answer(List<JsonObject> msgs, int id) =>
        msgs.Single(m => m["id"] is JsonValue v && v.GetValue<int>() == id);

    [Fact]
    public async Task Initialize_AndErrors()
    {
        var game = TempDir("game");
        var msgs = await Talk(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocol":1,"host":{"name":"WaterLauncher","version":"test"}}}""",
            """{"jsonrpc":"2.0","id":2,"method":"game.nope","params":{}}""",
            "{not json",
            """{"jsonrpc":"2.0","id":3,"method":"game.status","params":{"game":{"dir":"relative\\path"}}}""",
            Req(4, "game.beforeLaunch", game),
            """{"jsonrpc":"2.0","method":"shutdown"}""");
        Assert.Equal(1, Answer(msgs, 1)["result"]!["protocol"]!.GetValue<int>());
        Assert.Equal("DLSS Updater", Answer(msgs, 1)["result"]!["name"]!.GetValue<string>());
        Assert.Equal(-32601, Answer(msgs, 2)["error"]!["code"]!.GetValue<int>());
        Assert.Contains(msgs, m => m["error"]?["code"]?.GetValue<int>() == -32700);
        Assert.Contains("wasn't found", Answer(msgs, 3)["error"]!["message"]!.GetValue<string>());
        // A game DLSS Updater never touched: nothing to do before launch.
        Assert.Null(Answer(msgs, 4)["result"]!["message"]);
    }

    [Fact]
    public async Task Status_ShowsDlssAndOffersActions()
    {
        var game = TempDir("dlss");
        File.WriteAllText(Path.Combine(game, "game.exe"), "MZ");
        File.WriteAllText(Path.Combine(game, "nvngx_dlss.dll"), "x");
        var msgs = await Talk(Req(1, "game.status", game), Req(2, "game.actions", game));
        var lines = Answer(msgs, 1)["result"]!["lines"]!.AsArray();
        Assert.Contains(lines, l => l!["label"]!.GetValue<string>() == "Super Resolution");
        var actions = Answer(msgs, 2)["result"]!.AsArray().Select(a => a!["id"]!.GetValue<string>()).ToList();
        Assert.Contains("update", actions);
        Assert.Contains("open", actions);
        Assert.DoesNotContain("restore", actions);
    }

    [Fact]
    public void Reverted_DetectsReplacedAndMissingDlss()
    {
        var game = TempDir("reverted");
        var ours = Path.Combine(game, "nvngx_dlss.dll");
        File.WriteAllText(ours, "ours");
        var m = new InstallManifest { RootRel = "." };
        m.Backups.Add(new BackupEntry { Original = "nvngx_dlss.dll", Backup = "nvngx_dlss.dll", Kind = "dlss", InstalledSha = HashCache.Get(ours) });
        m.AddFile("nvngx_dlssg.dll");
        File.WriteAllText(Path.Combine(game, "nvngx_dlssg.dll"), "fg");
        m.Save(game);

        Assert.Empty(AddonServer.Reverted(game));

        // A game update puts its own DLSS back and removes the frame generation file.
        File.WriteAllText(ours, "the game's own");
        File.SetLastWriteTimeUtc(ours, DateTime.UtcNow.AddMinutes(1));
        File.Delete(Path.Combine(game, "nvngx_dlssg.dll"));
        var r = AddonServer.Reverted(game);
        Assert.Contains("nvngx_dlss.dll", r);
        Assert.Contains("nvngx_dlssg.dll", r);
    }

    [Fact]
    public void Registration_ManifestMatchesProtocol()
    {
        var m = AddonRegistration.Manifest(@"C:\Apps\DLSSUpdater.exe");
        Assert.Equal("dlssupdater", m["id"]!.GetValue<string>());
        Assert.Equal(1, m["protocol"]!.GetValue<int>());
        Assert.Equal("--addon", m["args"]![0]!.GetValue<string>());
        Assert.Contains(m["hooks"]!.AsArray(), h => h!.GetValue<string>() == "game.beforeLaunch");
    }
}
