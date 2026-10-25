using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Sentinel.Core;
using Sentinel.Models;

namespace Sentinel.Services.Hardware;

/// <summary>Reads CPU, GPU, RAM, storage, displays and Windows gaming state. Every probe is best-effort.</summary>
public static class HardwareDetector
{
    public static Task<HardwareProfile> DetectAsync() => Task.Run(Detect);

    public static HardwareProfile Detect()
    {
        var p = new HardwareProfile();
        Try("cpu", () => p.Cpu = DetectCpu());
        Try("gpu", () => p.Gpus = DetectGpus());
        p.Gpu = p.Gpus.OrderByDescending(g => g.Score).FirstOrDefault() ?? new GpuInfo();
        Try("memory", () => p.Memory = DetectMemory());
        Try("disks", () => p.Disks = DetectDisks());
        Try("displays", () => p.Displays = DetectDisplays());
        p.Display = p.Displays.FirstOrDefault(d => d.IsPrimary) ?? p.Displays.FirstOrDefault() ?? p.Display;
        Try("system", () => p.System = DetectSystem());
        return p;
    }

    private static void Try(string what, Action action)
    {
        try { action(); }
        catch (Exception ex) { Log.Error($"hardware:{what}", ex); }
    }

    private static IEnumerable<ManagementBaseObject> Wmi(string query, string scope = @"root\cimv2")
    {
        using var searcher = new ManagementObjectSearcher(scope, query);
        foreach (var o in searcher.Get()) yield return o;
    }

    private static T? Prop<T>(ManagementBaseObject o, string name)
    {
        try
        {
            var v = o[name];
            if (v is null) return default;
            if (v is T t) return t;
            return (T)Convert.ChangeType(v, typeof(T));
        }
        catch { return default; }
    }

    // ---------------------------------------------------------------- CPU

    private static CpuInfo DetectCpu()
    {
        var cpu = new CpuInfo();
        foreach (var o in Wmi("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor"))
        {
            cpu.Name = (Prop<string>(o, "Name") ?? cpu.Name).Trim();
            cpu.Cores += Prop<int>(o, "NumberOfCores");
            cpu.Threads += Prop<int>(o, "NumberOfLogicalProcessors");
            cpu.MaxClockMhz = Math.Max(cpu.MaxClockMhz, Prop<int>(o, "MaxClockSpeed"));
        }
        if (cpu.Threads == 0) cpu.Threads = Environment.ProcessorCount;
        var (score, match, mobile) = HardwareDatabase.MatchCpu(cpu.Name, cpu.Cores, cpu.Threads, cpu.MaxClockMhz);
        cpu.Score = score;
        cpu.MatchedModel = match;
        cpu.IsLaptopChip = mobile;
        return cpu;
    }

    // ---------------------------------------------------------------- GPU

    private static readonly Regex VirtualAdapter = new(
        @"Basic (Display|Render)|Remote|Virtual|Parsec|IddCx|Indirect|Citrix|Hyper-V|Spacedesk|DisplayLink|Mirage",
        RegexOptions.IgnoreCase);

