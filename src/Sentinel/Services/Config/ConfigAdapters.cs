using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;
using Sentinel.Core;
using Sentinel.Models;

namespace Sentinel.Services.Config;

/// <summary>Writes an optimisation plan into a game's own config files.</summary>
public interface IConfigAdapter
{
    string Name { get; }
    string Description { get; }
    bool Experimental { get; }
    /// <summary>Preset-only games expose one overall quality slider.</summary>
    bool PresetOnly { get; }
    IReadOnlySet<SettingCategory> Controls { get; }
    bool ControlsRenderScale { get; }
    bool ControlsFpsCap { get; }
    /// <summary>The config file (or registry key) that will be edited, or null if it doesn't exist yet.</summary>
    string? Target { get; }
    ApplyResult Apply(OptimizationPlan plan);
}

public static class ConfigResolver
{
    private static readonly Lazy<Dictionary<string, ConfigProfile>> Profiles = new(() =>
        EmbeddedData.Load<List<ConfigProfile>>("Sentinel.Data.profiles.json").ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase));

    public static IConfigAdapter? For(InstalledGame game)
    {
        var profile = game.Catalog?.Profile;
        if (profile is not null)
        {
            if (profile.StartsWith("ue:", StringComparison.OrdinalIgnoreCase))
                return new UnrealAdapter(game, profile[3..]);
            if (Profiles.Value.TryGetValue(profile, out var p))
                return new ProfileAdapter(game, p);
        }
        // UE3 titles use a different config layout, so only UE4/UE5 get the scalability adapter.
        bool modernUnreal = game.Engine.StartsWith("Unreal") && !game.Engine.StartsWith("Unreal 3");
        if (modernUnreal && (game.UnrealProject is not null || game.Catalog is not null))
            return new UnrealAdapter(game, game.UnrealProject);
        if (game.UnityInfo is not null)
            return new UnityAdapter(game);
        return null;
    }

    /// <summary>True when one of the game's processes is running (games overwrite configs on exit).</summary>
    public static bool IsRunning(InstalledGame game)
    {
        var names = (game.Catalog?.Exes ?? []).Select(Path.GetFileNameWithoutExtension)
            .Append(game.MainExe is null ? null : Path.GetFileNameWithoutExtension(game.MainExe))
            .Where(n => !string.IsNullOrEmpty(n) && !n!.Equals("javaw", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var n in names)
        {
            var procs = Process.GetProcessesByName(n);
            bool running = procs.Length > 0;
            foreach (var p in procs) p.Dispose();
            if (running) return true;
        }
        return false;
    }

    internal static void WriteRespectingReadOnly(string path, Action write)
    {
        var attrs = File.GetAttributes(path);
        bool ro = attrs.HasFlag(FileAttributes.ReadOnly);
        if (ro) File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
        try { write(); }
        finally { if (ro) File.SetAttributes(path, attrs); }
    }
}

// ==================================================================== data-driven profiles

public sealed class KeyMap
{
    [JsonPropertyName("k")] public string Key { get; set; } = "";
    [JsonPropertyName("v")] public string[] Values { get; set; } = [];
}

public sealed class KeyValue
{
    [JsonPropertyName("k")] public string Key { get; set; } = "";
    [JsonPropertyName("v")] public string Value { get; set; } = "";
}

public sealed class FpsKey
{
    [JsonPropertyName("k")] public string Key { get; set; } = "";
    [JsonPropertyName("max")] public int Max { get; set; } = 1000;
}

public sealed class ConfigProfile
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string[] Files { get; set; } = [];
    public string? Note { get; set; }
    public bool Preset { get; set; }
    public Dictionary<string, List<KeyMap>> Map { get; set; } = [];
    public List<KeyMap> Overall { get; set; } = [];
    public KeyValue? Vsync { get; set; }
    public FpsKey? Fps { get; set; }
}

