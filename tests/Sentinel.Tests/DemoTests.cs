using Sentinel.Services;
using Sentinel.Services.Config;
using Sentinel.Services.Tweaks;

namespace Sentinel.Tests;

/// <summary>The `--demo` PC and library behind the README screenshots.</summary>
public class DemoTests
{
    [Fact]
    public void Every_demo_game_is_in_the_catalog()
    {
        var games = DemoData.Games();
        Assert.NotEmpty(games);
        Assert.All(games, g => Assert.NotNull(g.Catalog));
    }

    [Fact]
    public void Auto_tunable_demo_games_have_a_made_up_config_path_and_guide_games_have_none()
    {
        Assert.All(DemoData.Games(), g => Assert.Equal(ConfigResolver.For(g) is not null, DemoData.ConfigPath(g) is not null));
    }

    [Fact]
    public void Demo_pc_matches_the_hardware_tables_and_shows_every_kind_of_tweak()
    {
        TestData.Init();
        var hw = DemoData.Hardware();
        Assert.NotNull(hw.Gpu.MatchedModel);
        Assert.NotNull(hw.Cpu.MatchedModel);

        var statuses = TweakService.Build(hw, DemoData.Games()).Select(t => t.Status).ToHashSet();
        Assert.Equal(Enum.GetValues<TweakStatus>().ToHashSet(), statuses);
    }
}
