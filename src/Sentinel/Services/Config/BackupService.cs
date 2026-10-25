using System.Text.Json;
using Sentinel.Core;

namespace Sentinel.Services.Config;

/// <summary>
/// Copies config files to %LOCALAPPDATA%\Sentinel\Backups\&lt;game&gt;\&lt;timestamp&gt;\ before every write,
/// with a manifest so restores go back to the original locations.
/// </summary>
public static class BackupService
{
    private sealed record Manifest(DateTime Created, List<Entry> Files, Dictionary<string, string>? Registry);
    private sealed record Entry(string Original, string Stored);

    public sealed record BackupInfo(string GameId, DateTime Created, string Folder, int FileCount);

    private static string GameDir(string gameId) => Path.Combine(AppPaths.Backups, Sanitize(gameId));

    private static string Sanitize(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    /// <summary>Backs up files (and optional registry values as "path|name" → value) and returns the backup folder.</summary>
    public static string Backup(string gameId, IEnumerable<string> files, Dictionary<string, string>? registry = null)
    {
        var folder = Path.Combine(GameDir(gameId), DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(folder);
        var entries = new List<Entry>();
        int i = 0;
        foreach (var f in files.Where(File.Exists))
        {
            var stored = $"{i++:00}_{Path.GetFileName(f)}";
            File.Copy(f, Path.Combine(folder, stored), overwrite: true);
            entries.Add(new Entry(f, stored));
        }
        File.WriteAllText(Path.Combine(folder, "manifest.json"),
            JsonSerializer.Serialize(new Manifest(DateTime.Now, entries, registry), new JsonSerializerOptions { WriteIndented = true }));
        Prune(gameId, keep: 10);
        return folder;
    }

    public static IReadOnlyList<BackupInfo> List(string gameId)
    {
        var dir = GameDir(gameId);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateDirectories(dir)
            .Select(d => (Dir: d, Manifest: Read(d)))
            .Where(x => x.Manifest is not null)
            .Select(x => new BackupInfo(gameId, x.Manifest!.Created, x.Dir, x.Manifest.Files.Count + (x.Manifest.Registry?.Count ?? 0)))
            .OrderByDescending(b => b.Created)
            .ToList();
    }

    public static bool HasBackup(string gameId) => List(gameId).Count > 0;

    /// <summary>Restores the newest backup. Returns a human readable result.</summary>
    public static string RestoreLatest(string gameId)
    {
        var latest = List(gameId).FirstOrDefault();
        if (latest is null) return "No backup found for this game.";
        var m = Read(latest.Folder)!;

        int restored = 0;
        foreach (var e in m.Files)
        {
            var src = Path.Combine(latest.Folder, e.Stored);
            if (!File.Exists(src)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(e.Original)!);
            var attrs = File.Exists(e.Original) ? File.GetAttributes(e.Original) : FileAttributes.Normal;
            if (File.Exists(e.Original)) File.SetAttributes(e.Original, attrs & ~FileAttributes.ReadOnly);
            File.Copy(src, e.Original, overwrite: true);
            File.SetAttributes(e.Original, attrs);
            restored++;
        }
        foreach (var (key, value) in m.Registry ?? [])
        {
            var split = key.Split('|', 2);
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(split[0]);
            k.SetValue(split[1], int.Parse(value), Microsoft.Win32.RegistryValueKind.DWord);
            restored++;
        }

        // A restored backup is consumed so repeated restores walk further back in time.
        Directory.Delete(latest.Folder, recursive: true);
        return $"Restored {restored} item(s) from {latest.Created:g}.";
    }

    private static Manifest? Read(string folder)
    {
        try { return JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(folder, "manifest.json"))); }
        catch (Exception) { return null; }
    }

    private static void Prune(string gameId, int keep)
    {
        foreach (var old in List(gameId).Skip(keep))
        {
            try { Directory.Delete(old.Folder, recursive: true); }
            catch (IOException) { }
        }
    }
}