/// <summary>Applies profiles.json definitions: kv (Valve style), colon (key:value), ini, xmlattr, xmlnamed.</summary>
public sealed class ProfileAdapter(InstalledGame game, ConfigProfile profile) : IConfigAdapter
{
    public string Name => profile.Type switch
    {
        "kv" => "Video config (key/value)", "colon" => "options.txt", "ini" => "INI config", "xmlnamed" => "Settings XML", _ => "Settings XML"
    };
    public string Description => profile.Note ?? "Edits the game's settings file.";
    public bool Experimental => false;
    public bool PresetOnly => profile.Preset;
    public IReadOnlySet<SettingCategory> Controls { get; } =
        profile.Map.Keys.Select(k => Enum.TryParse<SettingCategory>(k, out var c) ? c : (SettingCategory?)null).OfType<SettingCategory>().ToHashSet();
    public bool ControlsRenderScale => false;
    public bool ControlsFpsCap => profile.Fps is not null;

    public string? Target => profile.Files.SelectMany(f => PathTokens.Expand(f, game)).Where(File.Exists)
        .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();

    private Dictionary<string, string> Desired(OptimizationPlan plan)
    {
        var desired = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (catName, maps) in profile.Map)
        {
            if (!Enum.TryParse<SettingCategory>(catName, out var cat) || !plan.Levels.TryGetValue(cat, out var level)) continue;
            foreach (var m in maps) desired[m.Key] = m.Values[Math.Clamp(level, 0, m.Values.Length - 1)];
        }
        if (profile.Overall.Count > 0)
        {
            int overall = (int)Math.Round(plan.Levels.Values.Average());
            foreach (var m in profile.Overall) desired[m.Key] = m.Values[Math.Clamp(overall, 0, m.Values.Length - 1)];
        }
        if (profile.Vsync is not null) desired[profile.Vsync.Key] = profile.Vsync.Value;
        if (profile.Fps is not null) desired[profile.Fps.Key] = Math.Min(plan.FpsCap, profile.Fps.Max).ToString(CultureInfo.InvariantCulture);
        return desired;
    }

    public ApplyResult Apply(OptimizationPlan plan)
    {
        var file = Target;
        if (file is null)
            return new ApplyResult(false, "Settings file not found. Launch the game once (and change any graphics setting) so it creates its config, then try again.");

        var desired = Desired(plan);
        BackupService.Backup(game.BackupId, [file]);
        int changed = 0;
        ConfigResolver.WriteRespectingReadOnly(file, () => changed = profile.Type switch
        {
            "kv" => ApplyKeyValue(file, desired),
            "colon" => ApplyColon(file, desired),
            "ini" => ApplyIni(file, desired),
            "xmlattr" => ApplyXml(file, desired, named: false),
            "xmlnamed" => ApplyXml(file, desired, named: true),
            _ => throw new NotSupportedException(profile.Type)
        });

        return changed > 0
            ? new ApplyResult(true, $"Updated {changed} setting(s) in {Path.GetFileName(file)}. A backup was saved.", changed, file)
            : new ApplyResult(false, $"{Path.GetFileName(file)} already matches, or none of its keys were recognised.", 0, file);
    }

    private static readonly Regex KvLine = new(@"^(?<pre>\s*""?)(?<key>[^""\s]+)(?<mid>""?\s+""?)(?<val>[^""]*)(?<post>""?.*)$");

    internal static int ApplyKeyValue(string file, Dictionary<string, string> desired) =>
        TextFiles.EditLines(file, line =>
        {
            var m = KvLine.Match(line);
            if (!m.Success || !desired.TryGetValue(m.Groups["key"].Value, out var v)) return null;
            return m.Groups["pre"].Value + m.Groups["key"].Value + m.Groups["mid"].Value + v + m.Groups["post"].Value;
        });

    internal static int ApplyColon(string file, Dictionary<string, string> desired) =>
        TextFiles.EditLines(file, line =>
        {
            int c = line.IndexOf(':');
            return c > 0 && desired.TryGetValue(line[..c], out var v) ? $"{line[..c]}:{v}" : null;
        });

    internal static int ApplyIni(string file, Dictionary<string, string> desired)
    {
        var ini = IniFile.Load(file);
        int changed = 0;
        foreach (var (path, v) in desired)
        {
            var split = path.Split('/', 2);
            if (split.Length == 2 && ini.Set(split[0], split[1], v, addIfMissing: false)) changed++;
        }
        if (changed > 0) ini.Save(file);
        return changed;
    }

    // <Key ... value="x" ...> — the attribute named exactly "value" on an element named Key.
    private static readonly Regex XmlValueAttr = new(@"<(?<tag>[\w.-]+)(?<pre>(?:\s+[^>]*?)?\svalue\s*=\s*)(?<q>[""'])(?<val>[^""']*)\k<q>", RegexOptions.Compiled);
    // <tag ... name="Key" ...>text</tag>
    private static readonly Regex XmlNamedText = new(@"(?<open><(?<tag>[\w.-]+)\s(?:[^>]*?\s)?name\s*=\s*(?<q>[""'])(?<name>[^""']*)\k<q>[^>/]*>)(?<val>[^<]*)(?<close></\k<tag>\s*>)", RegexOptions.Compiled);

    /// <summary>
    /// Edits values as text instead of re-serialising with XDocument, which would add an XML declaration and BOM
    /// and normalise whitespace/quotes. The result is re-parsed before writing so a bad edit can't corrupt the file.
    /// </summary>
    internal static int ApplyXml(string file, Dictionary<string, string> desired, bool named)
    {
        var (text, encoding) = TextFiles.Read(file);
        int changed = 0;
        string updated = named
            ? XmlNamedText.Replace(text, m =>
            {
                if (!desired.TryGetValue(m.Groups["name"].Value, out var v)) return m.Value;
                var escaped = System.Security.SecurityElement.Escape(v);
                if (m.Groups["val"].Value == escaped) return m.Value;
                changed++;
                return m.Groups["open"].Value + escaped + m.Groups["close"].Value;
            })
            : XmlValueAttr.Replace(text, m =>
            {
                if (!desired.TryGetValue(m.Groups["tag"].Value, out var v)) return m.Value;
                var escaped = System.Security.SecurityElement.Escape(v);
                if (m.Groups["val"].Value == escaped) return m.Value;
                changed++;
                var q = m.Groups["q"].Value;
                return $"<{m.Groups["tag"].Value}{m.Groups["pre"].Value}{q}{escaped}{q}";
            });

        if (changed == 0) return 0;
        XDocument.Parse(updated); // throws (leaving the file untouched) if the edit produced invalid XML
        TextFiles.Write(file, updated, encoding);
        return changed;
    }
}

