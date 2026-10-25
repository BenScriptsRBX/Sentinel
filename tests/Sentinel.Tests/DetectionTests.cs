using Sentinel.Services.Catalog;
using Sentinel.Services.Games;
using Sentinel.Services.Hardware;

namespace Sentinel.Tests;

public class DetectionTests
{
    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070 Ti SUPER", 160)]
    [InlineData("NVIDIA GeForce RTX 4070", 122)]
    [InlineData("NVIDIA GeForce RTX 4060 Laptop GPU", 72)]
    [InlineData("AMD Radeon RX 7900 XT", 165)]
    [InlineData("AMD Radeon RX 7900 XTX", 190)]
    [InlineData("AMD Radeon RX 9070 XT", 170)]
    [InlineData("AMD Radeon RX 6600", 64)]
    [InlineData("Intel(R) Arc(TM) B580 Graphics", 85)]
    [InlineData("Intel(R) UHD Graphics 770", 4)]
    [InlineData("AMD Radeon 780M Graphics", 22)]
    public void Gpu_names_match_the_right_model(string name, double expected)
    {
        var (score, _, _) = HardwareDatabase.MatchGpu(name, 8L << 30);
        Assert.Equal(expected, score);
    }

    [Fact]
    public void Unknown_gpu_falls_back_to_a_vram_based_guess()
    {
        var (score, match, _) = HardwareDatabase.MatchGpu("Moore Threads MTT S80", 16L << 30);
        Assert.Null(match);
        Assert.InRange(score, 60, 140);
    }

    [Theory]
    [InlineData("AMD Ryzen 7 9800X3D 8-Core Processor", 166)]
    [InlineData("AMD Ryzen 7 7800X3D 8-Core Processor", 150)]
    [InlineData("AMD Ryzen 7 5800X 8-Core Processor", 104)]
    [InlineData("AMD Ryzen 5 7500F 6-Core Processor", 122)]
    [InlineData("AMD Ryzen 7 7700 8-Core Processor", 128)]
    [InlineData("13th Gen Intel(R) Core(TM) i7-13700KF", 135)]
    [InlineData("12th Gen Intel(R) Core(TM) i5-12400F", 105)]
    [InlineData("Intel(R) Core(TM) i7-7700 CPU @ 3.60GHz", 58)]
    [InlineData("Intel(R) Core(TM) Ultra 9 285K", 140)]
    public void Cpu_names_match_the_right_model(string name, double expected)
    {
        var (score, _, _) = HardwareDatabase.MatchCpu(name, 8, 16, 4500);
        Assert.Equal(expected, score);
    }

    [Fact]
    public void Catalog_is_large_and_unique()
    {
        var games = GameCatalog.Instance.Games;
        Assert.True(games.Count >= 500, $"catalog has {games.Count} games");
        Assert.Equal(games.Count, games.Select(g => g.Id).Distinct().Count());
        Assert.All(games, g => Assert.InRange(g.GpuDemand, 1, 10));
    }

    [Theory]
    [InlineData("Counter-Strike 2", 730, "Counter-Strike 2")]
    [InlineData("Cyberpunk 2077: Ultimate Edition", null, "Cyberpunk 2077")]
    [InlineData("Tom Clancy's Rainbow Six® Siege", null, "Rainbow Six Siege")]
    [InlineData("The Witcher 3: Wild Hunt - Game of the Year Edition", null, "The Witcher 3: Wild Hunt")]
    [InlineData("VALORANT", null, "Valorant")]
    public void Catalog_matches_launcher_names(string name, int? steamId, string expected)
    {
        Assert.Equal(expected, GameCatalog.Instance.Match(name, steamId)?.Name);
    }

    // Regression: a catalog merge script once rewrote the hand-curated files without "cfg"/"x",
    // silently turning CS2, Apex, Minecraft and Roblox from auto-tune into guide-only games.
    [Theory]
    [InlineData("Counter-Strike 2", "cs2", "cs2.exe")]
    [InlineData("Apex Legends", "apex", "r5apex.exe")]
    [InlineData("Minecraft: Java Edition", "minecraft", "javaw.exe")]
    [InlineData("Roblox", "roblox", "RobloxPlayerBeta.exe")]
    [InlineData("League of Legends", "league", "League of Legends.exe")]
    [InlineData("Grand Theft Auto V", "gtav", "GTA5.exe")]
    [InlineData("Red Dead Redemption 2", "rdr2", "RDR2.exe")]
    [InlineData("Fortnite", "ue:FortniteGame", "FortniteClient-Win64-Shipping.exe")]
    public void Hand_curated_games_keep_their_config_profile_and_exe(string name, string profile, string exe)
    {
        var game = GameCatalog.Instance.Match(name);
        Assert.NotNull(game);
        Assert.Equal(profile, game!.Profile);
        Assert.Contains(exe, game.Exes);
    }

    [Fact]
    public void Every_config_profile_is_used_by_a_catalog_game()
    {
        var used = GameCatalog.Instance.Games.Select(g => g.Profile).Where(p => p is not null).ToHashSet();
        var profiles = System.Text.Json.JsonDocument.Parse(
                typeof(GameCatalog).Assembly.GetManifestResourceStream("Sentinel.Data.profiles.json")!)
            .RootElement.EnumerateArray().Select(p => p.GetProperty("id").GetString());
        Assert.All(profiles, id => Assert.Contains(id, used));
    }

    [Fact]
    public void Vdf_parser_reads_nested_libraries()
    {
        var vdf = VdfNode.Parse("""
            "libraryfolders"
            {
                "0" { "path" "C:\\Program Files (x86)\\Steam" "apps" { "730" "123" } }
                "1" { "path" "D:\\SteamLibrary" }
            }
            """);
        var libs = vdf.Child("libraryfolders")!.Children.Values.Select(c => c["path"]).ToList();
        Assert.Equal(["C:\\Program Files (x86)\\Steam", "D:\\SteamLibrary"], libs);
        Assert.Equal("123", vdf.Child("libraryfolders")!.Child("0")!.Child("apps")!["730"]);
    }
}
