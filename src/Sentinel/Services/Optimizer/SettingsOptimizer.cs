using System.Text.RegularExpressions;
using Sentinel.Models;
using static Sentinel.Models.SettingCategory;

namespace Sentinel.Services.Optimizer;

/// <summary>
/// Finds the best-looking settings that still hit the requested fps range. Starts at Ultra and greedily takes the
/// step (lower one setting, upscale further, or enable frame generation) with the best fps-gained per visual-cost ratio.
/// </summary>
public static class SettingsOptimizer
{
    private static readonly Dictionary<SettingCategory, double> VisualWeight = new()
    {
        [Textures] = 1.0, [Lighting] = 0.9, [ViewDistance] = 0.8, [Shadows] = 0.7, [AntiAliasing] = 0.6,
        [Reflections] = 0.5, [AmbientOcclusion] = 0.5, [Foliage] = 0.5, [Effects] = 0.45, [Volumetrics] = 0.4, [PostProcessing] = 0.3,
    };
    // In competitive play the "value" of a setting is how much it helps you see enemies, not how pretty it is:
    // resolution, view distance and AA keep targets readable; shadows, GI and reflections are eye candy.
    private static readonly Dictionary<SettingCategory, double> CompetitiveWeight = new()
    {
        [Textures] = 1.0, [ViewDistance] = 1.2, [AntiAliasing] = 0.9, [Effects] = 0.15, [Foliage] = 0.15,
        [Lighting] = 0.12, [Shadows] = 0.1, [AmbientOcclusion] = 0.1, [Volumetrics] = 0.1, [PostProcessing] = 0.1, [Reflections] = 0.08,
    };
    // Visual cost of stepping down from level i+1 to i (index = new level).
    private static readonly double[] StepLoss = [1.8, 1.0, 0.4];

    // Settings that hurt visibility in competitive play: forced Low.
    private static readonly SettingCategory[] ClutterCategories = [PostProcessing, Effects, Volumetrics, Foliage];
    // Competitive mode stays at native resolution until all of these are already Low.
    private static readonly SettingCategory[] EyeCandyCategories = [Shadows, Lighting, Reflections, AmbientOcclusion];

