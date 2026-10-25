using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Sentinel.Core;

namespace Sentinel.Services.Hardware;

/// <summary>
/// Relative performance tables for GPUs (RTX 3070 = 100) and CPUs (Ryzen 5 5600X = 100).
/// Matching picks the longest key found in the device name, so "RTX 4070 TI SUPER" wins over "RTX 4070".
/// </summary>
public static class HardwareDatabase
{
    private sealed class Entry
    {
        [JsonPropertyName("k")] public string Key { get; set; } = "";
        [JsonPropertyName("s")] public double Score { get; set; }
        [JsonPropertyName("l")] public bool Laptop { get; set; }
        [JsonPropertyName("i")] public bool Integrated { get; set; }
        [JsonPropertyName("m")] public bool Mobile { get; set; }
        [JsonIgnore] public Regex? Pattern { get; set; }
    }

    private static readonly Lazy<List<Entry>> Gpus = new(() => Prepare(EmbeddedData.Load<List<Entry>>("Sentinel.Data.gpus.json")));
    private static readonly Lazy<List<Entry>> Cpus = new(() => Prepare(EmbeddedData.Load<List<Entry>>("Sentinel.Data.cpus.json")));

    private static List<Entry> Prepare(List<Entry> list)
    {
        foreach (var e in list)
        {
            var body = string.Join(@"[\s-]*", e.Key.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape));
            e.Pattern = new Regex($@"(?<![A-Z0-9]){body}(?![A-Z0-9])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        }
        return list.OrderByDescending(e => e.Key.Length).ToList();
    }

    public static string NormalizeGpu(string name)
    {
        var n = name.ToUpperInvariant();
        n = Regex.Replace(n, @"\((TM|R)\)|™|®", " ");
        n = n.Replace("NVIDIA", " ").Replace("GEFORCE", " ").Replace("AMD", " ").Replace("INTEL", " ");
        n = Regex.Replace(n, @"\bRADEON\s+(?=RX|R9|VII|PRO)", " ");
        // Intel names put the number last: "UHD Graphics 770" -> "UHD 770".
        n = Regex.Replace(n, @"\b(UHD|HD)\s+GRAPHICS\s+(\d+)", "$1 $2");
        n = Regex.Replace(n, @"\s+", " ").Trim();
        return n;
    }

    public static string NormalizeCpu(string name)
    {
        var n = name.ToUpperInvariant();
        n = Regex.Replace(n, @"\((TM|R)\)|™|®", " ");
        // Intel "F" (no iGPU) parts game like their full siblings: 13700KF -> 13700K, 12400F -> 12400.
        // AMD keeps its F suffix (7500F is its own model).
        if (n.Contains("INTEL"))
            n = Regex.Replace(n, @"\b(\d{4,5}[A-Z]{0,2}?)F\b", "$1");
        n = Regex.Replace(n, @"\b(I[3579])-", "$1 ");
        n = Regex.Replace(n, @"\s+", " ").Trim();
        return n;
    }

    public static (double Score, string? Match, bool Integrated) MatchGpu(string name, long vramBytes)
    {
        var n = NormalizeGpu(name);
        bool laptop = Regex.IsMatch(n, @"LAPTOP|MOBILE|MAX-Q|\bMQ\b");

        if (laptop)
        {
            var hit = Gpus.Value.FirstOrDefault(e => e.Laptop && e.Pattern!.IsMatch(n));
            if (hit is not null) return (hit.Score, hit.Key + " Laptop", false);
        }

        var desk = Gpus.Value.FirstOrDefault(e => !e.Laptop && e.Pattern!.IsMatch(n));
        if (desk is not null)
            return (laptop ? desk.Score * 0.78 : desk.Score, desk.Key, desk.Integrated);

        // Unknown card: rough guess from VRAM so we still produce sane numbers.
        double gb = vramBytes / 1024d / 1024 / 1024;
        double guess = gb switch { >= 16 => 120, >= 12 => 95, >= 8 => 70, >= 6 => 45, >= 4 => 28, >= 2 => 14, _ => 6 };
        return (guess, null, gb < 1);
    }

    public static (double Score, string? Match, bool Mobile) MatchCpu(string name, int cores, int threads, int maxMhz)
    {
        var n = NormalizeCpu(name);
        var hit = Cpus.Value.FirstOrDefault(e => e.Pattern!.IsMatch(n));
        if (hit is not null) return (hit.Score, hit.Key, hit.Mobile);

        // Heuristic fallback: clock speed dominates gaming performance, thread count matters up to ~12.
        bool mobile = Regex.IsMatch(n, @"\d{4,5}(U|H|HS|HX|HK|P|G\d)\b");
        double ghz = maxMhz > 0 ? maxMhz / 1000d : 3.0;
        double score = 30 + Math.Min(threads, 16) * 2.5 + (ghz - 3.0) * 18;
        if (n.Contains("X3D")) score += 20;
        if (mobile) score *= 0.85;
        return (Math.Clamp(score, 20, 130), null, mobile);
    }
}
