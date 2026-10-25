namespace Sentinel.Core;

/// <summary>
/// `--demo` swaps in the fictional PC and game library from <see cref="Services.DemoData"/> (README screenshots, walkthroughs).
/// While it's on, saved settings aren't loaded or written and no button changes Windows or a game's files.
/// </summary>
public static class AppMode
{
    public static bool Demo { get; set; }
}
