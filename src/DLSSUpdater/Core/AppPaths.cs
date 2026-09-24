namespace DLSSUpdater.Core;

public static class AppPaths
{
    public static string Root { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DLSSUpdater");

    public static string Cache => Path.Combine(Root, "cache");
    public static string Components => Path.Combine(Root, "components");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string GamesFile => Path.Combine(Root, "games.json");
    public static string ApiCacheFile => Path.Combine(Cache, "api.json");
    public static string LogFile => Path.Combine(Root, "log.txt");

    public static void Ensure()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Cache);
        Directory.CreateDirectory(Components);
    }
}
