using System.Text.Json.Serialization;

namespace DLSSUpdater.Core;

public sealed class IniOverride
{
    public string Section { get; set; } = "";
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";

    public IniOverride() { }
    public IniOverride(string section, string key, string value) => (Section, Key, Value) = (section, key, value);

    [JsonIgnore]
    public string Id => $"{Section.Trim()}/{Key.Trim()}";
}

public sealed record MergeResult(string Text, Dictionary<string, string> Applied);

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

    /// <summary>ReShade.ini values written on install; ReShade fills in everything else on first launch.</summary>
    public static List<IniOverride> ReShadeDefaults() =>
    [
        new("OVERLAY", "TutorialProgress", "4"),
    ];

    private static bool IsAuto(string? v) => v is null || v.Equals("auto", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Three-way merge. The base ini (a release's OptiScaler.ini, or the game's own ReShade.ini) gets
    /// the game's current non-auto values, then the profile. A profile value only replaces a current value
    /// that we wrote ourselves last time (<paramref name="lastApplied"/>), so settings a user changed
    /// in-game or in a hand-made config are never overwritten, while profile edits in the app still roll out.
    /// </summary>
    public static MergeResult Merge(string baseIni, string? currentIni, IEnumerable<IniOverride> overrides, bool carryOver,
        IReadOnlyDictionary<string, string>? lastApplied = null)
    {
        var ini = IniFile.Parse(baseIni);
        var pristine = IniFile.Parse(baseIni);
        var current = currentIni is null ? null : IniFile.Parse(currentIni);
        var applied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        lastApplied ??= new Dictionary<string, string>();

        if (carryOver && current is not null)
        {
            foreach (var (section, key, value) in current.Entries())
                if (section.Length > 0 && !IsAuto(value)) ini.Set(section, key, value);
        }

        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in overrides)
        {
            if (string.IsNullOrWhiteSpace(o.Section) || string.IsNullOrWhiteSpace(o.Key)) continue;
            var (section, key, value) = (o.Section.Trim(), o.Key.Trim(), o.Value.Trim());
            wanted.Add(o.Id);

            var cur = current?.Get(section, key);
            var ours = lastApplied.TryGetValue(o.Id, out var prev) && string.Equals(prev, cur, StringComparison.OrdinalIgnoreCase);
            if (carryOver && !IsAuto(cur) && !ours) continue; // the game's own setting wins

            ini.Set(section, key, value);
            applied[o.Id] = value;
        }

        // Keys we used to set but the profile dropped: hand them back to the base value if nobody touched them.
        foreach (var (id, prev) in lastApplied)
        {
            if (wanted.Contains(id)) continue;
            var slash = id.IndexOf('/');
            if (slash <= 0) continue;
            var (section, key) = (id[..slash], id[(slash + 1)..]);
            if (!string.Equals(current?.Get(section, key), prev, StringComparison.OrdinalIgnoreCase)) continue;
            if (pristine.Get(section, key) is { } baseValue && !string.Equals(baseValue, prev, StringComparison.OrdinalIgnoreCase))
                ini.Set(section, key, baseValue);
        }

        return new MergeResult(ini.ToString(), applied);
    }
}
