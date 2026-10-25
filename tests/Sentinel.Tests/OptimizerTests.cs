using Sentinel.Models;
using Sentinel.Services.Catalog;
using Sentinel.Services.Optimizer;

namespace Sentinel.Tests;

public class OptimizerTests
{
    private static readonly Resolution R1440 = new(2560, 1440);
    private static CatalogGame Game(string name) => GameCatalog.Instance.Match(name) ?? throw new InvalidOperationException(name);
    private static double AvgLevel(OptimizationPlan p) => p.Levels.Values.Average();

    [Fact]
    public void Higher_fps_targets_never_raise_quality()
    {
        var hw = TestData.Rtx4070;
        var game = Game("Cyberpunk 2077");
        double previous = double.MaxValue;
        foreach (var min in new[] { 30, 60, 90, 120, 144 })
        {
            var plan = SettingsOptimizer.Optimize(game, hw, new OptimizeRequest { MinFps = min, MaxFps = min + 30, Resolution = R1440, AllowFrameGen = false });
            // Ray tracing is part of image quality: a lower fps target may trade raster settings for RT/PT.
            double quality = AvgLevel(plan) + plan.RenderScale / 100d + (int)plan.RayTracing * 0.75;
            Assert.True(quality <= previous + 1e-9, $"quality rose at {min} fps");
            previous = quality;
        }
    }

    [Fact]
    public void Reachable_targets_are_met()
    {
        var plan = SettingsOptimizer.Optimize(Game("Fortnite"), TestData.Rtx4070, new OptimizeRequest { MinFps = 120, MaxFps = 165, Resolution = R1440 });
        Assert.True(plan.TargetReached);
        Assert.True(plan.EstimatedFps >= 115);
    }

    [Fact]
    public void Nvidia_rtx_gets_dlss_and_amd_does_not()
    {
        var req = new OptimizeRequest { MinFps = 120, MaxFps = 144, Resolution = R1440 };
        var nv = SettingsOptimizer.Optimize(Game("Cyberpunk 2077"), TestData.Rtx4070, req);
        var amd = SettingsOptimizer.Optimize(Game("Cyberpunk 2077"), TestData.Rx9070Xt, req);
        Assert.Contains(nv.Modern, m => m.Name == "Upscaling" && m.Value.StartsWith("DLSS"));
        Assert.DoesNotContain(amd.Modern, m => m.Value.StartsWith("DLSS"));
        Assert.Contains(nv.Modern, m => m.Name == "NVIDIA Reflex");
        Assert.Contains(amd.Modern, m => m.Name == "AMD Anti-Lag 2" || m.Name == "Radeon Anti-Lag");
    }

    [Fact]
    public void Competitive_mode_never_uses_frame_generation_and_drops_clutter()
    {
        var plan = SettingsOptimizer.Optimize(Game("Marvel Rivals"), TestData.Rtx4070,
            new OptimizeRequest { MinFps = 200, MaxFps = 240, Resolution = R1440, Competitive = true });
        Assert.Equal(FrameGenKind.None, plan.FrameGen);
        Assert.Equal(RayTracingMode.Off, plan.RayTracing);
        Assert.Equal(QualityLevel.Low, plan.Levels[SettingCategory.PostProcessing]);
        Assert.Equal(QualityLevel.Low, plan.Levels[SettingCategory.Foliage]);
    }

    // Regression: competitive Fortnite on an RX 9070 XT kept Shadows/GI/Reflections at Epic and dropped to 67% render scale.
    [Fact]
    public void Competitive_fortnite_stays_native_and_drops_eye_candy_first()
    {
        var plan = SettingsOptimizer.Optimize(Game("Fortnite"), TestData.Rx9070Xt,
            new OptimizeRequest { MinFps = 216, MaxFps = 360, Resolution = R1440, Competitive = true });

        Assert.Equal(100, plan.RenderScale);
        Assert.Equal(UpscalerKind.None, plan.Upscaler);
        foreach (var eyeCandy in new[] { SettingCategory.Shadows, SettingCategory.Lighting, SettingCategory.Reflections })
        {
            Assert.Equal(QualityLevel.Low, plan.Levels[eyeCandy]);
            Assert.True(plan.Levels[eyeCandy] <= plan.Levels[SettingCategory.ViewDistance], $"{eyeCandy} should drop before view distance");
        }
        Assert.True(plan.TargetReached);
    }

