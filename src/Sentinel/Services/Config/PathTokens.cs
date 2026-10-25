using Sentinel.Models;
using Sentinel.Services.Games;

namespace Sentinel.Services.Config;

/// <summary>Expands {tokens} and '*' wildcard segments in config path templates.</summary>
public static class PathTokens
{
    public static IEnumerable<string> Expand(string template, InstalledGame game)
    {
        var bases = new List<string> { template };

        Replace("{localappdata}", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        Replace("{appdata}", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        Replace("{documents}", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Replace("{userprofile}", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        Replace("{savedgames}", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games"));
        Replace("{programdata}", Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        if (template.Contains("{install}"))
        {
            if (game.InstallPath is null) return [];
            Replace("{install}", game.InstallPath);
        }
        if (template.Contains("{steamuserdata}"))
            bases = bases.SelectMany(b => GameScanner.SteamUserDataDirs().Select(d => b.Replace("{steamuserdata}", d))).ToList();

        return bases.SelectMany(ExpandWildcards).Distinct(StringComparer.OrdinalIgnoreCase);

        void Replace(string token, string value)
        {
            for (int i = 0; i < bases.Count; i++) bases[i] = bases[i].Replace(token, value, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static IEnumerable<string> ExpandWildcards(string path)
    {
        int star = path.IndexOf('*');
        if (star < 0) return [path];

        int segStart = path.LastIndexOf('\\', star) + 1;
        int segEnd = path.IndexOf('\\', star);
        string parent = path[..Math.Max(segStart - 1, 0)];
        string pattern = segEnd < 0 ? path[segStart..] : path[segStart..segEnd];
        string rest = segEnd < 0 ? "" : path[segEnd..];
        if (!Directory.Exists(parent)) return [];

        try
        {
            var matches = rest.Length == 0
                ? Directory.EnumerateFileSystemEntries(parent, pattern)
                : Directory.EnumerateDirectories(parent, pattern);
            return matches.SelectMany(m => ExpandWildcards(m + rest)).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }
}
