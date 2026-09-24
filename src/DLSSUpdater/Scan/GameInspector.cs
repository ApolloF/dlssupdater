using System.IO.Enumeration;
using System.Text.RegularExpressions;
using DLSSUpdater.Core;

namespace DLSSUpdater.Scan;

/// <summary>Walks one game folder once and collects exes, DLSS dlls, anti-cheat markers and our manifests.</summary>
public static partial class GameInspector
{
    private const int MaxDepth = 8;

    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "_CommonRedist", "CommonRedist", "Redist", "Redistributables", "redistributable", "DirectX", "DirectX9",
        "__Installer", "Installers", "vcredist", "DotNetFX", "dotnet", "PhysX", "$RECYCLE.BIN",
        "__overlay", "ShaderCache", "Movies", "Videos", "Localization",
        InstallManifest.DirName,
    };

    private static readonly Dictionary<string, string> AntiCheatDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EasyAntiCheat"] = "EasyAntiCheat",
        ["EasyAntiCheat_EOS"] = "EasyAntiCheat",
        ["BattlEye"] = "BattlEye",
        ["EAAntiCheat"] = "EA Javelin",
        ["GameGuard"] = "nProtect GameGuard",
        ["XIGNCODE"] = "XIGNCODE3",
        ["nProtect"] = "nProtect GameGuard",
    };

    private static readonly Dictionary<string, string> AntiCheatFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["start_protected_game.exe"] = "EasyAntiCheat",
        ["EasyAntiCheat_EOS_Setup.exe"] = "EasyAntiCheat",
        ["EasyAntiCheat_Setup.exe"] = "EasyAntiCheat",
        ["EasyAntiCheat_x64.dll"] = "EasyAntiCheat",
        ["BEService.exe"] = "BattlEye",
        ["BEService_x64.exe"] = "BattlEye",
        ["BEClient_x64.dll"] = "BattlEye",
        ["BELauncher.exe"] = "BattlEye",
        ["EAAntiCheat.GameServiceLauncher.exe"] = "EA Javelin",
        ["EAAntiCheat.Installer.exe"] = "EA Javelin",
        ["GameGuard.des"] = "nProtect GameGuard",
        ["xhunter1.sys"] = "XIGNCODE3",
        ["PnkBstrA.exe"] = "PunkBuster",
        ["mhyprot2.sys"] = "mhyprot",
        ["vgk.sys"] = "Vanguard",
    };

    private enum HitKind { None, Exe, Dlss, AntiCheat, Manifest }

    private readonly record struct Hit(HitKind Kind, string Path, long Size, string? Tag);

    public static void Inspect(GameInfo game)
    {
        var root = FileUtil.Normalize(game.Root);
        var rootLen = root.Length;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Offline,
            ReturnSpecialDirectories = false,
        };

        var hits = new FileSystemEnumerable<Hit>(root, Transform, options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry e) => Classify(ref e).Kind != HitKind.None,
            ShouldRecursePredicate = (ref FileSystemEntry e) =>
            {
                if (SkipDirs.Contains(e.FileName.ToString())) return false;
                var rel = e.Directory.Length > rootLen ? e.Directory[rootLen..] : ReadOnlySpan<char>.Empty;
                return rel.Count('\\') < MaxDepth;
            },
        };

        var exes = new List<(string Path, long Size)>();
        game.Dlss = [];
        game.Installs = [];
        game.AntiCheat = null;

        foreach (var h in hits)
        {
            switch (h.Kind)
            {
                case HitKind.Exe: exes.Add((h.Path, h.Size)); break;
                case HitKind.Dlss:
                    game.Dlss.Add(new DlssDll { Path = h.Path, Name = Path.GetFileName(h.Path), Version = FileUtil.ReadVersion(h.Path) });
                    break;
                case HitKind.AntiCheat: game.AntiCheat ??= h.Tag; break;
                case HitKind.Manifest:
                    var dir = Path.GetDirectoryName(h.Path)!;
                    if (File.Exists(InstallManifest.PathFor(dir))) game.Installs.Add(dir);
                    break;
            }
        }

        game.Dlss = game.Dlss.OrderBy(d => d.Path, StringComparer.OrdinalIgnoreCase).ToList();
        game.Exes = RankExes(root, exes, game.Name);
        game.Scanned = DateTime.UtcNow;
    }

    private static Hit Transform(ref FileSystemEntry e)
    {
        var (kind, tag) = Classify(ref e);
        return new Hit(kind, e.ToFullPath(), e.IsDirectory ? 0 : e.Length, tag);
    }

    private static (HitKind Kind, string? Tag) Classify(ref FileSystemEntry e)
    {
        var name = e.FileName;
        if (e.IsDirectory)
        {
            if (name.Equals(InstallManifest.DirName, StringComparison.OrdinalIgnoreCase)) return (HitKind.Manifest, null);
            return AntiCheatDirs.TryGetValue(name.ToString(), out var ac) ? (HitKind.AntiCheat, ac) : (HitKind.None, null);
        }

        if (name.StartsWith("nvngx_dlss", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var d in ComponentStore.DlssFiles)
                if (name.Equals(d, StringComparison.OrdinalIgnoreCase)) return (HitKind.Dlss, null);
            return (HitKind.None, null);
        }

        var ext = Path.GetExtension(name);
        var interestingExt = ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                             ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                             ext.Equals(".sys", StringComparison.OrdinalIgnoreCase) ||
                             ext.Equals(".des", StringComparison.OrdinalIgnoreCase);
        if (!interestingExt) return (HitKind.None, null);

        var s = name.ToString();
        if (AntiCheatFiles.TryGetValue(s, out var tag)) return (HitKind.AntiCheat, tag);
        return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ? (HitKind.Exe, null) : (HitKind.None, null);
    }

    // ---------- exe ranking ----------

    [GeneratedRegex(@"^(unins\d*|setup|dxsetup|dxwebsetup|vc_?redist.*|dotnet.*|.*crash.*|.*report.*|.*uninstall.*|.*updater?|.*installer.*|.*prereq.*|.*redist.*|unitycrashhandler\d*|.*cefsubprocess|.*webhelper|shadercompileworker|.*helper|touchup|cleanup|activation.*|quicksfv|7za?|.*benchmark.*|.*_be|.*_eac|.*server|.*dedicated.*)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex ExcludedExe();

    [GeneratedRegex(@"-(Win64|WinGDK)-Shipping$", RegexOptions.IgnoreCase)]
    private static partial Regex UnrealShipping();

    internal static List<string> RankExes(string root, IEnumerable<(string Path, long Size)> exes, string? gameName = null)
    {
        var names = new[] { Alnum(Path.GetFileName(root)), Alnum(gameName ?? "") }.Where(n => n.Length >= 3).ToArray();
        return exes
            .Where(e => !ExcludedExe().IsMatch(Path.GetFileNameWithoutExtension(e.Path)))
            .Where(e => !IsUnrealEngineFolder(root, e.Path))
            .Select(e => (e.Path, Score: Score(root, e.Path, e.Size, names)))
            .OrderByDescending(e => e.Score)
            .Select(e => e.Path)
            .ToList();
    }

    private static bool IsUnrealEngineFolder(string root, string path)
    {
        var rel = Path.GetRelativePath(root, path);
        return rel.StartsWith("Engine\\", StringComparison.OrdinalIgnoreCase);
    }

    private static string Alnum(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static double Score(string root, string path, long size, string[] gameNames)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var alnum = Alnum(name);
        double nameScore = 0;
        foreach (var g in gameNames)
        {
            if (alnum == g) nameScore = Math.Max(nameScore, 40);
            else if (alnum.Length >= 4 && (g.StartsWith(alnum) || alnum.StartsWith(g))) nameScore = Math.Max(nameScore, 20);
        }
        // Unity: the player exe sits next to <Name>_Data.
        if (Directory.Exists(Path.Combine(Path.GetDirectoryName(path)!, name + "_Data"))) nameScore += 35;
        var rel = Path.GetRelativePath(root, path);
        var depth = rel.Count(c => c == '\\');
        double score = Math.Log2(Math.Max(size, 1) / 1048576.0 + 1) * 6 - depth * 2 + nameScore;
        if (alnum.Contains("mod")) score -= 30;
        if (UnrealShipping().IsMatch(name)) score += 100;
        if (rel.Contains("\\Binaries\\Win64\\", StringComparison.OrdinalIgnoreCase) ||
            rel.Contains("\\Binaries\\WinGDK\\", StringComparison.OrdinalIgnoreCase)) score += 40;
        if (rel.StartsWith("bin\\x64\\", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("x64\\", StringComparison.OrdinalIgnoreCase)) score += 15;
        if (name.Contains("launcher", StringComparison.OrdinalIgnoreCase)) score -= 40;
        if (name.EndsWith("32", StringComparison.Ordinal) || rel.Contains("x86", StringComparison.OrdinalIgnoreCase)) score -= 20;
        if (name.Contains("editor", StringComparison.OrdinalIgnoreCase) || name.Contains("config", StringComparison.OrdinalIgnoreCase)) score -= 25;
        return score;
    }
}