    public static OptimizationPlan Optimize(CatalogGame game, HardwareProfile hw, OptimizeRequest req, bool presetOnly = false)
    {
        var gpu = hw.Gpu;
        var res = req.Resolution;
        var levels = Enum.GetValues<SettingCategory>().ToDictionary(c => c, _ => QualityLevel.Ultra);
        var upscaler = PickUpscaler(game, gpu, req);
        var frameGen = req.Competitive || !req.AllowFrameGen ? FrameGenKind.None : PickFrameGen(game, gpu);
        var rtMode = RayTracingMode.Off;
        int scaleIndex = 0;
        bool fgOn = false;
        var plan = new OptimizationPlan();

        if (req.Competitive && !presetOnly)
            foreach (var c in ClutterCategories) levels[c] = QualityLevel.Low;

        // Textures are a VRAM decision, not an fps one.
        double vram = gpu.IsIntegrated ? Math.Min(hw.Memory.TotalGb / 4, 4) : Math.Max(gpu.VramGb, 1);
        int tex = QualityLevel.Ultra;
        while (tex > 0 && PerformanceModel.VramNeeded(game, tex, res, RayTracingMode.Off, false) > vram * 0.92) tex--;
        if (!presetOnly) levels[Textures] = tex;

        // Every decision uses the conservative end of the model's range, clamped to the engine's own fps lock,
        // so a plan never relies on fps the hardware is unlikely to deliver.
        double Fps(Dictionary<SettingCategory, int> lv, int si, bool fg, RayTracingMode rt)
        {
            var e = PerformanceModel.Run(game, hw, lv, res, PerformanceModel.RenderScaleSteps[si], rt);
            return PerformanceModel.Conservative(game, e.Rendered * (fg ? FrameGenMultiplier(frameGen) : 1));
        }

        // The most this PC can do in this game with the options the user allowed: everything Low (textures stay
        // VRAM-bound), deepest upscaling, frame gen if available. Asking for more than this can't be honoured.
        var floorLevels = Enum.GetValues<SettingCategory>().ToDictionary(c => c, c => c == Textures && !presetOnly ? tex : QualityLevel.Low);
        bool canUpscale = req.AllowUpscaling && !presetOnly && (upscaler != UpscalerKind.None || game.Upscaling);
        plan.Ceiling = Fps(floorLevels, canUpscale ? PerformanceModel.RenderScaleSteps.Length - 1 : 0, frameGen != FrameGenKind.None, RayTracingMode.Off);
        double target = Math.Min(req.MinFps, plan.Ceiling * 0.98);

        // Ray tracing: only when the GPU has real RT hardware and there's headroom for it at a sane quality level.
        if (req.AllowRayTracing && !req.Competitive && (game.Has(GameFeature.RayTracing) || game.Has(GameFeature.PathTracing)) &&
            PerformanceModel.SupportsHardwareRt(gpu))
        {
            var probe = new Dictionary<SettingCategory, int>(levels);
            foreach (var c in probe.Keys.ToList()) probe[c] = Math.Min(probe[c], QualityLevel.High);
            int probeScale = upscaler != UpscalerKind.None ? 2 : 0;
            bool probeFg = frameGen != FrameGenKind.None;

            if (game.Has(GameFeature.PathTracing) && Regex.IsMatch(gpu.Name, @"RTX\s*(40[789]0|50[789]0)", RegexOptions.IgnoreCase) &&
                Fps(probe, Math.Min(4, probeScale + 2), probeFg, RayTracingMode.PathTracing) >= target * 1.05)
                rtMode = RayTracingMode.PathTracing;
            else if (game.Has(GameFeature.RayTracing) && Fps(probe, probeScale, probeFg, RayTracingMode.On) >= target * 1.1)
                rtMode = RayTracingMode.On;
        }

        // Preset-only games (one quality slider) move every category together.
        if (presetOnly)
        {
            int lvl = QualityLevel.Ultra;
            while (lvl > 0 && Fps(Uniform(lvl), 0, false, rtMode) < target) lvl--;
            foreach (var c in levels.Keys.ToList()) levels[c] = lvl;
        }
        else
        {
            int guard = 0;
            while (Fps(levels, scaleIndex, fgOn, rtMode) < target && guard++ < 64)
            {
                double current = Fps(levels, scaleIndex, fgOn, rtMode);
                (double Ratio, Action Apply)? best = null;

                void Consider(double newFps, double loss, Action apply)
                {
                    if (newFps <= current * 1.005) return;
                    double ratio = (Math.Min(newFps, target * 1.15) - current) / loss;
                    if (best is null || ratio > best.Value.Ratio) best = (ratio, apply);
                }

                var weights = req.Competitive ? CompetitiveWeight : VisualWeight;
                foreach (var cat in levels.Keys.Where(c => c != Textures && levels[c] > 0).ToList())
                {
                    var trial = new Dictionary<SettingCategory, int>(levels) { [cat] = levels[cat] - 1 };
                    Consider(Fps(trial, scaleIndex, fgOn, rtMode), weights[cat] * StepLoss[trial[cat]], () => levels[cat]--);
                }

                // Competitive: native resolution is kept until everything else (bar VRAM-bound textures) is already Low.
                bool upscaleAllowed = !req.Competitive || levels.All(kv => kv.Key == Textures || kv.Value == QualityLevel.Low);
                if (req.AllowUpscaling && upscaleAllowed && scaleIndex < PerformanceModel.RenderScaleSteps.Length - 1 && (upscaler != UpscalerKind.None || game.Upscaling))
                    Consider(Fps(levels, scaleIndex + 1, fgOn, rtMode), UpscaleLoss(upscaler, res, scaleIndex + 1), () => scaleIndex++);

                // Frame gen only once the rendered base is high enough for it to feel good.
                if (!fgOn && frameGen != FrameGenKind.None)
                {
                    double baseFps = Fps(levels, scaleIndex, false, rtMode);
                    if (baseFps >= Math.Max(40, target / FrameGenMultiplier(frameGen) * 1.05))
                        Consider(Fps(levels, scaleIndex, true, rtMode), 1.4, () => fgOn = true);
                }

                if (rtMode != RayTracingMode.Off)
                    Consider(Fps(levels, scaleIndex, fgOn, RayTracingMode.Off), 2.2, () => rtMode = RayTracingMode.Off);

                if (best is null) break;
                best.Value.Apply();
            }
        }

        var final = PerformanceModel.Run(game, hw, levels, res, PerformanceModel.RenderScaleSteps[scaleIndex], rtMode);
        plan.Levels.Clear();
        foreach (var (k, v) in levels) plan.Levels[k] = v;
        plan.RenderScale = PerformanceModel.RenderScaleSteps[scaleIndex];
        plan.Upscaler = plan.RenderScale < 100 ? upscaler : UpscalerKind.None;
        plan.RayTracing = rtMode;
        plan.FrameGen = fgOn ? frameGen : FrameGenKind.None;
        plan.FrameGenMultiplier = fgOn ? FrameGenMultiplier(frameGen) : 1;
        plan.GpuFps = final.Gpu;
        plan.CpuFps = final.Cpu;
        plan.EngineCap = game.EngineFpsCap;
        plan.BaseFps = PerformanceModel.Conservative(game, final.Rendered);
        plan.EstimatedFps = PerformanceModel.Conservative(game, final.Rendered * plan.FrameGenMultiplier);
        plan.TypicalFps = PerformanceModel.Capped(game, final.Rendered * plan.FrameGenMultiplier);
        plan.TargetReached = plan.EstimatedFps >= req.MinFps * 0.99;
        plan.FpsCap = PickFpsCap(hw, req, plan);

        BuildModernSettings(plan, game, hw, req, upscaler, frameGen, tex, vram);
        BuildNotes(plan, game, hw, req, tex, vram);
        return plan;
    }