// ==================================================================== Unreal Engine 4/5

/// <summary>Writes UE's standard scalability groups (sg.*) plus frame cap / V-Sync into GameUserSettings.ini.</summary>
public sealed class UnrealAdapter(InstalledGame game, string? projectOverride) : IConfigAdapter
{
    private static readonly string[] PlatformDirs = ["Windows", "WindowsNoEditor", "WindowsClient", "WinGDK", "WinGRTS"];

    private static readonly (SettingCategory Cat, string Key, bool Ue5Only)[] Groups =
    [
        (SettingCategory.ViewDistance, "sg.ViewDistanceQuality", false),
        (SettingCategory.AntiAliasing, "sg.AntiAliasingQuality", false),
        (SettingCategory.Shadows, "sg.ShadowQuality", false),
        (SettingCategory.Lighting, "sg.GlobalIlluminationQuality", true),
        (SettingCategory.Reflections, "sg.ReflectionQuality", true),
        (SettingCategory.PostProcessing, "sg.PostProcessQuality", false),
        (SettingCategory.Textures, "sg.TextureQuality", false),
        (SettingCategory.Effects, "sg.EffectsQuality", false),
        (SettingCategory.Foliage, "sg.FoliageQuality", false),
        (SettingCategory.Lighting, "sg.ShadingQuality", false),
    ];

    public string Name => "Unreal Engine scalability";
    public string Description => "Edits GameUserSettings.ini scalability groups, frame-rate limit and V-Sync. Works for most Unreal titles; a few override these with their own menu values.";
    public bool Experimental => false;
    public bool PresetOnly => false;
    public IReadOnlySet<SettingCategory> Controls { get; } = Groups.Select(g => g.Cat).ToHashSet();
    public bool ControlsRenderScale => true;
    public bool ControlsFpsCap => true;

    private IEnumerable<string> FolderNames()
    {
        var names = new List<string?> { projectOverride, game.UnrealProject, game.Name, game.Catalog?.Name };
        return names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public string? Target
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games");
            var roots = new List<string>();
            foreach (var n in FolderNames())
            {
                roots.Add(Path.Combine(local, n));
                roots.Add(Path.Combine(docs, n));
                if (game.InstallPath is not null) roots.Add(Path.Combine(game.InstallPath, n));
            }
            return roots
                .SelectMany(r => PlatformDirs.Select(p => Path.Combine(r, "Saved", "Config", p, "GameUserSettings.ini")))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
    }

