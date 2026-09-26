using System.Text.Json.Nodes;
using DLSSUpdater.Core;

namespace DLSSUpdater.Addon;

/// <summary>
/// Tells WaterLauncher about this add-on by writing its addon.json into WaterLauncher's add-ons
/// folder. WaterLauncher keeps it off until the user turns it on (and pins this exe's hash).
/// </summary>
public static class AddonRegistration
{
    public const string Id = "dlssupdater";

    public static string AddonsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WaterLauncher", "addons");

    public static string ManifestPath => Path.Combine(AddonsDir, Id, "addon.json");

    /// <summary>WaterLauncher has run on this PC (its data folder exists).</summary>
    public static bool WaterLauncherFound => Directory.Exists(Path.GetDirectoryName(AddonsDir)!);

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
        Log.Info($"Connected to WaterLauncher ({ManifestPath})");
    }

    public static void Unregister()
    {
        var dir = Path.GetDirectoryName(ManifestPath)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Log.Info("Disconnected from WaterLauncher");
    }
}
