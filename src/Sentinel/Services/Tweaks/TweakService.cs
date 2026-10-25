using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Sentinel.Core;
using Sentinel.Models;
using Sentinel.Services.Hardware;

namespace Sentinel.Services.Tweaks;

/// <summary>Builds the PC recommendation list from the detected hardware/Windows state. Every apply is reversible.</summary>
public static class TweakService
{
    private static readonly Guid HighPerformance = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private static readonly Guid UltimatePerformance = Guid.Parse("e9a42b02-d5df-448d-aa00-03f14749eb61");
    private const string GpuPrefsKey = @"Software\Microsoft\DirectX\UserGpuPreferences";

    public static List<Tweak> Build(HardwareProfile hw, IReadOnlyList<InstalledGame> games)
    {
        var list = new List<Tweak>();
        var s = hw.System;
        var state = TweakState.Load();

        // ------------------------------------------------------------ Power
        bool perfPlan = s.PowerPlanGuid == HighPerformance || s.PowerPlanGuid == UltimatePerformance ||
                        Regex.IsMatch(s.PowerPlanName, "performance|ultimate|gaming", RegexOptions.IgnoreCase);
        list.Add(new Tweak
        {
            Id = "power-plan",
            Category = "Power",
            Title = "High performance power plan",
            Impact = hw.System.HasBattery ? "Medium" : "Low",
            Description = perfPlan
                ? $"Active plan: {s.PowerPlanName}."
                : $"Active plan is '{s.PowerPlanName}'. High performance keeps CPU clocks and PCIe links from parking mid-game, which smooths 1% lows." +
                  (s.HasBattery ? " On laptops this increases battery drain — it's only applied while you choose it." : ""),
            Status = perfPlan ? TweakStatus.Optimal : TweakStatus.Recommended,
            CanRevert = state.Has("power-plan"),
            ApplyAction = () => Task.Run(() =>
            {
                state.Remember("power-plan", s.PowerPlanGuid.ToString());
                var output = HardwareDetector.RunCapture("powercfg", $"/setactive {HighPerformance}");
                if (output.Contains("does not exist", StringComparison.OrdinalIgnoreCase) || output.Contains("Invalid", StringComparison.OrdinalIgnoreCase) || !ActivePlanIs(HighPerformance))
                {
                    // Modern Standby machines hide the plan; duplicating it brings it back.
                    var dup = HardwareDetector.RunCapture("powercfg", $"-duplicatescheme {HighPerformance}");
                    var m = Regex.Match(dup, @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase);
                    if (m.Success) HardwareDetector.RunCapture("powercfg", $"/setactive {m.Value}");
                }
                return "Switched to High performance.";
            }),
            RevertAction = () => Task.Run(() =>
            {
                var prev = state.Get("power-plan");
                if (prev is null) return "Nothing to revert.";
                HardwareDetector.RunCapture("powercfg", $"/setactive {prev}");
                state.Forget("power-plan");
                return "Previous power plan restored.";
            }),
        });

        if (s.OnBattery)
            list.Add(Info("battery", "Power", "Running on battery", "Laptops cut GPU power by 50-80% on battery. Plug in before gaming.", "High", TweakStatus.Warning));

        // ------------------------------------------------------------ Windows gaming features
        list.Add(RegistryToggle(state, "game-mode", "Windows", "Game Mode",
            "Prioritises the game's threads and pauses Windows Update installs while you play.",
            Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1, s.GameModeEnabled == true, "Low"));

        list.Add(RegistryToggle(state, "game-dvr", "Windows", "Turn off background recording",
            "Xbox Game Bar's 'Record what happened' constantly encodes video in the background, costing a few % fps and VRAM.",
            Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled", 0, s.GameDvrEnabled == false, "Medium"));

        bool hagsCapable = HagsCapable(hw.Gpu);
        if (hagsCapable)
        {
            bool needsForFg = Regex.IsMatch(hw.Gpu.Name, @"RTX\s*[45]0\d0", RegexOptions.IgnoreCase);
            list.Add(new Tweak
            {
                Id = "hags",
                Category = "Windows",
                Title = "Hardware-accelerated GPU scheduling",
                Impact = needsForFg ? "High" : "Low",
                RequiresAdmin = true,
                RequiresReboot = true,
                Description = (needsForFg ? "Required for DLSS Frame Generation on your RTX GPU. " : "") +
                              "Lets the GPU manage its own memory scheduling, reducing CPU overhead and latency slightly." +
                              (s.HagsEnabled is null ? " (Current state could not be read.)" : ""),
                Status = s.HagsEnabled == true ? TweakStatus.Optimal : TweakStatus.Recommended,
                CanRevert = state.Has("hags"),
                ApplyAction = async () =>
                {
                    state.Remember("hags", s.HagsEnabled == false ? "1" : "");
                    return await RunElevated("reg", @"add ""HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers"" /v HwSchMode /t REG_DWORD /d 2 /f")
                        ? "Enabled. Restart Windows to take effect." : "Cancelled (administrator permission is required).";
                },
                RevertAction = async () =>
                {
                    var prev = state.Get("hags");
                    var args = string.IsNullOrEmpty(prev)
                        ? @"delete ""HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers"" /v HwSchMode /f"
                        : $@"add ""HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers"" /v HwSchMode /t REG_DWORD /d {prev} /f";
                    if (!await RunElevated("reg", args)) return "Cancelled.";
                    state.Forget("hags");
                    return "Reverted. Restart Windows to take effect.";
                },
            });
        }

        if (int.TryParse(s.OsBuild.Split('.')[0], out var build) && build >= 22000)
        {
            list.Add(new Tweak
            {
                Id = "windowed-opt",
                Category = "Windows",
                Title = "Optimizations for windowed games + VRR",
                Impact = "Medium",
                Description = "Upgrades DX10/11 borderless games to the flip presentation model: lower latency, VRR and Auto HDR in windowed mode.",
                Status = s.WindowedOptimizations == true ? TweakStatus.Optimal : TweakStatus.Recommended,
                CanRevert = state.Has("windowed-opt"),
                ApplyAction = () => Task.Run(() =>
                {
                    using var k = Registry.CurrentUser.CreateSubKey(GpuPrefsKey);
                    var current = k.GetValue("DirectXUserGlobalSettings") as string ?? "";
                    state.Remember("windowed-opt", current);
                    k.SetValue("DirectXUserGlobalSettings", SetFlag(SetFlag(current, "SwapEffectUpgradeEnable", "1"), "VRROptimizeEnable", "1"));
                    return "Enabled. Restart running games to pick it up.";
                }),
                RevertAction = () => Task.Run(() =>
                {
                    using var k = Registry.CurrentUser.CreateSubKey(GpuPrefsKey);
                    var prev = state.Get("windowed-opt") ?? "";
                    if (prev.Length == 0) k.DeleteValue("DirectXUserGlobalSettings", false);
                    else k.SetValue("DirectXUserGlobalSettings", prev);
                    state.Forget("windowed-opt");
                    return "Reverted.";
                }),
            });
        }

        // ------------------------------------------------------------ Display
        var d = hw.Display;
        if (d.MaxRefreshHz > d.RefreshHz + 1)
        {
            list.Add(new Tweak
            {
                Id = "refresh-rate",
                Category = "Display",
                Title = $"Run your display at {d.MaxRefreshHz} Hz",
                Impact = "High",
                Description = $"{d.FriendlyName} is running at {d.RefreshHz} Hz but supports {d.MaxRefreshHz} Hz at {d.Width}×{d.Height}. Many high-refresh monitors ship at 60 Hz by default.",
                Status = TweakStatus.Recommended,
                CanRevert = state.Has("refresh-rate"),
                ApplyAction = () => Task.Run(() =>
                {
                    state.Remember("refresh-rate", d.RefreshHz.ToString());
                    return SetRefreshRate(d.DeviceName, d.MaxRefreshHz) ? $"Display set to {d.MaxRefreshHz} Hz." : "Windows rejected the mode change.";
                }),
                RevertAction = () => Task.Run(() =>
                {
                    if (!int.TryParse(state.Get("refresh-rate"), out var prev)) return "Nothing to revert.";
                    SetRefreshRate(d.DeviceName, prev);
                    state.Forget("refresh-rate");
                    return $"Display set back to {prev} Hz.";
                }),
            });
        }
        else
            list.Add(Info("refresh-ok", "Display", $"Display at {d.RefreshHz} Hz", $"{d.FriendlyName} is already at its highest refresh rate for {d.Width}×{d.Height}.", "Low", TweakStatus.Optimal));

        // ------------------------------------------------------------ Input
        list.Add(new Tweak
        {
            Id = "mouse-accel",
            Category = "Input",
            Title = "Disable mouse acceleration",
            Impact = "Medium",
            Description = "'Enhance pointer precision' makes aim depend on how fast you move the mouse — bad for muscle memory in shooters.",
            Status = s.MouseAccelerationOn == true ? TweakStatus.Recommended : TweakStatus.Optimal,
            CanRevert = state.Has("mouse-accel"),
            ApplyAction = () => Task.Run(() =>
            {
                using var k = Registry.CurrentUser.CreateSubKey(@"Control Panel\Mouse");
                state.Remember("mouse-accel", $"{k.GetValue("MouseSpeed")}|{k.GetValue("MouseThreshold1")}|{k.GetValue("MouseThreshold2")}");
                SetMouse(k, "0", "0", "0");
                return "Mouse acceleration off.";
            }),
            RevertAction = () => Task.Run(() =>
            {
                var prev = (state.Get("mouse-accel") ?? "1|6|10").Split('|');
                using var k = Registry.CurrentUser.CreateSubKey(@"Control Panel\Mouse");
                SetMouse(k, prev[0], prev.ElementAtOrDefault(1) ?? "6", prev.ElementAtOrDefault(2) ?? "10");
                state.Forget("mouse-accel");
                return "Restored previous mouse settings.";
            }),
        });

        // ------------------------------------------------------------ Graphics
        if (hw.IsHybridGraphics)
        {
            var exes = games.Select(g => g.MainExe).Where(e => e is not null && File.Exists(e)).Select(e => e!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            list.Add(new Tweak
            {
                Id = "gpu-preference",
                Category = "Graphics",
                Title = $"Run games on the {hw.Gpu.Name}",
                Impact = "High",
                Description = $"This PC has integrated + dedicated graphics. Sets Windows' per-app GPU preference to High performance for {exes.Count} detected game executable(s).",
                // Already applied (e.g. in an earlier session) means optimal, not "recommended" again.
                Status = exes.Count == 0 ? TweakStatus.Info
                       : exes.All(HasHighPerformanceGpu) ? TweakStatus.Optimal
                       : TweakStatus.Recommended,
                CanRevert = state.Has("gpu-preference"),
                ApplyAction = exes.Count == 0 ? null : () => Task.Run(() =>
                {
                    var added = SetHighPerformanceGpu(exes);
                    state.Remember("gpu-preference", string.Join("\n", added));
                    return $"High-performance GPU set for {exes.Count} game(s).";
                }),
                RevertAction = () => Task.Run(() =>
                {
                    using var k = Registry.CurrentUser.CreateSubKey(GpuPrefsKey);
                    foreach (var exe in (state.Get("gpu-preference") ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries)) k.DeleteValue(exe, false);
                    state.Forget("gpu-preference");
                    return "GPU preferences removed.";
                }),
            });
        }

        if (hw.Gpu.DriverDate is DateTime dd && !hw.Gpu.IsIntegrated)
        {
            int months = (int)((DateTime.Now - dd).TotalDays / 30.4);
            var url = hw.Gpu.Vendor switch
            {
                GpuVendor.Nvidia => "https://www.nvidia.com/en-us/software/nvidia-app/",
                GpuVendor.Amd => "https://www.amd.com/en/support/download/drivers.html",
                GpuVendor.Intel => "https://www.intel.com/content/www/us/en/download/785597/intel-arc-iris-xe-graphics-windows.html",
                _ => null
            };
            list.Add(new Tweak
            {
                Id = "gpu-driver",
                Category = "Graphics",
                Title = months >= 6 ? "Update your graphics driver" : "Graphics driver is recent",
                Impact = months >= 6 ? "High" : "Low",
                Description = $"Driver {hw.Gpu.DriverVersion} from {dd:MMM yyyy} ({months} month(s) old). New drivers bring game-specific fixes, DLSS/FSR updates and shader-cache improvements.",
                Status = months >= 6 ? TweakStatus.Recommended : TweakStatus.Optimal,
                ActionLabel = "Get driver",
                ApplyAction = url is null || months < 6 ? null : () => Task.Run(() => { OpenUrl(url); return "Opened the driver download page."; }),
            });
        }

        if (hw.Gpu.Vendor == GpuVendor.Nvidia)
            list.Add(Info("nv-cp", "Graphics", "NVIDIA Control Panel essentials",
                "Shader Cache Size: 10 GB or Unlimited · Low Latency Mode: On (Ultra for competitive) · Power management: Prefer maximum performance for games · use the NVIDIA app's DLSS Override → Latest model.", "Medium"));
        else if (hw.Gpu.Vendor == GpuVendor.Amd)
            list.Add(Info("amd-cp", "Graphics", "AMD Adrenalin essentials",
                "Keep HYPR-RX/Radeon Boost off unless you want dynamic resolution · Anti-Lag on · Enhanced Sync off when using FreeSync + an fps cap · RDNA 4: enable FSR 4 upgrade per game.", "Medium"));

        if (!hw.Gpu.IsIntegrated && hw.Gpu.Score >= 40)
            list.Add(Info("rebar", "Graphics", "Check Resizable BAR / Smart Access Memory",
                "Enable 'Above 4G Decoding' and 'Re-Size BAR' in BIOS. Worth up to 10% in some titles, essential for Intel Arc.", hw.Gpu.Vendor == GpuVendor.Intel ? "High" : "Low"));

        // ------------------------------------------------------------ Memory
        var mem = hw.Memory;
        bool jedec = mem.Type.Contains("DDR4") && mem.ConfiguredSpeedMhz is > 0 and <= 2666 ||
                     mem.Type.Contains("DDR5") && mem.ConfiguredSpeedMhz is > 0 and <= 4800;
        if (jedec && !hw.System.HasBattery)
            list.Add(Info("xmp", "Memory", "Enable XMP / EXPO in BIOS",
                $"RAM is running at {mem.ConfiguredSpeedMhz} MT/s, a JEDEC default. If your kit is rated faster (check the sticker), enabling XMP/EXPO typically adds 5-15% fps in CPU-bound games.", "High", TweakStatus.Warning));
        if (mem.ModuleCount == 1 && !hw.System.HasBattery)
            list.Add(Info("single-channel", "Memory", "Single-channel memory", "Only one memory stick is installed. A matched pair (dual channel) can raise CPU-bound fps by 10-30%.", "High", TweakStatus.Warning));
        if (mem.TotalGb < 15)
            list.Add(Info("ram-size", "Memory", "16 GB+ RAM recommended", $"You have {mem.TotalGb:0} GB. Modern games commonly use 10-14 GB; with less, Windows pages to disk and you'll see stutter.", "High", TweakStatus.Warning));
        else if (mem.TotalGb < 30)
            list.Add(Info("ram-size", "Memory", $"{mem.TotalGb:0} GB RAM", "Enough for most games. 32 GB helps in Tarkov, Star Citizen, MSFS and modded titles.", "Low", TweakStatus.Optimal));

        // ------------------------------------------------------------ Storage
        var onHdd = games.Where(g => HardwareDetector.DiskFor(hw, g.InstallPath) is { MediaType: "HDD" }).ToList();
        if (onHdd.Count > 0)
            list.Add(Info("hdd-games", "Storage", $"{onHdd.Count} game(s) on a hard drive",
                $"Move these to an SSD for faster loads and less texture pop-in/stutter: {string.Join(", ", onHdd.Take(6).Select(g => g.DisplayName))}{(onHdd.Count > 6 ? "…" : "")}.", "High", TweakStatus.Warning));
        foreach (var disk in hw.Disks.Where(x => x.TotalBytes > 0 && x.FreePercent < 10))
            list.Add(Info($"disk-{disk.Root}", "Storage", $"Drive {disk.Root} is almost full",
                $"{disk.FreeBytes / 1_073_741_824d:0.#} GB free. Keep 10-15% free so shader caches and game updates don't fail.", "Medium", TweakStatus.Warning));

        // ------------------------------------------------------------ Security trade-offs (information only)
        if (s.HvciRunning == true)
            list.Add(new Tweak
            {
                Id = "hvci",
                Category = "Security",
                Title = "Memory Integrity (Core isolation) is on",
                Impact = "Medium",
                Description = "Memory Integrity protects against kernel attacks and can cost ~5% in some games on older CPUs. Sentinel won't change security settings — review it yourself in Windows Security if you want to.",
                Status = TweakStatus.Info,
                ActionLabel = "Open",
                ApplyAction = () => Task.Run(() => { OpenUrl("windowsdefender://coreisolation"); return "Opened Windows Security."; }),
            });

        // ------------------------------------------------------------ Background load
        if (s.RunningOverlays.Count > 0)
            list.Add(Info("overlays", "Background", "Overlays & monitoring apps running",
                $"{string.Join(", ", s.RunningOverlays)}. Each hooks into games; disable the ones you don't use to avoid stutter and conflicts.", "Low"));
        if (s.StartupAppCount > 10)
            list.Add(new Tweak
            {
                Id = "startup",
                Category = "Background",
                Title = $"{s.StartupAppCount} apps start with Windows",
                Impact = "Low",
                Description = "Trim startup apps to free RAM and CPU time for games.",
                Status = TweakStatus.Info,
                ActionLabel = "Open",
                ApplyAction = () => Task.Run(() => { OpenUrl("ms-settings:startupapps"); return "Opened Startup apps settings."; }),
            });
        if (mem.LoadPercent > 70)
            list.Add(Info("ram-load", "Background", $"Memory is {mem.LoadPercent}% used right now", "Close browsers and launchers before playing demanding games.", "Medium", TweakStatus.Warning));

        return list.OrderBy(t => t.Status switch { TweakStatus.Recommended => 0, TweakStatus.Warning => 1, TweakStatus.Info => 2, _ => 3 })
                   .ThenBy(t => t.Impact switch { "High" => 0, "Medium" => 1, _ => 2 })
                   .ToList();
    }

    // ---------------------------------------------------------------- helpers

    private static Tweak Info(string id, string category, string title, string description, string impact, TweakStatus status = TweakStatus.Info) =>
        new() { Id = id, Category = category, Title = title, Description = description, Impact = impact, Status = status };

    private static Tweak RegistryToggle(TweakState state, string id, string category, string title, string description,
                                        RegistryKey hive, string path, string name, int desired, bool alreadyOk, string impact) => new()
    {
        Id = id,
        Category = category,
        Title = title,
        Description = description,
        Impact = impact,
        Status = alreadyOk ? TweakStatus.Optimal : TweakStatus.Recommended,
        CanRevert = state.Has(id),
        ApplyAction = () => Task.Run(() =>
        {
            using var k = hive.CreateSubKey(path);
            state.Remember(id, k.GetValue(name) is int prev ? prev.ToString() : "");
            k.SetValue(name, desired, RegistryValueKind.DWord);
            return "Done.";
        }),
        RevertAction = () => Task.Run(() =>
        {
            using var k = hive.CreateSubKey(path);
            var prev = state.Get(id);
            if (string.IsNullOrEmpty(prev)) k.DeleteValue(name, false);
            else k.SetValue(name, int.Parse(prev), RegistryValueKind.DWord);
            state.Forget(id);
            return "Reverted.";
        }),
    };

    private static bool HagsCapable(GpuInfo gpu)
    {
        if (gpu.IsIntegrated) return false;
        var n = gpu.Name.ToUpperInvariant();
        return gpu.Vendor switch
        {
            GpuVendor.Nvidia => Regex.IsMatch(n, @"RTX|GTX\s*1[0-6]\d\d"),
            GpuVendor.Amd => Regex.IsMatch(n, @"RX\s*[5-9]\d{3}"),
            GpuVendor.Intel => n.Contains("ARC"),
            _ => false
        };
    }

    private static bool ActivePlanIs(Guid g) =>
        HardwareDetector.RunCapture("powercfg", "/getactivescheme").Contains(g.ToString(), StringComparison.OrdinalIgnoreCase);

    private static string SetFlag(string settings, string key, string value)
    {
        var parts = settings.Split(';', StringSplitOptions.RemoveEmptyEntries).Where(p => !p.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)).ToList();
        parts.Add($"{key}={value}");
        return string.Join(";", parts) + ";";
    }

    private static void SetMouse(RegistryKey k, string speed, string t1, string t2)
    {
        k.SetValue("MouseSpeed", speed);
        k.SetValue("MouseThreshold1", t1);
        k.SetValue("MouseThreshold2", t2);
        int[] p = [int.Parse(t1), int.Parse(t2), int.Parse(speed)];
        NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETMOUSE, 0, p, NativeMethods.SPIF_UPDATEINIFILE | NativeMethods.SPIF_SENDCHANGE);
    }

    private static bool SetRefreshRate(string device, int hz)
    {
        var mode = NativeMethods.NewDevMode();
        if (!NativeMethods.EnumDisplaySettings(device, NativeMethods.ENUM_CURRENT_SETTINGS, ref mode)) return false;
        mode.dmDisplayFrequency = hz;
        mode.dmFields = NativeMethods.DM_DISPLAYFREQUENCY;
        return NativeMethods.ChangeDisplaySettingsEx(device, ref mode, IntPtr.Zero, NativeMethods.CDS_UPDATEREGISTRY, IntPtr.Zero) == NativeMethods.DISP_CHANGE_SUCCESSFUL;
    }

    /// <summary>Sets GpuPreference=2 for each exe; returns the exes that had no preference before (so revert only removes ours).</summary>
    public static List<string> SetHighPerformanceGpu(IEnumerable<string> exes)
    {
        using var k = Registry.CurrentUser.CreateSubKey(GpuPrefsKey);
        var added = new List<string>();
        foreach (var exe in exes)
        {
            if (k.GetValue(exe) is null) added.Add(exe);
            k.SetValue(exe, "GpuPreference=2;");
        }
        return added;
    }

    public static bool HasHighPerformanceGpu(string exe)
    {
        using var k = Registry.CurrentUser.OpenSubKey(GpuPrefsKey);
        return (k?.GetValue(exe) as string)?.Contains("GpuPreference=2") == true;
    }

    public static async Task<bool> RunElevated(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            if (p is null) return false;
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch (Win32Exception) { return false; } // UAC prompt declined
    }

    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}

/// <summary>Remembers pre-tweak values so every change can be reverted, even after restarting Sentinel.</summary>
public sealed class TweakState
{
    private readonly Dictionary<string, string> _values;
    private TweakState(Dictionary<string, string> v) => _values = v;

    public static TweakState Load()
    {
        if (AppMode.Demo) return new TweakState([]); // the demo PC has nothing of this PC's to revert
        try
        {
            if (File.Exists(AppPaths.TweakState))
                return new TweakState(JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(AppPaths.TweakState)) ?? []);
        }
        catch (Exception ex) { Log.Error("tweak-state", ex); }
        return new TweakState([]);
    }

    public bool Has(string id) => _values.ContainsKey(id);
    public string? Get(string id) => _values.TryGetValue(id, out var v) ? v : null;

    public void Remember(string id, string previous)
    {
        // Keep the original value if a tweak is applied twice.
        _values.TryAdd(id, previous);
        Save();
    }

    public void Forget(string id)
    {
        _values.Remove(id);
        Save();
    }

    private void Save() => File.WriteAllText(AppPaths.TweakState, JsonSerializer.Serialize(_values));
}
