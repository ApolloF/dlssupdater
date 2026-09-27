using System.Text.Json.Nodes;
using DLSSUpdater.Core;

namespace DLSSUpdater.Addon;

/// <summary>
/// Tells Seaglass about this add-on by writing its addon.json into Seaglass's add-ons
/// folder. Seaglass keeps it off until the user turns it on (and pins this exe's hash).
/// </summary>
public static class AddonRegistration
{
    public const string Id = "dlssupdater";

    static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>
    /// Seaglass's add-ons folder. Seaglass was called WaterLauncher before 1.5; a WaterLauncher
    /// that hasn't updated yet still has its own folder (Seaglass moves it over when it first starts).
    /// </summary>
    public static string AddonsDir =>
        !Directory.Exists(Path.Combine(Local, "Seaglass")) && Directory.Exists(Path.Combine(Local, "WaterLauncher"))
            ? Path.Combine(Local, "WaterLauncher", "addons")
            : Path.Combine(Local, "Seaglass", "addons");

    public static string ManifestPath => Path.Combine(AddonsDir, Id, "addon.json");

    /// <summary>Seaglass has run on this PC (its data folder exists).</summary>
    public static bool SeaglassFound => Directory.Exists(Path.GetDirectoryName(AddonsDir)!);

    public static bool Registered => File.Exists(ManifestPath);

    internal static JsonObject Manifest(string exe) => new()
    {
        ["id"] = Id,
        ["name"] = "DLSS Updater",
        ["version"] = AddonServer.AppVersion,
        ["publisher"] = "ApolloF",
        ["description"] = "Shows each game's DLSS and OptiScaler versions, puts DLSS back when a game update replaced it, and installs DLSS and OptiScaler.",
        ["homepage"] = "https://github.com/ApolloF/dlssupdater",
        ["exe"] = exe,
        ["args"] = new JsonArray("--addon"),
        ["protocol"] = AddonServer.Protocol,
        ["hooks"] = new JsonArray("game.status", "game.actions", "game.beforeLaunch"),
        ["permissions"] = new JsonArray("modifyGameFiles", "network"),
    };

    /// <summary>Writes (or refreshes) addon.json for this exe.</summary>
    public static void Register()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Can't tell where DLSS Updater is");
        Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
        FileUtil.AtomicWriteText(ManifestPath, Manifest(exe).ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Log.Info($"Connected to Seaglass ({ManifestPath})");
    }

    /// <summary>Unregisters only when addon.json launches this exe, so uninstalling one copy never disconnects another.</summary>
    public static void UnregisterIfThisExe()
    {
        if (!Registered || Environment.ProcessPath is not { } exe) return;
        var registered = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(ManifestPath))?["exe"]?.GetValue<string>();
        if (registered is not null && string.Equals(Path.GetFullPath(registered), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase))
            Unregister();
    }

    public static void Unregister()
    {
        var dir = Path.GetDirectoryName(ManifestPath)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Log.Info("Disconnected from Seaglass");
    }
}
