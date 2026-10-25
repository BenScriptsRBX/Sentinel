namespace Sentinel.Models;

/// <summary>Engine-agnostic graphics setting groups the optimizer reasons about.</summary>
public enum SettingCategory
{
    Textures,
    Shadows,
    Lighting,
    Reflections,
    AmbientOcclusion,
    Volumetrics,
    Effects,
    PostProcessing,
    ViewDistance,
    Foliage,
    AntiAliasing,
}

public static class QualityLevel
{
    public const int Low = 0, Medium = 1, High = 2, Ultra = 3;
    public static readonly string[] Names = ["Low", "Medium", "High", "Ultra"];
    public static string Name(int level) => Names[Math.Clamp(level, 0, 3)];
}

public static class CategoryInfo
{
    public static string Label(SettingCategory c) => c switch
    {
        SettingCategory.AmbientOcclusion => "Ambient occlusion",
        SettingCategory.PostProcessing => "Post-processing",
        SettingCategory.ViewDistance => "View distance / LOD",
        SettingCategory.AntiAliasing => "Anti-aliasing",
        SettingCategory.Lighting => "Lighting & GI",
        _ => c.ToString()
    };
}

public sealed record Resolution(int Width, int Height)
{
    public long Pixels => (long)Width * Height;
    public override string ToString() => $"{Width} × {Height}";
}

public enum RayTracingMode { Off, On, PathTracing }

public enum UpscalerKind { None, Dlss, Fsr4, Fsr, Xess, Tsr }

public enum FrameGenKind { None, DlssFg, DlssMfg, FsrFg }

public sealed class OptimizeRequest
{
    public int MinFps { get; init; } = 60;
    public int MaxFps { get; init; } = 144;
    public Resolution Resolution { get; init; } = new(1920, 1080);
    public bool Competitive { get; init; }
    public bool AllowRayTracing { get; init; } = true;
    public bool AllowFrameGen { get; init; } = true;
    public bool AllowUpscaling { get; init; } = true;
}

public sealed class PlanRow
{
    public required SettingCategory Category { get; init; }
    public required int Level { get; init; }
    public bool AutoApplied { get; set; }
    public string Label => CategoryInfo.Label(Category);
    public string LevelName => QualityLevel.Name(Level);
    public int LevelIndex => Level;
}

/// <summary>A modern rendering feature recommendation: upscaler, frame gen, latency, RT, sync, API…</summary>
public sealed class ModernSetting
{
    public required string Name { get; init; }
    public required string Value { get; init; }
    public string Detail { get; init; } = "";
    public bool AutoApplied { get; set; }
    /// <summary>"good" (turn on), "off" (turn off), "info".</summary>
    public string Kind { get; init; } = "info";
}

public sealed class OptimizationPlan
{
    public Dictionary<SettingCategory, int> Levels { get; init; } = [];
    public int RenderScale { get; set; } = 100;
    public UpscalerKind Upscaler { get; set; }
    public FrameGenKind FrameGen { get; set; }
    public double FrameGenMultiplier { get; set; } = 1;
    public RayTracingMode RayTracing { get; set; }
    public int FpsCap { get; set; }

    /// <summary>Conservative displayed fps (low end of the expected range), including frame generation.</summary>
    public double EstimatedFps { get; set; }
    /// <summary>Middle of the expected range — what the game will usually do.</summary>
    public double TypicalFps { get; set; }
    /// <summary>Best fps this hardware can reach in the game with the allowed options (conservative).</summary>
    public double Ceiling { get; set; }
    /// <summary>The game's own fps lock, 0 if none.</summary>
    public int EngineCap { get; set; }
    /// <summary>Rendered fps before frame generation.</summary>
    public double BaseFps { get; set; }
    public double GpuFps { get; set; }
    public double CpuFps { get; set; }
    public bool TargetReached { get; set; }
    public bool CpuBound => CpuFps < GpuFps;

    public List<ModernSetting> Modern { get; } = [];
    public List<string> Notes { get; } = [];
    public string Summary { get; set; } = "";
}

public sealed record ApplyResult(bool Success, string Message, int ChangedKeys = 0, string? File = null);
