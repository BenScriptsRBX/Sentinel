using Sentinel.Models;

namespace Sentinel.Tests;

internal static class TestData
{
    static TestData()
    {
        // Keep backups/settings written by tests out of the real %LOCALAPPDATA%\Sentinel.
        Environment.SetEnvironmentVariable("SENTINEL_DATA", Path.Combine(Path.GetTempPath(), "sentinel-tests"));
    }

    public static void Init() { }

    public static HardwareProfile Rig(string gpu, double gpuScore, double vramGb, GpuVendor vendor, double cpuScore = 150, int refresh = 165) => new()
    {
        Cpu = new CpuInfo { Name = "Test CPU", Cores = 8, Threads = 16, Score = cpuScore },
        Gpu = new GpuInfo { Name = gpu, Score = gpuScore, VramBytes = (long)(vramGb * 1024 * 1024 * 1024), Vendor = vendor },
        Memory = new MemoryInfo { TotalBytes = 32L * 1024 * 1024 * 1024, ModuleCount = 2, Type = "DDR5", ConfiguredSpeedMhz = 6000 },
        Display = new DisplayInfo { Width = 2560, Height = 1440, RefreshHz = refresh, MaxRefreshHz = refresh },
    };

    public static HardwareProfile Rtx4070 => Rig("NVIDIA GeForce RTX 4070", 122, 12, GpuVendor.Nvidia);
    public static HardwareProfile Rx9070Xt => Rig("AMD Radeon RX 9070 XT", 170, 16, GpuVendor.Amd);
    public static HardwareProfile Gtx1650 => Rig("NVIDIA GeForce GTX 1650", 27, 4, GpuVendor.Nvidia, cpuScore: 70, refresh: 60);

    public static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sentinel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
