using Sentinel.Models;
using Sentinel.Services.Catalog;
using Sentinel.Services.Hardware;

namespace Sentinel.Services;

/// <summary>
/// The fictional PC and game library shown with `--demo`. Every name, spec and path here is invented:
/// nothing comes from the machine it runs on, so screenshots can be shared safely.
/// </summary>
public static class DemoData
{
    private const string SteamApps = @"C:\Program Files (x86)\Steam\steamapps\common\";
    private const string UserProfile = @"C:\Users\Player\";

    /// <param name="Config">Config file shown for auto-tunable games, standing in for the real lookup (which would show this PC's user folder).</param>
    private sealed record DemoGame(string Name, Launcher Launcher, string InstallPath, string? Config = null);

    // A spread of launchers and engines. The two games on D:\ sit on its hard drive, so PC Tweaks has a warning to show.
    private static readonly DemoGame[] Library =
    [
        new("Baldur's Gate 3", Launcher.Steam, SteamApps + "Baldurs Gate 3"),
        new("Battlefield 6", Launcher.EA, @"C:\Program Files\EA Games\Battlefield 6"),
        new("Black Myth: Wukong", Launcher.Steam, SteamApps + "BlackMythWukong", UserProfile + @"AppData\Local\b1\Saved\Config\Windows\GameUserSettings.ini"),
        new("Cyberpunk 2077", Launcher.GOG, @"C:\GOG Games\Cyberpunk 2077"),
        new("Diablo IV", Launcher.BattleNet, @"C:\Program Files (x86)\Diablo IV"),
        new("Elden Ring", Launcher.Steam, SteamApps + "ELDEN RING"),
        new("Forza Horizon 5", Launcher.Xbox, @"C:\XboxGames\Forza Horizon 5\Content"),
        new("Helldivers 2", Launcher.Steam, SteamApps + "Helldivers 2"),
        new("Hogwarts Legacy", Launcher.Epic, @"C:\Program Files\Epic Games\HogwartsLegacy", UserProfile + @"AppData\Local\Hogwarts Legacy\Saved\Config\WindowsNoEditor\GameUserSettings.ini"),
        new("League of Legends", Launcher.Riot, @"C:\Riot Games\League of Legends", @"C:\Riot Games\League of Legends\Config\game.cfg"),
        new("Marvel Rivals", Launcher.Steam, SteamApps + "MarvelRivals", UserProfile + @"AppData\Local\Marvel\Saved\Config\Windows\GameUserSettings.ini"),
        new("Palworld", Launcher.Steam, SteamApps + "Palworld", UserProfile + @"AppData\Local\Pal\Saved\Config\Windows\GameUserSettings.ini"),
        new("Red Dead Redemption 2", Launcher.Rockstar, @"C:\Program Files\Rockstar Games\Red Dead Redemption 2", UserProfile + @"Documents\Rockstar Games\Red Dead Redemption 2\Settings\system.xml"),
        new("Stardew Valley", Launcher.Steam, @"D:\SteamLibrary\steamapps\common\Stardew Valley"),
        new("The Witcher 3: Wild Hunt", Launcher.GOG, @"D:\GOG Games\The Witcher 3 Wild Hunt"),
    ];

    public static HardwareProfile Hardware()
    {
        // Scored through the same tables as real detection, so the demo's numbers behave like a real RTX 4070 SUPER / 7800X3D.
        var gpu = new GpuInfo
        {
            Name = "NVIDIA GeForce RTX 4070 SUPER", Vendor = GpuVendor.Nvidia, VramBytes = 12L << 30,
            DriverVersion = "32.0.15.8129", DriverDate = DateTime.Today.AddDays(-38),
        };
        (gpu.Score, gpu.MatchedModel, gpu.IsIntegrated) = HardwareDatabase.MatchGpu(gpu.Name, gpu.VramBytes);

        var cpu = new CpuInfo { Name = "AMD Ryzen 7 7800X3D 8-Core Processor", Cores = 8, Threads = 16, MaxClockMhz = 4201 };
        (cpu.Score, cpu.MatchedModel, cpu.IsLaptopChip) = HardwareDatabase.MatchCpu(cpu.Name, cpu.Cores, cpu.Threads, cpu.MaxClockMhz);

        // Running at 144 Hz on a 165 Hz panel, a classic the Tweaks page catches.
        var display = new DisplayInfo
        {
            DeviceName = @"\\.\DISPLAY1", FriendlyName = "QHD Gaming Monitor", Width = 2560, Height = 1440,
            RefreshHz = 144, MaxRefreshHz = 165, IsPrimary = true,
        };

        return new HardwareProfile
        {
            Cpu = cpu,
            Gpus = [gpu],
            Gpu = gpu,
            Memory = new MemoryInfo
            {
                TotalBytes = 34_145_000_000, InstalledBytes = 32L << 30, ConfiguredSpeedMhz = 6000, RatedSpeedMhz = 6000,
                ModuleCount = 2, Type = "DDR5", LoadPercent = 41,
            },
            Disks =
            [
                new DiskInfo { Root = @"C:\", Label = "Windows", Model = "NVMe SSD 1TB", MediaType = "SSD", Bus = "NVMe", TotalBytes = 1_000_202_043_392, FreeBytes = 462_116_000_000 },
                new DiskInfo { Root = @"D:\", Label = "Games", Model = "SATA HDD 2TB", MediaType = "HDD", Bus = "SATA", TotalBytes = 2_000_396_321_280, FreeBytes = 1_213_448_000_000 },
            ],
            Displays = [display],
            Display = display,
            System = new SystemState
            {
                OsName = "Windows 11 Pro 24H2", OsBuild = "26100.4946",
                PowerPlanName = "High performance", PowerPlanGuid = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"),
                GameModeEnabled = true, HagsEnabled = true, GameDvrEnabled = true, VbsRunning = false, HvciRunning = false,
                MouseAccelerationOn = true, WindowedOptimizations = true, StartupAppCount = 13,
                RunningOverlays = ["NVIDIA overlay", "MSI Afterburner", "RivaTuner Statistics Server"],
            },
        };
    }

    public static List<InstalledGame> Games() =>
        Library.Select(d =>
        {
            var catalog = GameCatalog.Instance.Match(d.Name);
            return new InstalledGame
            {
                Name = d.Name,
                Launcher = d.Launcher,
                InstallPath = d.InstallPath,
                AppId = d.Launcher == Launcher.Steam && catalog is { SteamIds.Length: > 0 } ? catalog.SteamIds[0].ToString() : null,
                Catalog = catalog,
                Engine = catalog?.Engine ?? "Unknown",
            };
        }).OrderBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();

    public static string? ConfigPath(InstalledGame? game) => Library.FirstOrDefault(d => d.Name == game?.Name)?.Config;
}
