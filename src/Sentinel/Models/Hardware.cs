namespace Sentinel.Models;

public enum GpuVendor { Unknown, Nvidia, Amd, Intel }

public sealed class CpuInfo
{
    public string Name { get; set; } = "Unknown CPU";
    public int Cores { get; set; }
    public int Threads { get; set; }
    public int MaxClockMhz { get; set; }
    /// <summary>Gaming performance index. Ryzen 5 5600X = 100.</summary>
    public double Score { get; set; } = 60;
    public string? MatchedModel { get; set; }
    public bool IsLaptopChip { get; set; }
}

public sealed class GpuInfo
{
    public string Name { get; set; } = "Unknown GPU";
    public GpuVendor Vendor { get; set; }
    public long VramBytes { get; set; }
    public string? DriverVersion { get; set; }
    public DateTime? DriverDate { get; set; }
    public bool IsIntegrated { get; set; }
    /// <summary>Raster performance index. RTX 3070 = 100.</summary>
    public double Score { get; set; } = 30;
    public string? MatchedModel { get; set; }
    public bool SupportsDlss => Vendor == GpuVendor.Nvidia && Name.Contains("RTX", StringComparison.OrdinalIgnoreCase);
    public bool SupportsFrameGen => SupportsDlss && System.Text.RegularExpressions.Regex.IsMatch(Name, @"RTX\s*[45]0\d0");
    public double VramGb => VramBytes / 1024d / 1024 / 1024;
}

public sealed class MemoryInfo
{
    public long TotalBytes { get; set; }
    /// <summary>Sum of installed DIMM capacity (TotalBytes excludes hardware-reserved memory).</summary>
    public long InstalledBytes { get; set; }
    public int ConfiguredSpeedMhz { get; set; }
    public int RatedSpeedMhz { get; set; }
    public int ModuleCount { get; set; }
    public string Type { get; set; } = "";
    public int LoadPercent { get; set; }
    public double TotalGb => TotalBytes / 1024d / 1024 / 1024;
    public double InstalledGb => (InstalledBytes > 0 ? InstalledBytes : TotalBytes) / 1024d / 1024 / 1024;
}

public sealed class DiskInfo
{
    public string Root { get; set; } = "";
    public string Label { get; set; } = "";
    public string Model { get; set; } = "";
    public string MediaType { get; set; } = "Unknown"; // SSD, HDD, Unknown
    public string Bus { get; set; } = "";              // NVMe, SATA, USB...
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public bool IsSsd => MediaType == "SSD";
    public double FreePercent => TotalBytes == 0 ? 0 : FreeBytes * 100d / TotalBytes;
}

public sealed class DisplayInfo
{
    public string DeviceName { get; set; } = "";
    public string FriendlyName { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefreshHz { get; set; }
    public int MaxRefreshHz { get; set; }
    public bool IsPrimary { get; set; }
}

public sealed class SystemState
{
    public string OsName { get; set; } = "Windows";
    public string OsBuild { get; set; } = "";
    public bool HasBattery { get; set; }
    public bool OnBattery { get; set; }
    public string PowerPlanName { get; set; } = "Unknown";
    public Guid PowerPlanGuid { get; set; }
    public bool? GameModeEnabled { get; set; }
    public bool? HagsEnabled { get; set; }
    public bool? GameDvrEnabled { get; set; }
    public bool? VbsRunning { get; set; }
    public bool? HvciRunning { get; set; }
    public bool? MouseAccelerationOn { get; set; }
    public bool? WindowedOptimizations { get; set; }
    public int StartupAppCount { get; set; }
    public List<string> RunningOverlays { get; set; } = [];
}

public sealed class HardwareProfile
{
    public CpuInfo Cpu { get; set; } = new();
    public List<GpuInfo> Gpus { get; set; } = [];
    public GpuInfo Gpu { get; set; } = new();
    public MemoryInfo Memory { get; set; } = new();
    public List<DiskInfo> Disks { get; set; } = [];
    public List<DisplayInfo> Displays { get; set; } = [];
    public DisplayInfo Display { get; set; } = new() { Width = 1920, Height = 1080, RefreshHz = 60, MaxRefreshHz = 60 };
    public SystemState System { get; set; } = new();

    public bool IsHybridGraphics => Gpus.Any(g => g.IsIntegrated) && Gpus.Any(g => !g.IsIntegrated);

    /// <summary>0-100 overall gaming score blending GPU (dominant), CPU and RAM.</summary>
    public int OverallScore
    {
        get
        {
            double gpu = Math.Min(Gpu.Score / 2.2, 100);       // ~RTX 4090 caps the scale
            double cpu = Math.Min(Cpu.Score / 1.6, 100);       // ~9800X3D caps the scale
            double ram = Math.Min(Memory.TotalGb / 32 * 100, 100);
            return (int)Math.Round(gpu * 0.65 + cpu * 0.27 + ram * 0.08);
        }
    }

    public string Tier => OverallScore switch
    {
        >= 80 => "Enthusiast",
        >= 62 => "High-end",
        >= 45 => "Upper mid-range",
        >= 30 => "Mid-range",
        >= 18 => "Entry level",
        _ => "Low-end"
    };
}
