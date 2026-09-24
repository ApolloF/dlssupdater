namespace DLSSUpdater.Core;

public sealed class IniOverride
{
    public string Section { get; set; } = "";
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";

    public IniOverride() { }
    public IniOverride(string section, string key, string value) => (Section, Key, Value) = (section, key, value);
}

public static class ConfigProfile
{
    /// <summary>Values that differ from "auto" in the reference OptiScaler.ini this tool was built around.</summary>
    public static List<IniOverride> Defaults() =>
    [
        new("Upscalers", "Dx12Upscaler", "dlss"),
        new("DLSS", "RenderPresetOverride", "true"),
        new("DLSS", "RenderPresetForAll", "12"),
        new("Menu", "ShortcutKey", "0x2e"),
        new("Plugins", "LoadReshade", "true"),
        new("Log", "LogToFile", "true"),
        new("Log", "LogLevel", "2"),
        new("DlssNr", "Enabled", "true"),
        new("DlssNr", "RunBeforeSR", "true"),
        new("DlssNr", "ColourStrength", "0.200000"),
        new("DlssNr", "LocalStructure", "0.700000"),
        new("DlssNr", "LocalTone", "0.250000"),
        new("DlssNr", "SkinStructure", "0.500000"),
        new("DlssNr", "ToggleKey", "0xdc"),
        new("DlssNr", "WhitePointSource", "3"),
        new("DlssNr", "ReversibleMode", "3"),
    ];

    /// <summary>
    /// Builds the ini for a game: the release ini is the base (so new keys and comments arrive),
    /// the profile is applied on top, then every non-auto value from the game's current ini wins,
    /// so an update never changes settings the game already had.
    /// </summary>
    public static string Merge(string releaseIni, string? currentIni, IEnumerable<IniOverride> overrides, bool carryOver)
    {
        var ini = IniFile.Parse(releaseIni);

        foreach (var o in overrides)
        {
            if (string.IsNullOrWhiteSpace(o.Section) || string.IsNullOrWhiteSpace(o.Key)) continue;
            ini.Set(o.Section.Trim(), o.Key.Trim(), o.Value.Trim());
        }

        if (carryOver && currentIni is not null)
        {
            foreach (var (section, key, value) in IniFile.Parse(currentIni).Entries())
            {
                if (section.Length == 0 || value.Equals("auto", StringComparison.OrdinalIgnoreCase)) continue;
                ini.Set(section, key, value);
            }
        }

        return ini.ToString();
    }
}
