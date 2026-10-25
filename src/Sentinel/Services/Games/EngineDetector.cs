using System.Text.RegularExpressions;
using Sentinel.Models;

namespace Sentinel.Services.Games;

/// <summary>Identifies the engine from install-folder fingerprints and locates the main executable.</summary>
public static class EngineDetector
{
    private static readonly Regex JunkExe = new(
        @"unins|crash|report|launcher|redist|setup|prereq|vc_?redist|dxsetup|dotnet|easyanticheat|eac|beservice|battleye|helper|" +
        @"updater|cefprocess|webhelper|quicksfv|touchup|cleanup|install|config|benchmark|uploader|bugsplat|ue4prereq|unrealcefsubprocess",
        RegexOptions.IgnoreCase);

    public static void Detect(InstalledGame game)
    {
        var dir = game.InstallPath;
        if (dir is null || !Directory.Exists(dir))
        {
            game.Engine = game.Catalog?.Engine ?? "Unknown";
            return;
        }

        string? detected = null;

        // Unreal: <Project>\Binaries\Win64\<Project>-Win64-Shipping.exe (sometimes directly under the root).
        foreach (var candidate in SafeDirs(dir).Prepend(dir))
        {
            var bin = Path.Combine(candidate, "Binaries", "Win64");
            if (!Directory.Exists(bin)) continue;
            var name = Path.GetFileName(candidate);
            if (name.Equals("Engine", StringComparison.OrdinalIgnoreCase)) continue;

            var shipping = SafeFiles(bin, "*-Shipping.exe").FirstOrDefault() ?? SafeFiles(bin, "*.exe").FirstOrDefault(f => !JunkExe.IsMatch(Path.GetFileName(f)));
            if (shipping is null) continue;

            detected = Directory.Exists(Path.Combine(dir, "Engine")) || shipping.EndsWith("-Shipping.exe", StringComparison.OrdinalIgnoreCase) ? "Unreal" : null;
            if (detected is not null)
            {
                game.UnrealProject = candidate == dir
                    ? Regex.Replace(Path.GetFileNameWithoutExtension(shipping), @"-Win64-Shipping$", "", RegexOptions.IgnoreCase)
                    : name;
                game.MainExe ??= shipping;
                break;
            }
        }

        // Unity: UnityPlayer.dll + <Game>_Data\app.info (company \n product -> HKCU\Software\company\product).
        if (detected is null && (File.Exists(Path.Combine(dir, "UnityPlayer.dll")) || SafeDirs(dir).Any(d => d.EndsWith("_Data", StringComparison.OrdinalIgnoreCase))))
        {
            var dataDir = SafeDirs(dir).FirstOrDefault(d => d.EndsWith("_Data", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(d, "app.info")));
            if (dataDir is not null || File.Exists(Path.Combine(dir, "UnityPlayer.dll")))
            {
                detected = "Unity";
                if (dataDir is not null)
                {
                    var lines = File.ReadAllLines(Path.Combine(dataDir, "app.info"));
                    if (lines.Length >= 2) game.UnityInfo = (lines[0].Trim(), lines[1].Trim());
                    var exe = Path.Combine(dir, Path.GetFileName(dataDir)[..^5] + ".exe");
                    if (File.Exists(exe)) game.MainExe ??= exe;
                }
            }
        }

        if (detected is null && Directory.Exists(Path.Combine(dir, "game", "bin", "win64"))) detected = "Source 2";
        if (detected is null && (File.Exists(Path.Combine(dir, "hl2.exe")) || File.Exists(Path.Combine(dir, "bin", "engine.dll")))) detected = "Source";
        if (detected is null && SafeFiles(dir, "*.pck").Any()) detected = "Godot";

        game.Engine = PrettyEngine(detected, game.Catalog?.Engine);
        game.MainExe ??= FindMainExe(dir, game);
    }

    private static string PrettyEngine(string? detected, string? catalogEngine)
    {
        // The catalog knows the exact version ("Unreal 5"); detection only knows the family.
        if (catalogEngine is not null && (detected is null || catalogEngine.StartsWith(detected, StringComparison.OrdinalIgnoreCase)))
            return catalogEngine;
        return detected ?? catalogEngine ?? "Unknown";
    }

    private static string? FindMainExe(string dir, InstalledGame game)
    {
        var known = game.Catalog?.Exes ?? [];
        var candidates = SafeFiles(dir, "*.exe")
            .Concat(SafeDirs(dir).SelectMany(d => SafeFiles(d, "*.exe").Concat(SafeDirs(d).SelectMany(dd => SafeFiles(dd, "*.exe")))))
            .Where(f => !JunkExe.IsMatch(Path.GetFileName(f)))
            .ToList();

        var byName = candidates.FirstOrDefault(f => known.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase));
        if (byName is not null) return byName;

        return candidates
            .Select(f => (Path: f, Size: SafeLength(f)))
            .OrderByDescending(x => x.Size)
            .Select(x => x.Path)
            .FirstOrDefault();
    }

    private static long SafeLength(string f)
    {
        try { return new FileInfo(f).Length; }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try { return Directory.GetDirectories(dir); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    private static IEnumerable<string> SafeFiles(string dir, string pattern)
    {
        try { return Directory.GetFiles(dir, pattern); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }
}