    private static List<GpuInfo> DetectGpus()
    {
        var vram = ReadRegistryVram();
        var list = new List<GpuInfo>();

        foreach (var o in Wmi("SELECT Name, PNPDeviceID, DriverVersion, DriverDate, AdapterRAM FROM Win32_VideoController"))
        {
            var name = (Prop<string>(o, "Name") ?? "").Trim();
            if (name.Length == 0 || VirtualAdapter.IsMatch(name)) continue;

            var pnp = Prop<string>(o, "PNPDeviceID") ?? "";
            var g = new GpuInfo
            {
                Name = name,
                DriverVersion = Prop<string>(o, "DriverVersion"),
                Vendor = pnp.Contains("VEN_10DE") || name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Nvidia
                       : pnp.Contains("VEN_1002") || name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Amd
                       : pnp.Contains("VEN_8086") || name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Intel
                       : GpuVendor.Unknown,
            };

            var rawDate = Prop<string>(o, "DriverDate");
            if (rawDate is { Length: >= 8 } &&
                DateTime.TryParseExact(rawDate[..8], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d))
                g.DriverDate = d;

            // AdapterRAM is a uint32 and caps at 4 GB, so prefer the driver's 64-bit value from the registry.
            long wmiRam = Convert.ToInt64(o["AdapterRAM"] ?? 0L);
            g.VramBytes = vram.TryGetValue(name, out var reg) && reg > 0 ? reg : wmiRam;

            var (score, match, integrated) = HardwareDatabase.MatchGpu(name, g.VramBytes);
            g.Score = score;
            g.MatchedModel = match;
            g.IsIntegrated = integrated || (g.Vendor == GpuVendor.Intel && !Regex.IsMatch(name, @"Arc\(?T?M?\)?\s*[AB]\d{3}", RegexOptions.IgnoreCase));
            list.Add(g);
        }
        return list;
    }

    /// <summary>DriverDesc -> dedicated VRAM bytes, from the display adapter class key.</summary>
    private static Dictionary<string, long> ReadRegistryVram()
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
        if (cls is null) return result;

