using System.Text.Json;

namespace Sentinel.Core;

public sealed class GameTarget
{
    public int Min { get; set; }
    public int Max { get; set; }
    public bool Competitive { get; set; }
    public bool AllowRayTracing { get; set; } = true;
    public bool AllowFrameGen { get; set; } = true;
    public bool AllowUpscaling { get; set; } = true;
    public int ResolutionWidth { get; set; }
    public int ResolutionHeight { get; set; }
}

/// <summary>Per-user preferences, including the fps target remembered for each game.</summary>
public sealed class AppSettings
{
    public Dictionary<string, GameTarget> Games { get; set; } = [];

    private static AppSettings? _current;
    public static AppSettings Current => _current ??= Load();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.Settings))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.Settings)) ?? new();
        }
        catch (Exception ex) { Log.Error("settings", ex); }
        return new();
    }

    public void Save()
    {
        try { File.WriteAllText(AppPaths.Settings, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { Log.Error("settings-save", ex); }
    }
}