    public ApplyResult Apply(OptimizationPlan plan)
    {
        var file = Target;
        if (file is null)
            return new ApplyResult(false, "GameUserSettings.ini not found. Launch the game once so Unreal creates it, then apply again.");

        BackupService.Backup(game.BackupId, [file]);
        var ini = IniFile.Load(file);
        bool ue5 = game.Engine.Contains('5') || ini.Get("ScalabilityGroups", "sg.GlobalIlluminationQuality") is not null;
        int changed = 0;

        foreach (var (cat, key, ue5Only) in Groups)
        {
            if (ue5Only && !ue5) continue;
            int level = Math.Clamp(plan.Levels.GetValueOrDefault(cat, QualityLevel.High), 0, 3);
            if (ini.Set("ScalabilityGroups", key, level.ToString(CultureInfo.InvariantCulture), addIfMissing: true)) changed++;
        }
        if (ini.Set("ScalabilityGroups", "sg.ResolutionQuality", plan.RenderScale.ToString(CultureInfo.InvariantCulture), addIfMissing: true)) changed++;

        // Frame cap and V-Sync live in the game's GameUserSettings section (its name varies per game).
        string section = ini.SectionContaining("FrameRateLimit") ?? ini.SectionContaining("bUseVSync") ?? "/Script/Engine.GameUserSettings";
        if (ini.Set(section, "FrameRateLimit", plan.FpsCap.ToString("0.000000", CultureInfo.InvariantCulture), addIfMissing: true)) changed++;
        if (ini.Set(section, "bUseVSync", "False", addIfMissing: true)) changed++;

        if (changed > 0) ConfigResolver.WriteRespectingReadOnly(file, () => ini.Save(file));
        return changed > 0
            ? new ApplyResult(true, $"Updated {changed} value(s) in {Path.GetFileName(file)}. A backup was saved.", changed, file)
            : new ApplyResult(true, "GameUserSettings.ini already matches the plan.", 0, file);
    }
}

// ==================================================================== Unity (registry PlayerPrefs)

/// <summary>Sets Unity's built-in quality level (UnityGraphicsQuality_h*). Many Unity games use custom menus, so this is experimental.</summary>
public sealed class UnityAdapter(InstalledGame game) : IConfigAdapter
{
    public string Name => "Unity quality level";
    public string Description => "Changes the built-in Unity quality preset in the registry. Games with custom settings menus may ignore it.";
    public bool Experimental => true;
    public bool PresetOnly => true;
    public IReadOnlySet<SettingCategory> Controls { get; } = Enum.GetValues<SettingCategory>().ToHashSet();
    public bool ControlsRenderScale => false;
    public bool ControlsFpsCap => false;

    private string KeyPath => $@"Software\{game.UnityInfo!.Value.Company}\{game.UnityInfo!.Value.Product}";

    private string? ValueName()
    {
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath);
        return k?.GetValueNames().FirstOrDefault(n => n.StartsWith("UnityGraphicsQuality_h", StringComparison.Ordinal));
    }

    public string? Target => ValueName() is { } v ? $@"HKCU\{KeyPath}\{v}" : null;

    public ApplyResult Apply(OptimizationPlan plan)
    {
        var name = ValueName();
        if (name is null) return new ApplyResult(false, "Unity quality value not found — change the quality in-game once, then try again.");

        using var k = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true)!;
        int current = k.GetValue(name) is int i ? i : 3;
        // Default Unity ladder: Very Low, Low, Medium, High, Very High, Ultra.
        int levels = Math.Max(6, current + 1);
        int avg = (int)Math.Round(plan.Levels.Values.Average());
        int target = avg switch { 0 => 1, 1 => 2, 2 => 3, _ => levels - 1 };

        BackupService.Backup(game.BackupId, [], new Dictionary<string, string> { [$"{KeyPath}|{name}"] = current.ToString(CultureInfo.InvariantCulture) });
        if (current == target) return new ApplyResult(true, "Quality level already matches.", 0, Target);
        k.SetValue(name, target, RegistryValueKind.DWord);
        return new ApplyResult(true, $"Unity quality level {current} → {target}. A backup was saved.", 1, Target);
    }
}
