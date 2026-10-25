using System.Text.Json;
using Sentinel.Models;
using Sentinel.Services.Catalog;
using Sentinel.Services.Config;
using Sentinel.Services.Games;
using Sentinel.Services.Hardware;
using Sentinel.Services.Optimizer;
using Sentinel.Services.Tweaks;

namespace Sentinel.Services;

/// <summary>Headless scan used by `Sentinel.exe --report out.json` (handy for support and testing).</summary>
public static class Report
{
    public static void Write(string path)
    {
        var hw = HardwareDetector.Detect();
        var games = GameScanner.Scan();
        var tweaks = TweakService.Build(hw, games);
        var res = new Resolution(hw.Display.Width, hw.Display.Height);

        var report = new
        {
            Generated = DateTime.Now,
            CatalogSize = GameCatalog.Instance.Games.Count,
            Hardware = new
            {
                hw.Cpu, Gpus = hw.Gpus, PrimaryGpu = hw.Gpu.Name, hw.Memory, hw.Disks, hw.Displays, hw.System,
                hw.OverallScore, hw.Tier, hw.IsHybridGraphics,
            },
            Games = games.Select(g =>
            {
                var cat = g.Catalog ?? Fallback(g);
                var adapter = ConfigResolver.For(g);
                var plan = SettingsOptimizer.Optimize(cat, hw, new OptimizeRequest { MinFps = 60, MaxFps = 144, Resolution = res, Competitive = cat.Competitive }, adapter?.PresetOnly ?? false);
                return new
                {
                    g.Name, g.DisplayName, Launcher = g.LauncherLabel, g.InstallPath, g.Engine, g.UnrealProject, g.MainExe,
                    Catalogued = g.InCatalog,
                    Adapter = adapter?.Name,
                    ConfigFile = adapter?.Target,
                    Plan60to144 = new
                    {
                        Levels = plan.Levels.ToDictionary(k => k.Key.ToString(), v => QualityLevel.Name(v.Value)),
                        plan.RenderScale, Upscaler = plan.Upscaler.ToString(), FrameGen = plan.FrameGen.ToString(), Rt = plan.RayTracing.ToString(),
                        Fps = Math.Round(plan.EstimatedFps), plan.FpsCap, plan.CpuBound,
                        Modern = plan.Modern.Select(m => $"{m.Name}: {m.Value}"),
                    },
                };
            }),
            Tweaks = tweaks.Select(t => new { t.Id, t.Title, Status = t.StatusLabel, t.Impact }),
        };

        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Stand-in catalog entry for games we detected but don't have data for.</summary>
    public static CatalogGame Fallback(InstalledGame g) => new()
    {
        Name = g.Name,
        Engine = g.Engine,
        GpuDemand = g.Engine.StartsWith("Unreal 5") ? 7 : g.Engine.StartsWith("Unreal") ? 5 : g.Engine.StartsWith("Unity") ? 4 : 5,
        CpuDemand = 5,
        Features = g.Engine.StartsWith("Unreal 5") ? [GameFeature.Tsr, GameFeature.Dx12] : [],
    };
}