    [Fact]
    public void Competitive_plans_only_upscale_once_eye_candy_is_exhausted()
    {
        HardwareProfile[] rigs = [TestData.Rtx4070, TestData.Rx9070Xt, TestData.Gtx1650];
        var games = GameCatalog.Instance.Games.Where(g => g.Competitive).ToList();
        Assert.NotEmpty(games);
        foreach (var hw in rigs)
        foreach (var g in games)
        foreach (var min in new[] { 144, 240 })
        {
            var plan = SettingsOptimizer.Optimize(g, hw, new OptimizeRequest { MinFps = min, MaxFps = min + 60, Resolution = R1440, Competitive = true });
            if (plan.RenderScale == 100) continue;
            foreach (var c in new[] { SettingCategory.Shadows, SettingCategory.Lighting, SettingCategory.Reflections, SettingCategory.AmbientOcclusion })
                Assert.True(plan.Levels[c] == QualityLevel.Low, $"{g.Name} on {hw.Gpu.Name} @ {min}: upscaled with {c} at {QualityLevel.Name(plan.Levels[c])}");
        }
    }

    [Fact]
    public void Non_competitive_plans_still_prefer_upscaling_over_ugly_settings()
    {
        var plan = SettingsOptimizer.Optimize(Game("Cyberpunk 2077"), TestData.Rtx4070, new OptimizeRequest { MinFps = 90, MaxFps = 120, Resolution = R1440 });
        Assert.True(plan.RenderScale < 100, "DLSS should be used before dropping everything to Low");
    }

    [Fact]
    public void Frame_generation_helps_when_the_cpu_is_the_limit()
    {
        var hw = TestData.Rig("NVIDIA GeForce RTX 4080", 177, 16, GpuVendor.Nvidia, cpuScore: 80);
        var plan = SettingsOptimizer.Optimize(Game("Starfield"), hw, new OptimizeRequest { MinFps = 100, MaxFps = 144, Resolution = R1440 });
        Assert.Equal(FrameGenKind.DlssFg, plan.FrameGen);
        Assert.True(plan.EstimatedFps > plan.BaseFps * 1.5);
    }

    [Fact]
    public void Low_vram_caps_texture_quality()
    {
        var plan = SettingsOptimizer.Optimize(Game("Monster Hunter Wilds"), TestData.Gtx1650, new OptimizeRequest { MinFps = 30, MaxFps = 60, Resolution = new(1920, 1080) });
        Assert.True(plan.Levels[SettingCategory.Textures] <= QualityLevel.Medium);
    }

    [Fact]
    public void Weak_hardware_reports_unreachable_targets_honestly()
    {
        var plan = SettingsOptimizer.Optimize(Game("Monster Hunter Wilds"), TestData.Gtx1650, new OptimizeRequest { MinFps = 144, MaxFps = 165, Resolution = R1440 });
        Assert.False(plan.TargetReached);
        Assert.NotEmpty(plan.Notes);
    }

    [Fact]
    public void Path_tracing_is_only_picked_on_high_end_rtx_with_headroom()
    {
        var hw = TestData.Rig("NVIDIA GeForce RTX 4090", 225, 24, GpuVendor.Nvidia, cpuScore: 150);
        var cinematic = SettingsOptimizer.Optimize(Game("Cyberpunk 2077"), hw, new OptimizeRequest { MinFps = 60, MaxFps = 90, Resolution = R1440 });
        Assert.Equal(RayTracingMode.PathTracing, cinematic.RayTracing);

        var amd = SettingsOptimizer.Optimize(Game("Cyberpunk 2077"), TestData.Rx9070Xt, new OptimizeRequest { MinFps = 60, MaxFps = 90, Resolution = R1440 });
        Assert.NotEqual(RayTracingMode.PathTracing, amd.RayTracing);
    }

