using System.Text.RegularExpressions;

namespace DLSSUpdater.Core;

/// <summary>A few plain-text lines distilled from a release's markdown notes.</summary>
public sealed record ReleaseSummary(string? Headline, IReadOnlyList<string> Points, bool More)
{
    public static readonly ReleaseSummary Empty = new(null, [], false);
    public bool IsEmpty => Headline is null && Points.Count == 0;
}

/// <summary>Short "what's new" from release notes, and which releases the user has already looked at.</summary>
public static partial class ReleaseNotes
{
    private const int MaxPoints = 4;
    private const int MaxPointLength = 160;
    private const int MaxParagraphLength = 280;

    /// <summary>
    /// The first heading (minus the version it usually repeats) and the first bullet points. Notes without bullets
    /// give their first paragraph instead. Callouts (&gt; [!NOTE]), code blocks and HTML are skipped.
    /// </summary>
    public static ReleaseSummary Summarize(string? markdown, string tag)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return ReleaseSummary.Empty;

        string? headline = null;
        var bullets = new List<string>();
        var paragraph = new List<string>();
        var paragraphDone = false;
        var inCode = false;
        int? listIndent = null;
        // The previous line was a bullet (or its wrapped text), and whether that bullet is a top-level point.
        var inBullet = false;
        var topLevel = false;

        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                inCode = !inCode;
                continue;
            }
            if (inCode || line.StartsWith('>') || line.StartsWith('<') || line.StartsWith('|')) continue;

            if (Heading().Match(line) is { Success: true } h)
            {
                // A heading that only repeats the version leaves room for the next one.
                headline ??= NullIfEmpty(StripVersion(Inline(h.Groups[1].Value), tag));
                listIndent = null;
                inBullet = false;
                if (paragraph.Count > 0) paragraphDone = true;
                continue;
            }
            if (line.Length == 0 || HorizontalRule().IsMatch(line))
            {
                inBullet = false;
                if (paragraph.Count > 0) paragraphDone = true;
                continue;
            }
            if (Bullet().Match(raw) is { Success: true } b)
            {
                // Top-level points only; nested ones are detail. Some notes indent the whole list.
                var indent = b.Groups[1].Value.Replace("\t", "    ").Length;
                listIndent ??= indent;
                topLevel = indent <= listIndent;
                if (topLevel) bullets.Add(Inline(b.Groups[2].Value));
                inBullet = true;
                continue;
            }
            if (inBullet)
            {
                // Wrapped text of the bullet above.
                if (topLevel) bullets[^1] += " " + Inline(line);
                continue;
            }
            // An indented paragraph inside a list item belongs to that item.
            if (listIndent is not null && raw.Length - raw.TrimStart().Length > listIndent) continue;
            listIndent = null;
            if (!paragraphDone) paragraph.Add(Inline(line));
        }

        bullets.RemoveAll(string.IsNullOrWhiteSpace);
        if (bullets.Count > 0)
            return new ReleaseSummary(NullIfEmpty(headline), bullets.Take(MaxPoints).Select(p => Shorten(p, MaxPointLength)).ToList(), bullets.Count > MaxPoints);

        var text = string.Join(" ", paragraph);
        // A paragraph that only announces the release ("SDK 1.2 is now available") says nothing new.
        if (text.Length == 0 || (headline is null && text.Contains("now available", StringComparison.OrdinalIgnoreCase)))
            return headline is null ? ReleaseSummary.Empty : new ReleaseSummary(NullIfEmpty(headline), [], false);
        return new ReleaseSummary(NullIfEmpty(headline), [Shorten(text, MaxParagraphLength)], text.Length > MaxParagraphLength);
    }

    /// <summary>
    /// True when <paramref name="tag"/> is newer than the last release of <paramref name="c"/> the user looked at.
    /// The first release seen for a component is recorded as seen, so only releases that arrive later are flagged.
    /// </summary>
    public static bool IsUnseen(IDictionary<string, string> seen, Component c, string tag)
    {
        var key = c.ToString();
        if (!seen.TryGetValue(key, out var last))
        {
            seen[key] = tag;
            return false;
        }
        if (string.Equals(last, tag, StringComparison.OrdinalIgnoreCase)) return false;
        // Same number with another tag is news too: "v0.9.0-pre2" -> "v0.9.0", or "-pre1" -> "-pre2".
        return FileUtil.ParseTag(tag) is not { } now || FileUtil.ParseTag(last) is not { } before || now >= before;
    }

    public static void MarkSeen(IDictionary<string, string> seen, Component c, string tag) => seen[c.ToString()] = tag;

    /// <summary>True the first time <paramref name="tag"/> of <paramref name="c"/> is announced, so a release is logged once and not on every start.</summary>
    public static bool Announce(IDictionary<string, string> announced, Component c, string tag)
    {
        var key = c.ToString();
        if (announced.TryGetValue(key, out var last) && string.Equals(last, tag, StringComparison.OrdinalIgnoreCase)) return false;
        announced[key] = tag;
        return true;
    }

    /// <summary>Where the full notes of a release live.</summary>
    public static string Url(Component c, string tag) => c switch
    {
        Component.OptiScaler => GitHubTag(ComponentStore.OptiRepo, tag),
        Component.MfgUnlock => GitHubTag(ComponentStore.MfgRepo, tag),
        Component.Streamline => GitHubTag(ComponentStore.StreamlineRepo, tag),
        Component.ReShade => ComponentStore.ReShadeSite + "/releases",
        _ => GitHubTag(ComponentStore.DlssRepo, tag),
    };

    private static string GitHubTag(string repo, string tag) => $"https://github.com/{repo}/releases/tag/{Uri.EscapeDataString(tag)}";

    /// <summary>"v0.8.91 prerelease - compressed model input preview" -> "compressed model input preview".</summary>
    private static string StripVersion(string heading, string tag)
    {
        var bare = tag.TrimStart('v', 'V');
        foreach (var prefix in new[] { tag, "v" + bare, bare })
        {
            if (!heading.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            return LeadingFiller().Replace(heading[prefix.Length..], "").Trim();
        }
        return heading;
    }

    private static string Inline(string s)
    {
        s = Image().Replace(s, "");
        s = Link().Replace(s, "$1");
        s = Emphasis().Replace(s, "$2");
        s = s.Replace("`", "");
        return Whitespace().Replace(s, " ").Trim();
    }

    private static string Shorten(string s, int max)
    {
        if (s.Length <= max) return s;
        // End on a full sentence when one ends late enough; a dot inside "v310.9.1" doesn't end one.
        for (var i = max - 1; i >= max / 2; i--)
            if (s[i] is '.' or '!' or '?' && char.IsWhiteSpace(s[i + 1])) return s[..(i + 1)];
        var cut = s[..max];
        var space = cut.LastIndexOf(' ');
        return (space > max / 2 ? cut[..space] : cut).TrimEnd(',', ';', ':', ' ') + "…";
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    [GeneratedRegex(@"^#{1,6}\s+(.+?)\s*#*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(\s*)(?:[-*+]|\d+[.)])\s+(.+)$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^(?:[-*_]\s*){3,}$")]
    private static partial Regex HorizontalRule();

    [GeneratedRegex(@"^\s*(?:pre-?release|release)?\s*[-–—:·|]?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingFiller();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"(?<!\w)(\*\*|__|\*|_)(\S(?:.*?\S)?)\1(?!\w)")]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
