using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Sentinel.Core;
using Sentinel.Models;
using Sentinel.Services.Catalog;

namespace Sentinel.Services.Games;

/// <summary>Finds installed games across Steam, Epic, GOG, Ubisoft, EA, Battle.net, Riot, Xbox, Rockstar and standalone installs.</summary>
public static class GameScanner
{
    private static readonly HashSet<int> SteamNonGames =
    [
        228980, 250820, 1070560, 1391110, 1628350, 1493710, 2180100, 1826330, 1161040, 1580130, 1887720, 2348590, 431960, 1245040, 365670, 1113000,
    ];

    private static readonly Regex NonGameName = new(
        @"Redistributable|Proton|Steam Linux Runtime|Steamworks|SteamVR|Dedicated Server|\bSDK\b|Soundtrack|\bOST\b|Benchmark Tool|Wallpaper Engine|" +
        @"Launcher$|Anti-?Cheat|Vanguard|Battle\.net|EA app|Origin$|Ubisoft Connect|GOG Galaxy|Epic Games Launcher|Riot Client|Rockstar Games Launcher|" +
        @"Unreal Engine|Blender|OBS Studio|DirectX|Visual C\+\+|\.NET|Graphics Driver|Display Driver|Update|Helper|Uninstall|Tools?$|Editor$|Server$",
        RegexOptions.IgnoreCase);

    public static Task<List<InstalledGame>> ScanAsync() => Task.Run(Scan);

    public static List<InstalledGame> Scan()
    {
        var found = new List<InstalledGame>();
        void Run(string name, Func<IEnumerable<InstalledGame>> scanner)
        {
            try { found.AddRange(scanner()); }
            catch (Exception ex) { Log.Error($"scan:{name}", ex); }
        }

        Run("steam", ScanSteam);
        Run("epic", ScanEpic);
        Run("gog", ScanGog);
        Run("ubisoft", ScanUbisoft);
        Run("ea", ScanEa);
        Run("xbox", ScanXbox);
        Run("registry", ScanUninstallRegistry);
        Run("special", ScanSpecial);

        var catalog = GameCatalog.Instance;
        foreach (var g in found)
        {
            g.Catalog ??= catalog.Match(g.Name, int.TryParse(g.AppId, out var id) && g.Launcher == Launcher.Steam ? id : null, g.MainExe);
            try { EngineDetector.Detect(g); }
            catch (Exception ex) { Log.Error($"engine:{g.Name}", ex); }
        }

        // Same game found by a launcher and by the uninstall registry: keep the launcher's record.
        return found
            .Where(g => !string.IsNullOrWhiteSpace(g.Name))
            .GroupBy(g => g.Catalog?.Id ?? NameNormalizer.Loose(g.Name))
            .Select(grp => grp.OrderBy(x => x.Launcher == Launcher.Other ? 1 : 0).First())
            .OrderBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ---------------------------------------------------------------- Steam

    public static string? SteamRoot()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        if (k?.GetValue("SteamPath") is string p && Directory.Exists(p)) return Path.GetFullPath(p);
        using var lm = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
        return lm?.GetValue("InstallPath") is string ip && Directory.Exists(ip) ? ip : null;
    }

    public static IEnumerable<string> SteamUserDataDirs()
    {
        var root = SteamRoot();
        var ud = root is null ? null : Path.Combine(root, "userdata");
        return ud is not null && Directory.Exists(ud) ? Directory.EnumerateDirectories(ud).Where(d => Path.GetFileName(d) != "0") : [];
    }

    private static IEnumerable<InstalledGame> ScanSteam()
    {
        var root = SteamRoot();
        if (root is null) yield break;

        var libraries = new List<string> { Path.Combine(root, "steamapps") };
        var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            var lf = VdfNode.Load(vdf).Child("libraryfolders");
            foreach (var lib in lf?.Children.Values ?? Enumerable.Empty<VdfNode>())
                if (lib["path"] is string path) libraries.Add(Path.Combine(path, "steamapps"));
        }

        foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
        {
            foreach (var acf in Directory.EnumerateFiles(lib, "appmanifest_*.acf"))
            {
                VdfNode? state;
                try { state = VdfNode.Load(acf).Child("AppState"); }
                catch (IOException) { continue; }
                if (state is null || !int.TryParse(state["appid"], out var appId)) continue;

                var name = state["name"] ?? "";
                if (SteamNonGames.Contains(appId) || NonGameName.IsMatch(name)) continue;

                var dir = Path.Combine(lib, "common", state["installdir"] ?? "");
                if (!Directory.Exists(dir)) continue;

                yield return new InstalledGame
                {
                    Name = name,
                    Launcher = Launcher.Steam,
                    AppId = appId.ToString(),
                    InstallPath = dir,
                    SizeBytes = long.TryParse(state["SizeOnDisk"], out var size) ? size : 0,
                };
            }
        }
    }

    // ---------------------------------------------------------------- Epic

    private static IEnumerable<InstalledGame> ScanEpic()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
        if (!Directory.Exists(dir)) yield break;

        foreach (var item in Directory.EnumerateFiles(dir, "*.item"))
        {
            JsonElement root;
            try { root = JsonDocument.Parse(File.ReadAllText(item)).RootElement; }
            catch (Exception) { continue; }

            string Str(string p) => root.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            if (root.TryGetProperty("bIsIncompleteInstall", out var inc) && inc.ValueKind == JsonValueKind.True) continue;
            if (root.TryGetProperty("AppCategories", out var cats) && cats.ValueKind == JsonValueKind.Array &&
                !cats.EnumerateArray().Any(c => c.GetString() == "games")) continue;

            var name = Str("DisplayName");
            var path = Str("InstallLocation");
            if (NonGameName.IsMatch(name) || !Directory.Exists(path)) continue;

            var exe = Str("LaunchExecutable");
            yield return new InstalledGame
            {
                Name = name,
                Launcher = Launcher.Epic,
                AppId = Str("AppName"),
                InstallPath = path,
                MainExe = exe.Length > 0 ? Path.Combine(path, exe) : null,
                SizeBytes = root.TryGetProperty("InstallSize", out var s) && s.TryGetInt64(out var sz) ? sz : 0,
            };
        }
    }

    // ---------------------------------------------------------------- GOG

    private static IEnumerable<InstalledGame> ScanGog()
    {
        using var games = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
        if (games is null) yield break;
        foreach (var id in games.GetSubKeyNames())
        {
            using var k = games.OpenSubKey(id);
            var name = k?.GetValue("gameName") as string ?? k?.GetValue("GAMENAME") as string;
            var path = k?.GetValue("path") as string ?? k?.GetValue("PATH") as string;
            if (name is null || path is null || !Directory.Exists(path)) continue;
            yield return new InstalledGame
            {
                Name = name,
                Launcher = Launcher.GOG,
                AppId = id,
                InstallPath = path,
                MainExe = k?.GetValue("exe") as string,
            };
        }
    }

    // ---------------------------------------------------------------- Ubisoft

    private static IEnumerable<InstalledGame> ScanUbisoft()
    {
        using var installs = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
        if (installs is null) yield break;
        foreach (var id in installs.GetSubKeyNames())
        {
            using var k = installs.OpenSubKey(id);
            if (k?.GetValue("InstallDir") is not string dir || !Directory.Exists(dir)) continue;
            var full = Path.GetFullPath(dir);
            yield return new InstalledGame
            {
                Name = UninstallName($"Uplay Install {id}") ?? Path.GetFileName(full.TrimEnd('\\', '/')),
                Launcher = Launcher.Ubisoft,
                AppId = id,
                InstallPath = full,
            };
        }
    }

    // ---------------------------------------------------------------- EA

    private static IEnumerable<InstalledGame> ScanEa()
    {
        // EA app writes one key per title under Electronic Arts / EA Games with an "Install Dir" value.
        foreach (var vendor in new[] { @"SOFTWARE\WOW6432Node\Electronic Arts", @"SOFTWARE\WOW6432Node\EA Games", @"SOFTWARE\Electronic Arts" })
        {
            using var root = Registry.LocalMachine.OpenSubKey(vendor);
            if (root is null) continue;
            foreach (var title in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(title);
                var dir = k?.GetValue("Install Dir") as string ?? k?.GetValue("InstallDir") as string;
                if (dir is null || !Directory.Exists(dir) || NonGameName.IsMatch(title)) continue;
                yield return new InstalledGame { Name = title, Launcher = Launcher.EA, InstallPath = dir };
            }
        }
    }

    // ---------------------------------------------------------------- Xbox / Microsoft Store

    private static IEnumerable<InstalledGame> ScanXbox()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            foreach (var root in new[] { "XboxGames", @"Program Files\ModifiableWindowsApps" })
            {
                var dir = Path.Combine(drive.RootDirectory.FullName, root);
                if (!Directory.Exists(dir)) continue;
                IEnumerable<string> subs;
                try { subs = Directory.EnumerateDirectories(dir).ToList(); }
                catch (UnauthorizedAccessException) { continue; }
                foreach (var game in subs)
                {
                    var name = Path.GetFileName(game);
                    if (name.Equals("GameSave", StringComparison.OrdinalIgnoreCase) || NonGameName.IsMatch(name)) continue;
                    var content = Path.Combine(game, "Content");
                    yield return new InstalledGame
                    {
                        Name = name,
                        Launcher = Launcher.Xbox,
                        InstallPath = Directory.Exists(content) ? content : game,
                    };
                }
            }
        }
    }

    // ---------------------------------------------------------------- Uninstall registry (Battle.net, Riot, Rockstar, standalone)

    private static readonly (string Pattern, Launcher Launcher)[] PublisherLaunchers =
    [
        ("Blizzard", Launcher.BattleNet), ("Activision", Launcher.BattleNet), ("Riot Games", Launcher.Riot),
        ("Rockstar Games", Launcher.Rockstar), ("Electronic Arts", Launcher.EA), ("Ubisoft", Launcher.Ubisoft),
    ];

    private static IEnumerable<InstalledGame> ScanUninstallRegistry()
    {
        var catalog = GameCatalog.Instance;
        foreach (var (hive, path) in UninstallRoots())
        {
            using var root = hive.OpenSubKey(path);
            if (root is null) continue;
            foreach (var sub in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(sub);
                if (k?.GetValue("DisplayName") is not string name || NonGameName.IsMatch(name)) continue;
                if (k.GetValue("SystemComponent") is int sc && sc == 1) continue;

                var publisher = k.GetValue("Publisher") as string ?? "";
                var location = (k.GetValue("InstallLocation") as string)?.Trim('"');
                var uninstall = k.GetValue("UninstallString") as string ?? "";

                // Steam/Epic/GOG/Ubisoft entries are already covered by their own scanners.
                if (sub.StartsWith("Steam App", StringComparison.OrdinalIgnoreCase) || sub.StartsWith("Uplay Install", StringComparison.OrdinalIgnoreCase)) continue;
                if (uninstall.Contains("GOG Galaxy", StringComparison.OrdinalIgnoreCase)) continue;

                var match = catalog.Match(name);
                var launcher = PublisherLaunchers.FirstOrDefault(p => publisher.Contains(p.Pattern, StringComparison.OrdinalIgnoreCase)).Launcher;
                if (uninstall.Contains("RiotClientServices", StringComparison.OrdinalIgnoreCase)) launcher = Launcher.Riot;
                if (uninstall.Contains("Battle.net", StringComparison.OrdinalIgnoreCase)) launcher = Launcher.BattleNet;

                // Only surface registry entries we can tie to a game: a catalog hit or a known game publisher.
                if (match is null && launcher == default) continue;
                if (launcher == default) launcher = Launcher.Other;

                yield return new InstalledGame
                {
                    Name = name,
                    Launcher = launcher,
                    InstallPath = !string.IsNullOrEmpty(location) && Directory.Exists(location) ? location : null,
                    Catalog = match,
                };
            }
        }
    }

    private static IEnumerable<(RegistryKey Hive, string Path)> UninstallRoots() =>
    [
        (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
        (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
    ];

    private static string? UninstallName(string subKey)
    {
        foreach (var (hive, path) in UninstallRoots())
        {
            using var k = hive.OpenSubKey($@"{path}\{subKey}");
            if (k?.GetValue("DisplayName") is string n) return n;
        }
        return null;
    }

    // ---------------------------------------------------------------- Games without a classic install (Minecraft Java, Roblox)

    private static IEnumerable<InstalledGame> ScanSpecial()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var catalog = GameCatalog.Instance;

        var mc = Path.Combine(appData, ".minecraft");
        if (File.Exists(Path.Combine(mc, "options.txt")))
        {
            // Java Edition has no exe of its own; the launcher's exe supplies the icon.
            var launcher = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => Path.Combine(d.RootDirectory.FullName, @"XboxGames\Minecraft Launcher\Content\Minecraft.exe"))
                .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Minecraft Launcher\MinecraftLauncher.exe"))
                .FirstOrDefault(File.Exists);
            yield return new InstalledGame
            {
                Name = "Minecraft: Java Edition", Launcher = Launcher.Standalone, InstallPath = mc, IconPath = launcher,
                Catalog = catalog.Match("Minecraft: Java Edition"),
            };
        }

        var roblox = Path.Combine(local, "Roblox");
        var hasPlayer = Directory.Exists(Path.Combine(roblox, "Versions")) &&
                        Directory.EnumerateFiles(Path.Combine(roblox, "Versions"), "RobloxPlayerBeta.exe", SearchOption.AllDirectories).Any();
        if (hasPlayer || File.Exists(Path.Combine(roblox, "GlobalSettings_13.xml")))
            yield return new InstalledGame { Name = "Roblox", Launcher = Launcher.Standalone, InstallPath = roblox, Catalog = catalog.Match("Roblox") };
    }
}