    [Fact]
    public void Engine_locked_games_never_promise_more_than_the_lock()
    {
        var hw = TestData.Rig("NVIDIA GeForce RTX 4090", 225, 24, GpuVendor.Nvidia, cpuScore: 166, refresh: 240);
        var plan = SettingsOptimizer.Optimize(Game("Elden Ring"), hw, new OptimizeRequest { MinFps = 144, MaxFps = 240, Resolution = R1440 });
        Assert.True(plan.EstimatedFps <= 60);
        Assert.True(plan.TypicalFps <= 60);
        Assert.False(plan.TargetReached);
        Assert.True(plan.FpsCap <= 60);
        Assert.Contains(plan.Notes, n => n.Contains("locked to 60"));
        Assert.All(SettingsOptimizer.PresetEstimates(Game("Elden Ring"), hw, R1440), fps => Assert.True(fps <= 60));
    }

    [Fact]
    public void Estimates_are_the_conservative_end_of_the_range()
    {
        var plan = SettingsOptimizer.Optimize(Game("Cyberpunk 2077"), TestData.Rtx4070, new OptimizeRequest { MinFps = 60, MaxFps = 90, Resolution = R1440 });
        Assert.Equal(plan.TypicalFps * PerformanceModel.ConservativeFactor, plan.EstimatedFps, 3);
        Assert.True(plan.EstimatedFps >= 60, "the conservative figure, not the typical one, must clear the target");
    }

    [Fact]
    public void Ai_estimated_games_use_the_wider_safety_margin_and_say_so()
    {
        var estimated = GameCatalog.Instance.Games.First(g => g.IsEstimated && g.EngineFpsCap == 0);
        var plan = SettingsOptimizer.Optimize(estimated, TestData.Rtx4070, new OptimizeRequest { MinFps = 60, MaxFps = 120, Resolution = R1440 });
        Assert.Equal(plan.TypicalFps * PerformanceModel.EstimatedDataFactor, plan.EstimatedFps, 3);
        Assert.Contains(plan.Notes, n => n.Contains("AI estimate"));
        Assert.False(Game("Cyberpunk 2077").IsEstimated);
    }

    [Fact]
    public void Plans_never_promise_fps_above_the_hardware_ceiling()
    {
        HardwareProfile[] rigs = [TestData.Rtx4070, TestData.Rx9070Xt, TestData.Gtx1650];
        foreach (var hw in rigs)
        foreach (var g in GameCatalog.Instance.Games)
        foreach (var min in new[] { 60, 240 })
        {
            var plan = SettingsOptimizer.Optimize(g, hw, new OptimizeRequest { MinFps = min, MaxFps = min + 60, Resolution = R1440 });
            Assert.True(plan.EstimatedFps <= plan.Ceiling + 0.5, $"{g.Name} on {hw.Gpu.Name}: {plan.EstimatedFps:0} > ceiling {plan.Ceiling:0}");
            Assert.True(plan.EstimatedFps <= plan.TypicalFps + 0.01, g.Name);
            if (plan.TargetReached) Assert.True(plan.EstimatedFps >= min * 0.99, $"{g.Name} claims {min} but plans {plan.EstimatedFps:0}");
            if (min > plan.Ceiling + 0.5) Assert.False(plan.TargetReached, $"{g.Name} on {hw.Gpu.Name} claims an impossible {min}");
            if (g.EngineFpsCap > 0) Assert.True(plan.TypicalFps <= g.EngineFpsCap, g.Name);
        }
    }

    [Fact]
    public void Unreachable_targets_plan_for_the_ceiling_instead()
    {
        var plan = SettingsOptimizer.Optimize(Game("Cyberpunk 2077"), TestData.Gtx1650, new OptimizeRequest { MinFps = 165, MaxFps = 240, Resolution = R1440 });
        Assert.False(plan.TargetReached);
        Assert.True(plan.EstimatedFps >= plan.Ceiling * 0.9, $"plan {plan.EstimatedFps:0} should be close to the best possible {plan.Ceiling:0}");
        Assert.Contains(plan.Notes, n => n.Contains("tops out"));
    }

    [Fact]
    public void Every_catalog_game_produces_a_complete_plan()
    {
        var req = new OptimizeRequest { MinFps = 60, MaxFps = 144, Resolution = R1440 };
        foreach (var g in GameCatalog.Instance.Games)
        {
            var plan = SettingsOptimizer.Optimize(g, TestData.Rtx4070, req);
            Assert.Equal(Enum.GetValues<SettingCategory>().Length, plan.Levels.Count);
            Assert.Contains(plan.Modern, m => m.Name == "Frame rate cap");
            Assert.True(plan.EstimatedFps > 0, g.Name);
        }
    }
}
