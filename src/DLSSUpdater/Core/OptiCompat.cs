namespace DLSSUpdater.Core;

public sealed record CompatReport(string Tag, IReadOnlyList<string> MissingKeys, bool NewerThanTested)
{
    public bool HasIssues => MissingKeys.Count > 0 || NewerThanTested;

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (NewerThanTested)
                parts.Add($"OptiScaler-NR {Tag} is a newer major line than this app was tested with ({OptiCompat.TestedTag}).");
            if (MissingKeys.Count > 0)
                parts.Add($"{MissingKeys.Count} setting(s) this app writes no longer exist in its OptiScaler.ini and would be ignored or appended " +
                          "as unknown keys:\n  " + string.Join("\n  ", MissingKeys.Take(12)) + (MissingKeys.Count > 12 ? "\n  …" : ""));
            return parts.Count == 0 ? $"OptiScaler-NR {Tag}: config compatible." : string.Join("\n\n", parts);
        }
    }
}

/// <summary>
/// Compares a release's OptiScaler.ini with the keys this app manages, before anything is installed.
/// Only the ini is fetched (~60 KB, straight from the tag), not the 130 MB package.
/// </summary>
public static class OptiCompat
{
    /// <summary>Newest OptiScaler-NR release the settings in this app were written against.</summary>
    public const string TestedTag = "v0.8.91";

    public static async Task<CompatReport> CheckAsync(GitHubClient gh, string tag, IEnumerable<(string Section, string Key)> managed, CancellationToken ct)
    {
        var cached = Path.Combine(AppPaths.Cache, "optiscaler", $"ini-{string.Concat(tag.Where(char.IsLetterOrDigit))}.ini");
        if (!File.Exists(cached))
            await gh.DownloadAsync($"https://raw.githubusercontent.com/{ComponentStore.OptiRepo}/{tag}/OptiScaler.ini", cached, "OptiScaler.ini", null, ct);
        var ini = IniFile.Load(cached);

        var missing = managed
            .Where(k => !string.IsNullOrWhiteSpace(k.Section) && !string.IsNullOrWhiteSpace(k.Key))
            .Distinct()
            .Where(k => ini.Get(k.Section, k.Key) is null)
            .Select(k => $"[{k.Section}] {k.Key}")
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var v = FileUtil.ParseTag(tag);
        var tested = FileUtil.ParseTag(TestedTag)!;
        var newer = v is not null && (v.Major > tested.Major || (v.Major == tested.Major && v.Minor > tested.Minor));
        return new CompatReport(tag, missing, newer);
    }
}