    private static Dictionary<SettingCategory, int> Uniform(int level) =>
        Enum.GetValues<SettingCategory>().ToDictionary(c => c, _ => level);

    /// <summary>Conservative uniform-preset estimates for the comparison table (native resolution, no RT, no frame gen).</summary>
    public static double[] PresetEstimates(CatalogGame game, HardwareProfile hw, Resolution res) =>
        Enumerable.Range(0, 4).Select(l => PerformanceModel.Conservative(game, PerformanceModel.Run(game, hw, Uniform(l), res, 100, RayTracingMode.Off).Rendered)).ToArray();

    // ---------------------------------------------------------------- feature selection

    public static UpscalerKind PickUpscaler(CatalogGame game, GpuInfo gpu, OptimizeRequest req)
    {
        if (!req.AllowUpscaling) return UpscalerKind.None;
        var n = gpu.Name.ToUpperInvariant();
        bool rtx = gpu.Vendor == GpuVendor.Nvidia && n.Contains("RTX");
        bool rdna4 = gpu.Vendor == GpuVendor.Amd && Regex.IsMatch(n, @"RX\s*90\d0");
        bool arc = gpu.Vendor == GpuVendor.Intel && n.Contains("ARC");

        if (rtx && game.Has(GameFeature.Dlss)) return UpscalerKind.Dlss;
        if (rdna4 && (game.Has(GameFeature.Fsr4) || game.Has(GameFeature.Fsr))) return UpscalerKind.Fsr4;
        if (arc && game.Has(GameFeature.Xess)) return UpscalerKind.Xess;
        if (game.Has(GameFeature.Tsr)) return UpscalerKind.Tsr;
        if (game.Has(GameFeature.Xess) && !gpu.IsIntegrated && gpu.Score >= 40) return UpscalerKind.Xess;
        if (game.Has(GameFeature.Fsr)) return UpscalerKind.Fsr;
        if (game.Has(GameFeature.Xess)) return UpscalerKind.Xess;
        return UpscalerKind.None;
    }

    public static FrameGenKind PickFrameGen(CatalogGame game, GpuInfo gpu)
    {
        var n = gpu.Name.ToUpperInvariant();
        bool rtx50 = Regex.IsMatch(n, @"RTX\s*50\d0");
        bool rtx40 = Regex.IsMatch(n, @"RTX\s*40\d0");
        if (rtx50 && game.Has(GameFeature.DlssMfg)) return FrameGenKind.DlssMfg;
        if ((rtx50 || rtx40) && game.Has(GameFeature.DlssFg)) return FrameGenKind.DlssFg;
        if (game.Has(GameFeature.FsrFg) && gpu.Score >= 30) return FrameGenKind.FsrFg;
        return FrameGenKind.None;
    }

    private static double FrameGenMultiplier(FrameGenKind fg) => fg switch
    {
        FrameGenKind.DlssMfg => 2.6, // 3x mode, after overhead
        FrameGenKind.DlssFg => 1.75,
        FrameGenKind.FsrFg => 1.8,
        _ => 1
    };