        foreach (var sub in cls.GetSubKeyNames().Where(n => Regex.IsMatch(n, @"^\d{4}$")))
        {
            try
            {
                using var k = cls.OpenSubKey(sub);
                if (k?.GetValue("DriverDesc") is not string desc) continue;
                long bytes = k.GetValue("HardwareInformation.qwMemorySize") switch
                {
                    long l => l,
                    byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
                    _ => k.GetValue("HardwareInformation.MemorySize") switch
                    {
                        int i => (uint)i,
                        long l => l,
                        byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0),
                        _ => 0
                    }
                };
                if (bytes > 0 && (!result.TryGetValue(desc, out var prev) || bytes > prev)) result[desc] = bytes;
            }
            catch (System.Security.SecurityException) { /* some subkeys are locked down */ }
        }
        return result;
    }

    // ---------------------------------------------------------------- Memory

    private static MemoryInfo DetectMemory()
    {
        var m = new MemoryInfo();
        var status = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        if (NativeMethods.GlobalMemoryStatusEx(ref status))
        {
            m.TotalBytes = (long)status.ullTotalPhys;
            m.LoadPercent = (int)status.dwMemoryLoad;
        }

        foreach (var o in Wmi("SELECT Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType FROM Win32_PhysicalMemory"))
        {
            m.ModuleCount++;
            m.InstalledBytes += Prop<long>(o, "Capacity");
            m.RatedSpeedMhz = Math.Max(m.RatedSpeedMhz, Prop<int>(o, "Speed"));
            m.ConfiguredSpeedMhz = Math.Max(m.ConfiguredSpeedMhz, Prop<int>(o, "ConfiguredClockSpeed"));
            m.Type = Prop<int>(o, "SMBIOSMemoryType") switch
            {
                34 => "DDR5", 35 => "LPDDR5", 26 => "DDR4", 30 => "LPDDR4", 24 => "DDR3", 29 => "LPDDR3", _ => m.Type
            };
        }
        if (m.ConfiguredSpeedMhz == 0) m.ConfiguredSpeedMhz = m.RatedSpeedMhz;
        return m;
    }

    // ---------------------------------------------------------------- Storage

    private static List<DiskInfo> DetectDisks()
    {
        const string storage = @"root\Microsoft\Windows\Storage";
        var physical = new Dictionary<string, (string Model, string Media, string Bus)>();
        try
        {
            foreach (var o in Wmi("SELECT DeviceId, FriendlyName, MediaType, BusType FROM MSFT_PhysicalDisk", storage))
            {
                var media = Prop<int>(o, "MediaType") switch { 3 => "HDD", 4 => "SSD", 5 => "SSD", _ => "Unknown" };
                var bus = Prop<int>(o, "BusType") switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", 8 => "RAID", 10 => "SAS", 3 => "ATA", _ => "" };
                if (bus == "NVMe" && media == "Unknown") media = "SSD";
                physical[Prop<string>(o, "DeviceId") ?? ""] = (Prop<string>(o, "FriendlyName") ?? "", media, bus);
            }
        }
        catch (ManagementException ex) { Log.Error("disks:physical", ex); }

        var letterToDisk = new Dictionary<char, string>();
        try
        {
            foreach (var o in Wmi("SELECT DiskNumber, DriveLetter FROM MSFT_Partition", storage))
            {
                var raw = o["DriveLetter"];
                if (raw is null) continue;
                char letter = raw is char c ? c : (char)Convert.ToInt32(raw);
                if (char.IsLetter(letter)) letterToDisk[char.ToUpperInvariant(letter)] = Prop<uint>(o, "DiskNumber").ToString();
            }
        }
        catch (ManagementException ex) { Log.Error("disks:partitions", ex); }

        var disks = new List<DiskInfo>();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            var info = new DiskInfo
            {
                Root = drive.RootDirectory.FullName,
                Label = drive.VolumeLabel,
                TotalBytes = drive.TotalSize,
                FreeBytes = drive.AvailableFreeSpace,
            };
            if (letterToDisk.TryGetValue(char.ToUpperInvariant(drive.Name[0]), out var num) && physical.TryGetValue(num, out var ph))
            {
                info.Model = ph.Model;
                info.MediaType = ph.Media;
                info.Bus = ph.Bus;
            }
            disks.Add(info);
        }
        return disks;
    }

    public static DiskInfo? DiskFor(HardwareProfile hw, string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length < 2) return null;
        return hw.Disks.FirstOrDefault(d => path.StartsWith(d.Root, StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------- Displays

    private static List<DisplayInfo> DetectDisplays()
    {
        var list = new List<DisplayInfo>();
        var adapter = new NativeMethods.DISPLAY_DEVICE { cb = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>() };

        for (uint i = 0; NativeMethods.EnumDisplayDevices(null, i, ref adapter, 0); i++)
        {
            if ((adapter.StateFlags & NativeMethods.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) goto next;

            var current = NativeMethods.NewDevMode();
            if (!NativeMethods.EnumDisplaySettings(adapter.DeviceName, NativeMethods.ENUM_CURRENT_SETTINGS, ref current)) goto next;

            int maxHz = current.dmDisplayFrequency;
            var mode = NativeMethods.NewDevMode();
            for (int m = 0; NativeMethods.EnumDisplaySettings(adapter.DeviceName, m, ref mode); m++)
            {
                if (mode.dmPelsWidth == current.dmPelsWidth && mode.dmPelsHeight == current.dmPelsHeight)
                    maxHz = Math.Max(maxHz, mode.dmDisplayFrequency);
            }

            var monitor = new NativeMethods.DISPLAY_DEVICE { cb = adapter.cb };
            string friendly = NativeMethods.EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, 0) ? monitor.DeviceString : adapter.DeviceString;

            list.Add(new DisplayInfo
            {
                DeviceName = adapter.DeviceName,
                FriendlyName = friendly,
                Width = current.dmPelsWidth,
                Height = current.dmPelsHeight,
                RefreshHz = current.dmDisplayFrequency,
                MaxRefreshHz = maxHz,
                IsPrimary = (adapter.StateFlags & NativeMethods.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0,
            });

        next:
            adapter.cb = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>();
        }
        return list;
    }

    // ---------------------------------------------------------------- Windows state

    private static SystemState DetectSystem()
    {
        var s = new SystemState();

        using (var nt = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
        {
            var product = nt?.GetValue("ProductName") as string ?? "Windows";
            var build = nt?.GetValue("CurrentBuild") as string ?? "";
            if (int.TryParse(build, out var b) && b >= 22000) product = product.Replace("Windows 10", "Windows 11");
            s.OsName = $"{product} {nt?.GetValue("DisplayVersion")}".Trim();
            s.OsBuild = $"{build}.{nt?.GetValue("UBR")}";
        }

        if (NativeMethods.GetSystemPowerStatus(out var power))
        {
            s.HasBattery = power.BatteryFlag != 128 && power.BatteryFlag != 255;
            s.OnBattery = s.HasBattery && power.ACLineStatus == 0;
        }

        try
        {
            var output = RunCapture("powercfg", "/getactivescheme");
            var m = Regex.Match(output, @"([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\s*\((.+)\)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                s.PowerPlanGuid = Guid.Parse(m.Groups[1].Value);
                s.PowerPlanName = m.Groups[2].Value.Trim();
            }
        }
        catch (Exception ex) { Log.Error("powercfg", ex); }

        s.GameModeEnabled = RegDword(Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled") is int gm ? gm != 0 : true;
        s.HagsEnabled = RegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode") switch { 2 => true, 1 => false, _ => null };
        s.GameDvrEnabled = RegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled") is int dvr ? dvr != 0 : false;

        using (var mouse = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse"))
            s.MouseAccelerationOn = mouse?.GetValue("MouseSpeed") is string ms ? ms != "0" : null;

        using (var dx = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences"))
            s.WindowedOptimizations = (dx?.GetValue("DirectXUserGlobalSettings") as string)?.Contains("SwapEffectUpgradeEnable=1") ?? false;

        try
        {
            foreach (var o in Wmi("SELECT VirtualizationBasedSecurityStatus, SecurityServicesRunning FROM Win32_DeviceGuard", @"root\Microsoft\Windows\DeviceGuard"))
            {
                s.VbsRunning = Prop<uint>(o, "VirtualizationBasedSecurityStatus") == 2;
                s.HvciRunning = o["SecurityServicesRunning"] is uint[] running && running.Contains(2u);
            }
        }
        catch (Exception ex) { Log.Error("deviceguard", ex); }

        s.StartupAppCount = CountValues(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run")
                          + CountValues(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run")
                          + CountStartupFolder();

        var overlays = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["NVIDIA Overlay"] = "NVIDIA overlay", ["NVIDIA Share"] = "NVIDIA overlay", ["RTSS"] = "RivaTuner Statistics Server",
            ["MSIAfterburner"] = "MSI Afterburner", ["obs64"] = "OBS Studio", ["Overwolf"] = "Overwolf", ["Medal"] = "Medal.tv",
            ["XboxPcAppFT"] = "Xbox app", ["GameBar"] = "Xbox Game Bar", ["iCUE"] = "Corsair iCUE", ["LGHUB"] = "Logitech G HUB",
            ["ArmouryCrate"] = "Armoury Crate", ["RadeonSoftware"] = "AMD Adrenalin", ["Wallpaper64"] = "Wallpaper Engine",
            ["lively"] = "Lively Wallpaper", ["SignalRgb"] = "SignalRGB",
        };
        foreach (var proc in Process.GetProcesses())
        {
            using (proc)
                if (overlays.TryGetValue(proc.ProcessName, out var label) && !s.RunningOverlays.Contains(label))
                    s.RunningOverlays.Add(label);
        }
        return s;
    }

    private static int? RegDword(RegistryKey root, string path, string name)
    {
        using var k = root.OpenSubKey(path);
        return k?.GetValue(name) is int v ? v : null;
    }

    private static int CountValues(RegistryKey root, string path)
    {
        using var k = root.OpenSubKey(path);
        return k?.ValueCount ?? 0;
    }

    private static int CountStartupFolder()
    {
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        return Directory.Exists(dir) ? Directory.EnumerateFiles(dir).Count(f => !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)) : 0;
    }

    public static string RunCapture(string file, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(10_000);
        return output;
    }
}
