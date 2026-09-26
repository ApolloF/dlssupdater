using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Addon;

/// <summary>
/// "DLSSUpdater.exe --addon": DLSS Updater as a WaterLauncher add-on. It speaks JSON-RPC 2.0 over
/// stdin/stdout, one message per line (WaterLauncher's docs/addon-protocol.md): it shows a game's
/// DLSS and OptiScaler versions, puts DLSS back before a launch when a game update replaced it,
/// and offers updating DLSS, installing OptiScaler and restoring the original files.
/// </summary>
public sealed class AddonServer
{
    public const int Protocol = 1;
    private const int MaxLine = 1 << 20;

    private readonly TextWriter _out;
    private readonly object _writeLock = new();
    private readonly Lazy<Services> _services;
    private readonly SemaphoreSlim _storeReady = new(1, 1);
    private bool _storeRefreshed;
    private readonly CancellationTokenSource _cts = new();

    public AddonServer(TextWriter output, Func<Services>? services = null)
    {
        _out = output;
        _services = new Lazy<Services>(services ?? CreateServices);
    }

    private static Services CreateServices()
    {
        AppPaths.Ensure();
        var settings = AppSettings.Load();
        var gh = new GitHubClient { Token = settings.GitHubToken };
        var store = new ComponentStore(gh, () => settings.IncludePrereleases);
        return new Services { Settings = settings, Store = store, Installer = new Installer(store) };
    }

    /// <summary>Serves requests until stdin closes or WaterLauncher sends "shutdown".</summary>
    public async Task RunAsync(TextReader input)
    {
        var running = new List<Task>();
        while (!_cts.IsCancellationRequested)
        {
            var line = await input.ReadLineAsync();
            if (line is null) break;
            if (line.Length > MaxLine || string.IsNullOrWhiteSpace(line)) continue;
            JsonNode? msg;
            try { msg = JsonNode.Parse(line); }
            catch (JsonException) { Send(Error(null, -32700, "not valid JSON")); continue; }
            if (msg is not JsonObject req || req["method"]?.GetValue<string>() is not { } method) continue;
            var id = req["id"]?.DeepClone();
            var p = req["params"] as JsonObject ?? new JsonObject();
            if (method == "shutdown") break;
            running.Add(Task.Run(() => HandleAsync(id, method, p)));
            running.RemoveAll(t => t.IsCompleted);
        }
        _cts.Cancel();
        await Task.WhenAll(running.Select(t => t.ContinueWith(_ => { })));
    }

