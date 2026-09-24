using System.Text.Json;
using System.Text.Json.Serialization;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

public sealed class GameOverride
{
    public string? TargetDir { get; set; }
    public string? Proxy { get; set; }
    /// <summary>DLSS release tag pinned for this game; null follows the global choice.</summary>
    public string? DlssTag { get; set; }
    /// <summary>Config preset used for this game; null uses the current settings.</summary>
    public string? Preset { get; set; }
}

/// <summary>A named snapshot of the OptiScaler.ini and ReShade.ini settings (incl. keybinds and options).</summary>
public sealed class ConfigPreset
{
    public string Name { get; set; } = "";
    public List<IniOverride> Opti { get; set; } = [];
    public List<IniOverride> ReShade { get; set; } = [];
}

public sealed class AppSettings
{
    public static readonly string[] ProxyNames =
        ["dxgi.dll", "winmm.dll", "version.dll", "dbghelp.dll", "d3d12.dll", "wininet.dll", "winhttp.dll"];

    public List<string> ManualGames { get; set; } = [];
    public List<string> LibraryRoots { get; set; } = [];
    public List<string> HiddenGames { get; set; } = [];
    public Dictionary<string, GameOverride> Games { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string DefaultProxy { get; set; } = "dxgi.dll";
    public bool IncludePrereleases { get; set; } = true;
    public bool InstallReShade { get; set; } = true;
    public bool InstallMfgUnlock { get; set; } = true;
    public bool InstallDlssNr { get; set; } = true;
    public bool AddMissingDlss { get; set; } = true;
    /// <summary>Legacy (1.0/1.1); false maps to IniMode "fresh".</summary>
    public bool CarryOverGameIni { get; set; } = true;
    /// <summary>keep | apply | fresh — how existing game configs are treated.</summary>
    public string IniMode { get; set; } = "keep";
    public string? GitHubToken { get; set; }
    public List<IniOverride> IniOverrides { get; set; } = ConfigProfile.Defaults();
    public List<IniOverride> ReShadeOverrides { get; set; } = ConfigProfile.ReShadeDefaults();
    public bool InstallStreamline { get; set; }
    public List<ConfigPreset> Presets { get; set; } = [];

    public ConfigPreset? PresetFor(string gameId) =>
        Games.GetValueOrDefault(gameId)?.Preset is { } name ? Presets.FirstOrDefault(p => p.Name == name) : null;
    /// <summary>DLSS release tag to install; null = latest.</summary>
    public string? DlssTag { get; set; }

    public GameOverride For(string gameId)
    {
        if (!Games.TryGetValue(gameId, out var o)) Games[gameId] = o = new GameOverride();
        return o;
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var s = JsonSerializer.Deserialize(File.ReadAllText(AppPaths.SettingsFile), JsonCtx.Default.AppSettings);
                if (s is not null)
                {
                    s.Games = new Dictionary<string, GameOverride>(s.Games, StringComparer.OrdinalIgnoreCase);
                    if (!s.CarryOverGameIni && s.IniMode == "keep") s.IniMode = "fresh";
                    s.CarryOverGameIni = true;
                    return s;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Log.Error("settings.json unreadable, using defaults", ex);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try { FileUtil.AtomicWriteText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, JsonCtx.Default.AppSettings)); }
        catch (IOException ex) { Log.Error("Could not save settings", ex); }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(InstallManifest))]
[JsonSerializable(typeof(List<GameInfo>))]
[JsonSerializable(typeof(Dictionary<string, ApiCacheEntry>))]
[JsonSerializable(typeof(List<GhRelease>))]
[JsonSerializable(typeof(GhRelease))]
[JsonSerializable(typeof(CachedComponent))]
internal partial class JsonCtx : JsonSerializerContext;
