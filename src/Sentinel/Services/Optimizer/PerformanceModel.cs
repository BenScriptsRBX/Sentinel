using System.Text.RegularExpressions;
using Sentinel.Models;

namespace Sentinel.Services.Optimizer;

/// <summary>
/// Analytical fps model. Reference rig is an RTX 3070 (GPU score 100) and a Ryzen 5 5600X (CPU score 100)
/// running the game at 1080p High; catalog demand ratings 1-10 map to that rig's fps.
/// </summary>
public static class PerformanceModel
{
    private static readonly double[] GpuBaseFps = [0, 600, 400, 260, 180, 130, 100, 80, 65, 52, 40];
    private static readonly double[] CpuBaseFps = [0, 900, 600, 400, 280, 200, 160, 130, 105, 85, 65];

    // Share of GPU frame time each category takes at High, and its multiplier per level (Low..Ultra).
    public static readonly Dictionary<SettingCategory, double> GpuShare = new()
    {
        [SettingCategory.Shadows] = 0.14, [SettingCategory.Lighting] = 0.12, [SettingCategory.Volumetrics] = 0.07,
        [SettingCategory.Reflections] = 0.07, [SettingCategory.AmbientOcclusion] = 0.05, [SettingCategory.PostProcessing] = 0.05,
        [SettingCategory.Effects] = 0.04, [SettingCategory.ViewDistance] = 0.06, [SettingCategory.Foliage] = 0.04,
        [SettingCategory.AntiAliasing] = 0.03, [SettingCategory.Textures] = 0.01,
    };
    private const double GpuFixedShare = 0.32;
    private static readonly double[] LevelCost = [0.35, 0.65, 1.0, 1.5];

    private static readonly Dictionary<SettingCategory, double[]> CpuLevelCost = new()
    {
        [SettingCategory.ViewDistance] = [0.82, 0.90, 1.0, 1.10],
        [SettingCategory.Foliage] = [0.95, 0.97, 1.0, 1.03],
        [SettingCategory.Shadows] = [0.94, 0.97, 1.0, 1.04],
        [SettingCategory.Effects] = [0.95, 0.98, 1.0, 1.03],
    };

    public static readonly int[] RenderScaleSteps = [100, 77, 67, 58, 50, 33];

    /// <summary>
    /// The model's estimates scatter roughly ±12% around real benchmarks. Plans are built on the low end of that
    /// band so the fps Sentinel shows is one the hardware should actually hold.
    /// </summary>
    public const double ConservativeFactor = 0.88;
    /// <summary>Wider margin for games whose demand ratings are AI-estimated rather than hand-curated.</summary>
    public const double EstimatedDataFactor = 0.80;

    /// <summary>Clamps to the game's own fps lock (if any).</summary>
    public static double Capped(CatalogGame game, double fps) => game.EngineFpsCap > 0 ? Math.Min(fps, game.EngineFpsCap) : fps;

    /// <summary>Low end of the expected range, clamped to the game's fps lock.</summary>
    public static double Conservative(CatalogGame game, double fps) =>
        Capped(game, fps * (game.IsEstimated ? EstimatedDataFactor : ConservativeFactor));

    public static double GpuCost(IReadOnlyDictionary<SettingCategory, int> levels)
    {
        double cost = GpuFixedShare;
        foreach (var (cat, share) in GpuShare)
            cost += share * LevelCost[levels.TryGetValue(cat, out var l) ? l : QualityLevel.High];
        return cost;
    }

    public static double CpuCost(IReadOnlyDictionary<SettingCategory, int> levels)
    {
        double cost = 1;
        foreach (var (cat, table) in CpuLevelCost)
            cost *= table[levels.TryGetValue(cat, out var l) ? l : QualityLevel.High];
        return cost;
    }