    private static double UpscaleLoss(UpscalerKind kind, Resolution res, int stepIndex)
    {
        double[] perStep = [0, 0.35, 0.35, 0.6, 0.9, 2.5];
        double quality = kind switch
        {
            UpscalerKind.Dlss => 1.0, UpscalerKind.Fsr4 => 1.15, UpscalerKind.Xess => 1.6, UpscalerKind.Tsr => 1.6,
            UpscalerKind.Fsr => 2.3, _ => 4.0
        };
        double resFactor = res.Height switch { >= 2000 => 0.6, >= 1400 => 1.0, _ => 1.7 };
        return perStep[stepIndex] * quality * resFactor;
    }

    private static int PickFpsCap(HardwareProfile hw, OptimizeRequest req, OptimizationPlan plan)
    {
        int refresh = Math.Max(hw.Display.RefreshHz, 30);
        // With VRR, capping a few fps under refresh keeps you inside the VRR window and avoids V-Sync latency.
        int vrrCap = refresh - Math.Max(3, refresh / 40);
        int cap = req.MaxFps;
        if (!req.Competitive && req.MaxFps >= refresh) cap = vrrCap;
        if (plan.EngineCap > 0) cap = Math.Min(cap, plan.EngineCap);
        return Math.Max(cap, 30);
    }

    public static string UpscalerName(UpscalerKind k) => k switch
    {
        UpscalerKind.Dlss => "DLSS", UpscalerKind.Fsr4 => "FSR 4", UpscalerKind.Fsr => "FSR", UpscalerKind.Xess => "XeSS",
        UpscalerKind.Tsr => "TSR", _ => "Native"
    };

    public static string ScaleModeName(int scale) => scale switch
    {
        >= 100 => "Native", >= 77 => "Ultra Quality", >= 67 => "Quality", >= 58 => "Balanced", >= 50 => "Performance", _ => "Ultra Performance"
    };

    // ---------------------------------------------------------------- modern settings list