    private async Task HandleAsync(JsonNode? id, string method, JsonObject p)
    {
        try
        {
            var result = await CallAsync(method, p, _cts.Token);
            if (id is not null) Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });
        }
        catch (AddonException ex)
        {
            if (id is not null) Send(Error(id, ex.Code, ex.Message));
        }
        catch (NeedsAdminException)
        {
            if (id is not null) Send(Error(id, -32000, "This game's folder needs administrator rights. Open DLSS Updater as administrator to change it."));
        }
        catch (OperationCanceledException)
        {
            if (id is not null) Send(Error(id, -32000, "Cancelled"));
        }
        catch (Exception ex)
        {
            Log.Error($"Add-on {method}", ex);
            if (id is not null) Send(Error(id, -32000, ex.Message));
        }
    }

    internal async Task<JsonNode?> CallAsync(string method, JsonObject p, CancellationToken ct)
    {
        switch (method)
        {
            case "initialize":
                if (p["protocol"]?.GetValue<int>() is { } v && v != Protocol)
                    throw new AddonException(-32000, $"DLSS Updater speaks add-on protocol {Protocol}, not {v}");
                return new JsonObject
                {
                    ["protocol"] = Protocol,
                    ["name"] = "DLSS Updater",
                    ["version"] = AppVersion,
                };
            case "game.status":
                return Status(Game(p));
            case "game.actions":
                return Actions(Game(p));
            case "game.runAction":
                return await RunActionAsync(Game(p), p["action"]?.GetValue<string>() ?? "", ct);
            case "game.beforeLaunch":
                return await BeforeLaunchAsync(Game(p), ct);
            case "library.gameAdded":
                return null;
        }
        throw new AddonException(-32601, "unknown method " + method);
    }

    internal static string AppVersion =>
        typeof(AddonServer).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}" : "0";

    // ---------- the game WaterLauncher asks about ----------

    private static GameInfo Game(JsonObject p)
    {
        if (p["game"] is not JsonObject g || g["dir"]?.GetValue<string>() is not { Length: > 0 } dir)
            throw new AddonException(-32602, "the game has no folder");
        if (!Path.IsPathFullyQualified(dir) || !Directory.Exists(dir))
            throw new AddonException(-32000, "The game's folder wasn't found");
        var title = g["title"]?.GetValue<string>() ?? Path.GetFileName(dir);
        return GameScanner.Inspect(new GameEntry(title, dir, "WaterLauncher"));
    }

    private GameViewModel Model(GameInfo info) => new(info, _services.Value);

    /// <summary>Loads cached release info (and refreshes it once per run) so "latest" is known.</summary>
    private async Task EnsureStoreAsync(CancellationToken ct)
    {
        await _storeReady.WaitAsync(ct);
        try
        {
            if (_storeRefreshed) return;
            _storeRefreshed = true;
            try { await _services.Value.Store.RefreshAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { Log.Error("Add-on: release check failed", ex); }
        }
        finally { _storeReady.Release(); }
    }

    /// <summary>DLSS files DLSS Updater put in that a game update has since replaced or removed.</summary>
    internal static List<string> Reverted(string targetDir)
    {
        var m = InstallManifest.Load(targetDir);
        if (m is null) return [];
        var root = FileUtil.Normalize(Path.Combine(targetDir, m.RootRel));
        var dlssNames = ComponentStore.DlssFiles;
        var out_ = new List<string>();
        foreach (var b in m.Backups.Where(b => b.Kind == "dlss" && b.InstalledSha is not null))
        {
            var path = Path.Combine(root, b.Original);
            if (!File.Exists(path) || !string.Equals(HashCache.Get(path), b.InstalledSha, StringComparison.OrdinalIgnoreCase))
                out_.Add(b.Original);
        }
        foreach (var rel in m.Files.Where(f => dlssNames.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)))
            if (!File.Exists(Path.Combine(root, rel)) && !out_.Contains(rel, StringComparer.OrdinalIgnoreCase))
                out_.Add(rel);
        return out_;
    }

    private static string Short(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";

    private JsonObject Status(GameInfo info)
    {
        var badges = new JsonArray();
        var lines = new JsonArray();
        var reverted = info.Installs.SelectMany(Reverted).ToList();
        var m = info.Installs.Select(InstallManifest.Load).FirstOrDefault(x => x is not null);

        if (info.Dlss.Select(d => d.Version).Where(v => v is not null).Max() is { } top)
            badges.Add(Badge($"DLSS {Short(top)}", "info"));
        if (reverted.Count > 0)
            badges.Add(Badge("DLSS reverted by a game update", "warn", "DLSS Updater puts it back before the game starts"));
        if (m?.Opti == true)
            badges.Add(Badge(m.OptiTag is { } t ? $"OptiScaler {t}" : "OptiScaler", "ok"));

        foreach (var group in info.Dlss.GroupBy(d => d.Kind))
        {
            var label = group.Key switch { "SR" => "Super Resolution", "RR" => "Ray Reconstruction", "FG" => "Frame Generation", _ => group.Key };
            var ver = group.Select(d => d.Version).Where(v => v is not null).Max();
            lines.Add(Line(label, ver is null ? "present" : Short(ver)));
        }
        if (info.Dlss.Count > 0 || m is not null)
            lines.Add(Line("OptiScaler", m?.Opti == true ? m.OptiTag ?? "installed" : "not installed"));
        if (info.AntiCheat is { } ac) lines.Add(Line("Anti-cheat", ac));
        return new JsonObject { ["badges"] = badges, ["lines"] = lines };
    }

    private static JsonArray Actions(GameInfo info)
    {
        var actions = new JsonArray();
        var m = info.Installs.Select(InstallManifest.Load).FirstOrDefault(x => x is not null);
        if (info.Dlss.Count > 0)
            actions.Add(Action("update", m?.Dlss == true ? "Update DLSS" : "Install latest DLSS", "Replace the game's DLSS files with the newest ones"));
        if (m?.Opti != true && info.AntiCheat is null && info.Exes.Count > 0)
            actions.Add(Action("opti", "Install OptiScaler", "Install OptiScaler with your DLSS Updater settings",
                "Install OptiScaler and the components enabled in DLSS Updater into this game?"));
        if (m?.Backups.Any(b => b.Kind is "dlss" or "streamline") == true)
            actions.Add(Action("restore", "Restore original DLSS", "Put back the DLSS files the game shipped with",
                "Put back the DLSS files the game shipped with?"));
        actions.Add(Action("open", "Open DLSS Updater", "All settings and components"));
        return actions;
    }

    private async Task<JsonObject> RunActionAsync(GameInfo info, string action, CancellationToken ct)
    {
        var s = _services.Value;
        switch (action)
        {
            case "open":
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false });
                return Message("");
            case "update":
            case "opti":
            {
                await EnsureStoreAsync(ct);
                var vm = Model(info);
                var target = vm.SelectedTarget?.Dir ?? throw new AddonException(-32000, "No folder to install into");
                var o = vm.BuildOptions(dlssOnly: action == "update", acConfirmed: vm.Manifest?.AntiCheatConfirmed == true);
                if (vm.HasAntiCheat && vm.NeedsInjection(o) && !o.AntiCheatConfirmed)
                    throw new AddonException(-32000, $"{info.Name} uses {info.AntiCheat}. Confirm the risk in DLSS Updater first.");
                Progress(action == "update" ? "Installing DLSS…" : "Installing OptiScaler…");
                await s.Installer.InstallAsync(info, target, o, null, ct);
                return Message(action == "update" ? "DLSS is up to date" : "OptiScaler is installed");
            }
            case "restore":
            {
                var target = info.Installs.FirstOrDefault() ?? throw new AddonException(-32000, "DLSS Updater hasn't changed this game");
                await s.Installer.RestoreDlssAsync(info, target, ct);
                return Message("The game's original DLSS files are back");
            }
        }
        throw new AddonException(-32602, "unknown action " + action);
    }

    private async Task<JsonObject> BeforeLaunchAsync(GameInfo info, CancellationToken ct)
    {
        var fixedAny = false;
        foreach (var target in info.Installs)
        {
            var reverted = Reverted(target);
            if (reverted.Count == 0) continue;
            var m = InstallManifest.Load(target)!;
            Progress($"A game update replaced {string.Join(", ", reverted.Select(Path.GetFileName).Distinct())}; putting DLSS back");
            await EnsureStoreAsync(ct);
            var vm = Model(info);
            var o = vm.UpdateOptions(m);
            if (vm.HasAntiCheat && vm.NeedsInjection(o) && !m.AntiCheatConfirmed)
                throw new AddonException(-32000, $"{info.Name} uses {info.AntiCheat}; update it in DLSS Updater");
            await _services.Value.Installer.InstallAsync(info, target, o, null, ct);
            Log.Info($"{info.Name}: DLSS put back after a game update (WaterLauncher)");
            fixedAny = true;
        }
        return Message(fixedAny ? "DLSS is back after a game update" : "");
    }

    // ---------- messages ----------

    private void Progress(string text) =>
        Send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "progress", ["params"] = new JsonObject { ["text"] = text } });

    private void Send(JsonNode msg)
    {
        var line = msg.ToJsonString();
        lock (_writeLock)
        {
            _out.Write(line);
            _out.Write('\n');
            _out.Flush();
        }
    }

    private static JsonObject Error(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id ?? JsonValue.Create((string?)null),
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    };

    private static JsonObject Message(string text) => text.Length == 0 ? new JsonObject() : new JsonObject { ["message"] = text };

    private static JsonObject Badge(string text, string tone, string? tooltip = null)
    {
        var b = new JsonObject { ["text"] = text, ["tone"] = tone };
        if (tooltip is not null) b["tooltip"] = tooltip;
        return b;
    }

    private static JsonObject Line(string label, string value) => new() { ["label"] = label, ["value"] = value };

    private static JsonObject Action(string id, string label, string description, string? confirm = null)
    {
        var a = new JsonObject { ["id"] = id, ["label"] = label, ["description"] = description };
        if (confirm is not null) a["confirm"] = confirm;
        return a;
    }

    /// <summary>Runs the add-on on the process's stdin and stdout.</summary>
    public static async Task RunStdioAsync()
    {
        var utf8 = new UTF8Encoding(false);
        using var input = new StreamReader(Console.OpenStandardInput(), utf8);
        using var output = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = false };
        await new AddonServer(output).RunAsync(input);
    }
}

/// <summary>An error for WaterLauncher to show; the message is written for people.</summary>
public sealed class AddonException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}
