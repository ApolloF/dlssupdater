using System.Text;
using System.Text.RegularExpressions;

namespace DLSSUpdater.Core;

/// <summary>
/// Line-preserving INI document. Comments, ordering and spacing survive a round trip;
/// only the values that are set are rewritten.
/// </summary>
public sealed partial class IniFile
{
    private readonly List<string> _lines;
    private readonly string _newline;

    private IniFile(List<string> lines, string newline)
    {
        _lines = lines;
        _newline = newline;
    }

    public static IniFile Parse(string text)
    {
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return new IniFile(lines, newline);
    }

    public static IniFile Load(string path) => Parse(File.ReadAllText(path));

    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var l in _lines) sb.Append(l).Append(_newline);
        return sb.ToString();
    }

    public void Save(string path) => FileUtil.AtomicWriteText(path, ToString());

    public IEnumerable<(string Section, string Key, string Value)> Entries()
    {
        var section = "";
        foreach (var raw in _lines)
        {
            if (TryParseSection(raw, out var s)) { section = s; continue; }
            if (TryParseKey(raw, out var k, out var v)) yield return (section, k, v);
        }
    }

    public string? Get(string section, string key)
    {
        var idx = FindKey(section, key);
        return idx < 0 ? null : (TryParseKey(_lines[idx], out _, out var v) ? v : null);
    }

    public void Set(string section, string key, string value)
    {
        var idx = FindKey(section, key);
        if (idx >= 0)
        {
            var m = KeyLine().Match(_lines[idx]);
            _lines[idx] = m.Groups["head"].Value + value;
            return;
        }

        var (start, end) = FindSection(section);
        if (start < 0)
        {
            if (_lines.Count > 0 && _lines[^1].Trim().Length > 0) _lines.Add("");
            _lines.Add($"[{section}]");
            _lines.Add($"{key}={value}");
            return;
        }

        // Insert after the last key line in the section so trailing comment blocks stay attached to the next section.
        var insertAt = start + 1;
        for (var i = start + 1; i < end; i++)
            if (TryParseKey(_lines[i], out _, out _)) insertAt = i + 1;
        _lines.Insert(insertAt, $"{key}={value}");
    }

    private int FindKey(string section, string key)
    {
        var (start, end) = FindSection(section);
        if (start < 0) return -1;
        for (var i = start + 1; i < end; i++)
            if (TryParseKey(_lines[i], out var k, out _) && k.Equals(key, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private (int Start, int End) FindSection(string section)
    {
        var start = -1;
        for (var i = 0; i < _lines.Count; i++)
        {
            if (!TryParseSection(_lines[i], out var s)) continue;
            if (start >= 0) return (start, i);
            if (s.Equals(section, StringComparison.OrdinalIgnoreCase)) start = i;
        }
        return (start, _lines.Count);
    }

    private static bool TryParseSection(string line, out string section)
    {
        var t = line.Trim();
        if (t.Length > 2 && t[0] == '[' && t[^1] == ']')
        {
            section = t[1..^1].Trim();
            return true;
        }
        section = "";
        return false;
    }

    private static bool TryParseKey(string line, out string key, out string value)
    {
        key = value = "";
        var t = line.TrimStart();
        if (t.Length == 0 || t[0] is ';' or '#' or '[') return false;
        var m = KeyLine().Match(line);
        if (!m.Success) return false;
        key = m.Groups["key"].Value.Trim();
        value = m.Groups["value"].Value.Trim();
        return key.Length > 0;
    }

    [GeneratedRegex(@"^(?<head>\s*(?<key>[^=;#\[]+?)\s*=\s*)(?<value>.*)$")]
    private static partial Regex KeyLine();
}