    private static void BuildModernSettings(OptimizationPlan plan, CatalogGame game, HardwareProfile hw, OptimizeRequest req,
                                            UpscalerKind upscaler, FrameGenKind fgKind, int tex, double vram)
    {
        var gpu = hw.Gpu;
        var m = plan.Modern;

        // Upscaling / anti-aliasing
        if (plan.Upscaler != UpscalerKind.None)
        {
            var detail = plan.Upscaler switch
            {
                UpscalerKind.Dlss => "Use the latest DLSS 4 transformer model (NVIDIA app → DLSS Override → Latest) for the sharpest result.",
                UpscalerKind.Fsr4 => "RDNA 4 ML upscaler. In FSR 3.1 games enable 'FSR 4' via AMD Adrenalin per-game override.",
                UpscalerKind.Xess => gpu.Vendor == GpuVendor.Intel ? "XMX path on Arc — the best XeSS quality." : "DP4a path; usually sharper than FSR 2/3 at the same mode.",
                UpscalerKind.Tsr => "Unreal's built-in temporal upscaler — vendor-neutral and better than FSR 2 in most UE5 titles.",
                _ => "FSR 2/3 temporal upscaling. Add a little sharpening (0.3-0.5)."
            };
            m.Add(new ModernSetting { Name = "Upscaling", Value = $"{UpscalerName(plan.Upscaler)} {ScaleModeName(plan.RenderScale)} ({plan.RenderScale}%)", Detail = detail, Kind = "good" });
        }
        else if (upscaler == UpscalerKind.Dlss && game.Has(GameFeature.Dlaa) && !req.Competitive)
            m.Add(new ModernSetting { Name = "Anti-aliasing", Value = "DLAA (native DLSS)", Detail = "You have headroom — DLAA is the best-looking AA available.", Kind = "good" });
        else if (game.Upscaling)
            m.Add(new ModernSetting
            {
                Name = "Upscaling",
                Value = "Off (native)",
                Detail = req.Competitive
                    ? EyeCandyCategories.Any(c => plan.Levels[c] < QualityLevel.Ultra)
                        ? "Native resolution keeps distant targets sharp — shadows, GI and reflections were lowered first instead."
                        : "Native resolution keeps distant targets sharp, and your target is met without upscaling."
                    : $"Not needed for your target. Best upscaler on your GPU here: {UpscalerName(upscaler)}.",
                Kind = "off"
            });

        // Frame generation
        if (plan.FrameGen != FrameGenKind.None)
        {
            var (name, detail) = plan.FrameGen switch
            {
                FrameGenKind.DlssMfg => ("DLSS Multi Frame Gen 3×", "RTX 50 exclusive. Use 3× (or 4× with a 240 Hz+ display). Keep base fps ≥ 50."),
                FrameGenKind.DlssFg => ("DLSS Frame Generation", "Doubles displayed fps; Reflex is forced on automatically."),
                _ => ("FSR Frame Generation", "Works on any GPU. Pair with Anti-Lag 2 / Reflex if offered to offset latency.")
            };
            m.Add(new ModernSetting { Name = "Frame generation", Value = name, Detail = detail + $" Rendered ≈ {plan.BaseFps:0} fps → shown ≈ {plan.EstimatedFps:0} fps.", Kind = "good" });
        }
        else if (fgKind != FrameGenKind.None || game.Has(GameFeature.DlssFg) || game.Has(GameFeature.FsrFg))
        {
            string why = req.Competitive ? "Adds input latency — keep it off for competitive play."
                       : fgKind == FrameGenKind.None ? "Not available for your GPU in this game."
                       : "Not needed to reach your target. Turn it on if you want to max out a high-refresh display.";
            m.Add(new ModernSetting { Name = "Frame generation", Value = "Off", Detail = why, Kind = "off" });
        }

        // Latency
        if (gpu.Vendor == GpuVendor.Nvidia && game.Has(GameFeature.Reflex))
            m.Add(new ModernSetting { Name = "NVIDIA Reflex", Value = plan.CpuBound ? "On" : "On + Boost", Detail = "Cuts render queue latency. Boost keeps GPU clocks up when CPU-bound.", Kind = "good" });
        else if (gpu.Vendor == GpuVendor.Amd && game.Has(GameFeature.AntiLag2))
            m.Add(new ModernSetting { Name = "AMD Anti-Lag 2", Value = "On", Detail = "In-game Anti-Lag 2 is far more effective than the driver toggle.", Kind = "good" });
        else if (gpu.Vendor == GpuVendor.Nvidia)
            m.Add(new ModernSetting { Name = "Low Latency Mode", Value = req.Competitive ? "Ultra (driver)" : "On (driver)", Detail = "No Reflex here — use NVIDIA Control Panel › Low Latency Mode.", Kind = "info" });
        else if (gpu.Vendor == GpuVendor.Amd)
            m.Add(new ModernSetting { Name = "Radeon Anti-Lag", Value = "On (driver)", Detail = "Enable in Adrenalin for this game.", Kind = "info" });

        // Ray tracing
        if (game.Has(GameFeature.RayTracing) || game.Has(GameFeature.PathTracing))
        {
            var (value, detail, kind) = plan.RayTracing switch
            {
                RayTracingMode.PathTracing => ("Path tracing", game.Has(GameFeature.RayRecon) ? "Turn on DLSS Ray Reconstruction — it denoises far better than the default." : "Full path tracing fits your budget.", "good"),
                RayTracingMode.On => ("On (Medium/High)", game.Has(GameFeature.RayRecon) && gpu.SupportsDlss ? "Enable Ray Reconstruction for cleaner RT reflections." : "Reflections first, then GI; RT shadows are the least visible upgrade.", "good"),
                _ => ("Off", req.Competitive ? "Costs fps with no competitive benefit." :
                    !PerformanceModel.SupportsHardwareRt(gpu) ? "Your GPU has no (or weak) ray tracing hardware." : "Doesn't fit your fps target on this hardware.", "off")
            };
            m.Add(new ModernSetting { Name = "Ray tracing", Value = value, Detail = detail, Kind = kind });
        }

        // Sync & cap
        int refresh = hw.Display.RefreshHz;
        m.Add(new ModernSetting
        {
            Name = "V-Sync",
            Value = "Off (in-game)",
            Detail = refresh > 60 ? $"Use G-Sync/FreeSync with the fps cap below; if you see tearing, enable V-Sync in the driver instead of in-game." : "Turn on if you see tearing on a 60 Hz display.",
            Kind = "off"
        });
        m.Add(new ModernSetting { Name = "Frame rate cap", Value = $"{plan.FpsCap} fps", Detail = plan.FpsCap < refresh ? "Even frame pacing beats an unstable uncapped number." : $"Just under your {refresh} Hz refresh keeps VRR engaged.", Kind = "good" });

        // Textures / VRAM
        m.Add(new ModernSetting
        {
            Name = "Texture quality",
            Value = QualityLevel.Name(tex),
            Detail = gpu.IsIntegrated ? "Integrated GPU shares system RAM — keep textures modest." : $"Chosen for {vram:0.#} GB VRAM. Textures cost almost no fps until VRAM runs out — then stutter.",
            Kind = "info"
        });

        // API choice
        if (game.Has(GameFeature.Vulkan) && (gpu.Vendor == GpuVendor.Amd || game.Has(GameFeature.Dx11)))
            m.Add(new ModernSetting { Name = "Graphics API", Value = "Vulkan", Detail = "Lower CPU overhead and better 1% lows in this title.", Kind = "good" });
        else if (game.Has(GameFeature.Dx11) && gpu.Score < 45 && plan.RayTracing == RayTracingMode.Off && plan.FrameGen == FrameGenKind.None)
            m.Add(new ModernSetting { Name = "Graphics API", Value = "DX11", Detail = "Older GPUs often run this game's DX11 path more smoothly.", Kind = "good" });
        else if (game.Has(GameFeature.Dx12))
            m.Add(new ModernSetting { Name = "Graphics API", Value = "DX12", Detail = "Required for RT, frame gen and the newest upscalers.", Kind = "good" });

        if (game.Has(GameFeature.Hdr))
            m.Add(new ModernSetting { Name = "HDR", Value = "On if your display supports it", Detail = "Calibrate peak brightness with the Windows HDR Calibration app first.", Kind = "info" });

        m.Add(new ModernSetting
        {
            Name = "Motion blur / DoF / film grain",
            Value = req.Competitive ? "Off" : "Off (taste)",
            Detail = "Cheap on fps, but they smear detail — especially with upscaling.",
            Kind = "off"
        });
    }