    /// <summary>GPU cost scaling with rendered pixels (sub-linear: geometry and CPU-side work don't scale).</summary>
    public static double ResolutionCost(Resolution output, int renderScale)
    {
        double scale = renderScale / 100d;
        double rendered = output.Pixels * scale * scale;
        double cost = Math.Pow(rendered / (1920d * 1080), 0.75);
        if (renderScale < 100) cost += 0.05 * Math.Sqrt(output.Pixels / (1920d * 1080)); // upscaler pass
        return cost;
    }

    /// <summary>How much fps survives turning ray tracing on, by GPU architecture.</summary>
    public static double RayTracingFactor(GpuInfo gpu, RayTracingMode mode)
    {
        if (mode == RayTracingMode.Off) return 1;
        var n = gpu.Name.ToUpperInvariant();
        bool rtx5040 = Regex.IsMatch(n, @"RTX\s*[45]0\d0");
        bool rtx2030 = Regex.IsMatch(n, @"RTX\s*[23]0\d0");
        bool rdna4 = Regex.IsMatch(n, @"RX\s*90\d0");
        bool rdna3 = Regex.IsMatch(n, @"RX\s*7\d00");
        bool arc = n.Contains("ARC");
        if (mode == RayTracingMode.PathTracing)
            return rtx5040 ? 0.32 : rtx2030 ? 0.22 : rdna4 ? 0.24 : 0.14;
        return rtx5040 ? 0.72 : rtx2030 ? 0.62 : rdna4 ? 0.68 : rdna3 ? 0.55 : arc ? 0.58 : 0.45;
    }

    public static bool SupportsHardwareRt(GpuInfo gpu)
    {
        var n = gpu.Name.ToUpperInvariant();
        return Regex.IsMatch(n, @"RTX|RX\s*[679]\d{3}|ARC") && !Regex.IsMatch(n, @"RX\s*5\d{3}") && !gpu.IsIntegrated
               || Regex.IsMatch(n, @"8[0-9]0M|8060S|8050S|780M|760M|680M");
    }

    public static double GpuBase(CatalogGame game) => GpuBaseFps[Math.Clamp(game.GpuDemand, 1, 10)];
    public static double CpuBase(CatalogGame game) => CpuBaseFps[Math.Clamp(game.CpuDemand, 1, 10)];

    public sealed record Estimate(double Gpu, double Cpu)
    {
        public double Rendered => Math.Min(Gpu, Cpu);
    }

    public static Estimate Run(CatalogGame game, HardwareProfile hw, IReadOnlyDictionary<SettingCategory, int> levels,
                               Resolution res, int renderScale, RayTracingMode rt)
    {
        double gpu = GpuBase(game) * (hw.Gpu.Score / 100d) / GpuCost(levels) / ResolutionCost(res, renderScale) * RayTracingFactor(hw.Gpu, rt);

        double cpu = CpuBase(game) * (hw.Cpu.Score / 100d) / CpuCost(levels);
        if (rt != RayTracingMode.Off) cpu *= 0.88; // BVH building is CPU work
        if (hw.Memory.TotalGb < 15 && game.CpuDemand >= 6) cpu *= 0.8;
        if (hw.System.OnBattery) { gpu *= 0.7; cpu *= 0.8; }
        return new Estimate(gpu, cpu);
    }

    /// <summary>Approximate VRAM needed (GB) for a texture level at a resolution.</summary>
    public static double VramNeeded(CatalogGame game, int textureLevel, Resolution res, RayTracingMode rt, bool frameGen)
    {
        double baseGb = game.GpuDemand switch { <= 3 => 2.0, <= 5 => 4.0, <= 7 => 6.0, <= 9 => 7.5, _ => 9.0 };
        double mult = textureLevel switch { 0 => 0.55, 1 => 0.75, 2 => 1.0, _ => 1.3 };
        double resAdd = res.Pixels switch { > 6_000_000 => 1.8, > 3_000_000 => 0.7, _ => 0 };
        return baseGb * mult + resAdd + (rt != RayTracingMode.Off ? 1.0 : 0) + (frameGen ? 0.5 : 0);
    }
}
