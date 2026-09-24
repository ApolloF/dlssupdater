using System.Diagnostics;
using System.Security.Cryptography;

namespace DLSSUpdater.Core;

public static class FileUtil
{
    public static Version? ReadVersion(string path)
    {
        try
        {
            var v = FileVersionInfo.GetVersionInfo(path);
            if (v.FileMajorPart == 0 && v.FileMinorPart == 0 && v.FileBuildPart == 0 && v.FilePrivatePart == 0)
                return null;
            return new Version(v.FileMajorPart, v.FileMinorPart, v.FileBuildPart, v.FilePrivatePart);
        }
        catch (FileNotFoundException) { return null; }
    }

    public static string? OriginalFilename(string path)
    {
        try { return FileVersionInfo.GetVersionInfo(path).OriginalFilename?.Trim(); }
        catch (FileNotFoundException) { return null; }
    }

    /// <summary>310.9.1.0 -> "310.9.1"</summary>
    public static string Format(Version? v)
    {
        if (v is null) return "—";
        if (v.Revision > 0) return v.ToString(4);
        return v.Build >= 0 ? v.ToString(3) : v.ToString(2);
    }

    /// <summary>Parses tags like "v310.9.1", "1.1.5", "v0.8.91".</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var s = tag.Trim().TrimStart('v', 'V');
        var end = 0;
        while (end < s.Length && (char.IsDigit(s[end]) || s[end] == '.')) end++;
        s = s[..end].Trim('.');
        if (!s.Contains('.')) s += ".0";
        return Version.TryParse(s, out var v) ? v : null;
    }

    public static string Sha256(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    public static bool SameFile(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (!fa.Exists || !fb.Exists || fa.Length != fb.Length) return false;
        if (fa.LastWriteTimeUtc == fb.LastWriteTimeUtc) return true;
        return Sha256(a) == Sha256(b);
    }

    /// <summary>Copies via a temp file next to the destination, then swaps it in. Preserves timestamps.</summary>
    public static void AtomicCopy(string src, string dest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var tmp = dest + ".dlssu-tmp";
        File.Copy(src, tmp, true);
        File.Move(tmp, dest, true);
    }

    public static void AtomicWriteText(string dest, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var tmp = dest + ".dlssu-tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, dest, true);
    }

    public static bool IsUnder(string path, string root)
    {
        var p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return p.StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

    public static void TryDeleteEmptyDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
