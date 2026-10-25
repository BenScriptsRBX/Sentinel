using System.Text.Json.Serialization;

namespace Sentinel.Models;

/// <summary>One entry of the embedded game catalog (Data/Catalog/*.json).</summary>
public sealed class CatalogGame
{
    [JsonPropertyName("n")] public string Name { get; set; } = "";
    [JsonPropertyName("a")] public string[] Aliases { get; set; } = [];
    [JsonPropertyName("s")] public int[] SteamIds { get; set; } = [];
    [JsonPropertyName("x")] public string[] Exes { get; set; } = [];
    [JsonPropertyName("e")] public string Engine { get; set; } = "Proprietary";
    [JsonPropertyName("g")] public string Genre { get; set; } = "";
    /// <summary>GPU demand 1 (very light) .. 10 (extremely heavy).</summary>
    [JsonPropertyName("gpu")] public int GpuDemand { get; set; } = 5;
    /// <summary>CPU demand 1 (very light) .. 10 (extremely heavy).</summary>
    [JsonPropertyName("cpu")] public int CpuDemand { get; set; } = 5;
    [JsonPropertyName("c")] public bool Competitive { get; set; }
    /// <summary>Config profile id from profiles.json, or "ue:ProjectName" for Unreal titles.</summary>
    [JsonPropertyName("cfg")] public string? Profile { get; set; }
    [JsonPropertyName("y")] public int Year { get; set; }
    /// <summary>Hard frame-rate lock of the engine (e.g. 60 for most fighting games); 0 = uncapped.</summary>
    [JsonPropertyName("cap")] public int EngineFpsCap { get; set; }
    /// <summary>1 = demand/feature data was AI-estimated rather than hand-curated, so plans use a wider safety margin.</summary>
    [JsonPropertyName("q")] public int EstimatedData { get; set; }
    [JsonIgnore] public bool IsEstimated => EstimatedData == 1;
    /// <summary>Steam popularity rank by owners (1 = most owned); 0 = unknown.</summary>
    [JsonPropertyName("r")] public int Rank { get; set; }
    /// <summary>Non-Steam stores/launchers that sell the game (e.g. "Epic", "GOG", "Battle.net").</summary>
    [JsonPropertyName("l")] public string[] Stores { get; set; } = [];
    /// <summary>Wide store art for games without a Steam app id (e.g. GOG horizontal cover).</summary>
    [JsonPropertyName("img")] public string? ArtUrl { get; set; }

    /// <summary>Every store we know sells it, Steam first.</summary>
    [JsonIgnore] public IEnumerable<string> AllStores => (SteamIds.Length > 0 ? ["Steam"] : Array.Empty<string>()).Concat(Stores).Distinct();

    /// <summary>A–Z key that ignores leading punctuation, so "!AnyWay!" files under A.</summary>
    [JsonIgnore] public string SortName => Name.TrimStart(' ', '!', '"', '#', '$', '\'', '(', ')', '-', '.', '[', '*', '~', '¡', '¿');
    [JsonPropertyName("t")] public string? Tip { get; set; }
    /// <summary>Modern rendering features the game exposes. See <see cref="GameFeature"/> for the codes.</summary>
    [JsonPropertyName("f")] public string[] Features { get; set; } = [];

    [JsonIgnore] public string Id => Services.Catalog.NameNormalizer.Normalize(Name).Replace(' ', '-');
    public bool Has(string feature) => Features.Contains(feature, StringComparer.OrdinalIgnoreCase);
    [JsonIgnore] public bool Upscaling => Has(GameFeature.Dlss) || Has(GameFeature.Fsr) || Has(GameFeature.Xess) || Has(GameFeature.Tsr);
}

/// <summary>Feature codes used in the catalog's "f" array.</summary>
public static class GameFeature
{
    public const string Dlss = "dlss";         // DLSS Super Resolution
    public const string DlssFg = "fg";         // DLSS 3 Frame Generation (RTX 40+)
    public const string DlssMfg = "mfg";       // DLSS 4 Multi Frame Generation (RTX 50)
    public const string RayRecon = "rr";       // DLSS Ray Reconstruction
    public const string Dlaa = "dlaa";         // DLAA (native-res DLSS AA)
    public const string Fsr = "fsr";           // AMD FSR 2/3 upscaling
    public const string Fsr4 = "fsr4";         // FSR 4 ML upscaling (RDNA 4), incl. driver upgrade path from FSR 3.1
    public const string FsrFg = "afg";         // FSR 3 frame generation (any GPU)
    public const string Xess = "xess";         // Intel XeSS
    public const string Tsr = "tsr";           // Unreal Temporal Super Resolution
    public const string Reflex = "reflex";     // NVIDIA Reflex
    public const string AntiLag2 = "al2";      // AMD Anti-Lag 2
    public const string RayTracing = "rt";     // Hardware ray tracing options
    public const string PathTracing = "pt";    // Full path tracing mode
    public const string Hdr = "hdr";           // Native HDR output
    public const string Dx12 = "dx12";         // DX12 renderer selectable
    public const string Vulkan = "vk";         // Vulkan renderer selectable
    public const string Dx11 = "dx11";         // DX11 renderer selectable (often better on old GPUs)

    public static string Label(string code) => code switch
    {
        Dlss => "DLSS", DlssFg => "DLSS Frame Gen", DlssMfg => "DLSS 4 Multi Frame Gen", RayRecon => "Ray Reconstruction",
        Dlaa => "DLAA", Fsr => "FSR", Fsr4 => "FSR 4", FsrFg => "FSR Frame Gen", Xess => "XeSS", Tsr => "TSR",
        Reflex => "Reflex", AntiLag2 => "Anti-Lag 2", RayTracing => "Ray tracing", PathTracing => "Path tracing",
        Hdr => "HDR", Dx12 => "DX12", Vulkan => "Vulkan", Dx11 => "DX11", _ => code.ToUpperInvariant()
    };
}

public enum Launcher { Steam, Epic, GOG, Ubisoft, EA, BattleNet, Riot, Xbox, Rockstar, Standalone, Other }

public sealed class InstalledGame
{
    public string Name { get; set; } = "";
    public Launcher Launcher { get; set; }
    public string? InstallPath { get; set; }
    public string? AppId { get; set; }
    public long SizeBytes { get; set; }
    public CatalogGame? Catalog { get; set; }
    public string Engine { get; set; } = "Unknown";
    public string? UnrealProject { get; set; }
    public (string Company, string Product)? UnityInfo { get; set; }
    public string? MainExe { get; set; }
    /// <summary>Exe used only for the icon when the game has no exe of its own (e.g. Minecraft Java).</summary>
    public string? IconPath { get; set; }

    public string DisplayName => Catalog?.Name ?? Name;
    /// <summary>Stable id for backups and saved targets; matches <see cref="CatalogGame.Id"/> for catalogued games.</summary>
    public string BackupId => Catalog?.Id ?? Services.Catalog.NameNormalizer.Normalize(Name).Replace(' ', '-');
    public bool InCatalog => Catalog is not null;

    public string LauncherLabel => Launcher switch
    {
        Launcher.BattleNet => "Battle.net",
        Launcher.EA => "EA app",
        Launcher.Xbox => "Xbox / MS Store",
        _ => Launcher.ToString()
    };
}