    private static void BuildNotes(OptimizationPlan plan, CatalogGame game, HardwareProfile hw, OptimizeRequest req, int tex, double vram)
    {
        var n = plan.Notes;
        if (plan.EngineCap > 0 && req.MinFps > plan.EngineCap)
            n.Add($"{game.Name} is locked to {plan.EngineCap} fps by the game itself — no hardware can go past it, so Sentinel plans for {plan.EngineCap}.");
        else if (!plan.TargetReached)
            n.Add($"Your PC tops out around {plan.Ceiling:0} fps in this game at {req.Resolution}, even with every setting on Low" +
                  (req.AllowUpscaling && game.Upscaling ? " and maximum upscaling" : "") + $". Sentinel won't promise {req.MinFps} — it planned for the best this hardware can do. " +
                  (plan.CpuBound
                      ? "The CPU is the limit here" + (game.Has(GameFeature.DlssFg) || game.Has(GameFeature.FsrFg) ? "; frame generation is the only setting that gets around it." : "; enabling XMP/EXPO and closing background apps helps most.")
                      : "Lowering the resolution is the remaining lever."));
        else if (plan.CpuBound && plan.CpuFps < req.MaxFps)
            n.Add($"CPU-bound at ≈{plan.CpuFps:0} fps — the GPU has spare room, so graphics were kept higher.");

        if (plan.EstimatedFps > req.MaxFps * 1.25 && !req.Competitive)
            n.Add("Plenty of headroom: you could raise resolution or enable more eye candy.");
        if (req.MaxFps > hw.Display.RefreshHz && hw.Display.RefreshHz > 0)
            n.Add($"Your display shows at most {hw.Display.RefreshHz} Hz. Frames above that can lower input latency, but you won't see them.");
        if (hw.System.OnBattery)
            n.Add("Running on battery — plug in for full GPU/CPU clocks.");
        if (tex < QualityLevel.High && !hw.Gpu.IsIntegrated)
            n.Add($"Textures held at {QualityLevel.Name(tex)} because {vram:0.#} GB VRAM is tight for this game at {req.Resolution}.");
        if (!string.IsNullOrWhiteSpace(game.Tip))
            n.Add(game.Tip!);
        if (game.IsEstimated)
            n.Add("This game's demand data is an AI estimate rather than hand-checked, so Sentinel uses a wider safety margin. Verify with an in-game benchmark.");
    }
}
