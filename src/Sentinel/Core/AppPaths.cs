namespace Sentinel.Core;

public static class AppPaths
{
    /// <summary>%LOCALAPPDATA%\Sentinel, overridable with SENTINEL_DATA (used by tests).</summary>
    public static string DataRoot { get; } = Environment.GetEnvironmentVariable("SENTINEL_DATA") is { Length: > 0 } custom
        ? custom
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sentinel");

    public static string Backups => Ensure(Path.Combine(DataRoot, "Backups"));
    public static string TweakState => Path.Combine(Ensure(DataRoot), "tweak-state.json");
    public static string Settings => Path.Combine(Ensure(DataRoot), "settings.json");
    public static string Log => Path.Combine(Ensure(DataRoot), "sentinel.log");

    private static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}

public static class Log
{
    private static readonly Lock Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
                File.AppendAllText(AppPaths.Log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    public static void Error(string context, Exception ex) => Write($"[ERR] {context}: {ex.GetType().Name}: {ex.Message}");
}
