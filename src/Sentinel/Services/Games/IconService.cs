using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sentinel.Core;
using Sentinel.Models;

namespace Sentinel.Services.Games;

/// <summary>
/// Finds artwork for a game, in order: Steam's local library cache, the Steam CDN (by app id, cached on disk),
/// the icon embedded in the game's executable. Callers fall back to the letter tile when nothing is found.
/// </summary>
public static class IconService
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        // Some networks stall ~10 s on IPv6 (AAAA) lookups for Steam's CDN; resolve IPv4 first, fall back to anything.
        ConnectCallback = async (ctx, ct) =>
        {
            var host = ctx.DnsEndPoint.Host;
            var addresses = await System.Net.Dns.GetHostAddressesAsync(host, System.Net.Sockets.AddressFamily.InterNetwork, ct);
            if (addresses.Length == 0) addresses = await System.Net.Dns.GetHostAddressesAsync(host, ct);
            var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(addresses, ctx.DnsEndPoint.Port, ct);
                return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly SemaphoreSlim Downloads = new(6);
    private static readonly ConcurrentDictionary<string, Task<GameArt?>> Pending = new();
    private static readonly ConcurrentDictionary<string, Task<ImageSource?>> Heroes = new();
    private static readonly Lazy<string?> SteamCache = new(() =>
        GameScanner.SteamRoot() is { } root && Directory.Exists(Path.Combine(root, "appcache", "librarycache"))
            ? Path.Combine(root, "appcache", "librarycache") : null);

    private static string CacheDir
    {
        get
        {
            var dir = Path.Combine(AppPaths.DataRoot, "Icons");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    static IconService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Sentinel-GameOptimizer/1.0");
    }

    public static Task<GameArt?> GetAsync(int? steamAppId, string? exePath, string? artUrl = null)
    {
        var key = steamAppId is int id ? $"steam-{id}"
                : exePath is not null ? $"exe-{exePath.ToLowerInvariant()}"
                : artUrl is not null ? $"url-{artUrl}"
                : null;
        if (key is null) return Task.FromResult<GameArt?>(null);
        return Pending.GetOrAdd(key, _ => Task.Run(() => Load(steamAppId, exePath, artUrl)));
    }

    /// <summary>
    /// Wide banner art (Steam headers, ~2.15:1) is shown whole in banner-shaped tiles. Square icons (exe icons,
    /// Steam's small store icon) are drawn centred inside the tile instead of being stretched. Portrait covers are
    /// never used — no crop of a 2:3 cover reads well in a banner.
    /// </summary>
    private static async Task<GameArt?> Load(int? appId, string? exePath, string? artUrl)
    {
        try
        {
            if (appId is int id)
            {
                if (FindLocalSteamHeader(id) is { } local) return new GameArt(Decode(await File.ReadAllBytesAsync(local)), Wide: true);
                if (await DownloadSteamArt(id) is { } bytes) return new GameArt(Decode(bytes), Wide: true);
                if (FindLocalSteamIcon(id) is { } icon) return new GameArt(Decode(await File.ReadAllBytesAsync(icon)), Wide: false);
            }
            if (artUrl is not null && await DownloadStoreArt(artUrl) is { } storeArt) return new GameArt(Decode(storeArt), Wide: true);
            if (exePath is not null && File.Exists(exePath) && ExtractExeIcon(exePath) is { } exeIcon) return new GameArt(exeIcon, Wide: false);
        }
        catch (Exception ex) { Log.Error($"icon:{appId?.ToString() ?? exePath}", ex); }
        return null;
    }

    // ---------------------------------------------------------------- Steam

    private static string? SteamAppCacheDir(int appId) =>
        SteamCache.Value is { } cache && Directory.Exists(Path.Combine(cache, appId.ToString())) ? Path.Combine(cache, appId.ToString()) : null;

    private static string? FindLocalSteamHeader(int appId)
    {
        // Current layout: librarycache\<appid>\<hash>\header.jpg (or library_header.jpg on newer titles);
        // older clients used librarycache\<appid>_header.jpg.
        if (SteamAppCacheDir(appId) is { } dir)
        {
            foreach (var name in new[] { "header.jpg", "library_header.jpg" })
            {
                var hit = Directory.EnumerateFiles(dir, name, SearchOption.AllDirectories).FirstOrDefault();
                if (hit is not null) return hit;
            }
        }
        var legacy = SteamCache.Value is { } c ? Path.Combine(c, $"{appId}_header.jpg") : null;
        return legacy is not null && File.Exists(legacy) ? legacy : null;
    }

    /// <summary>Steam's 32×32 store icon (a SHA-1-named jpg at the top of the app's cache folder).</summary>
    private static string? FindLocalSteamIcon(int appId) =>
        SteamAppCacheDir(appId) is { } dir
            ? Directory.EnumerateFiles(dir, "*.jpg").FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Length == 40)
            : null;

    private static async Task<byte[]?> DownloadSteamArt(int appId)
    {
        var cached = Path.Combine(CacheDir, $"steam-{appId}.jpg");
        var missing = cached + ".missing";
        if (File.Exists(cached)) return await File.ReadAllBytesAsync(cached);
        // Remember misses for a week so delisted/odd apps aren't re-requested on every launch.
        if (File.Exists(missing) && DateTime.UtcNow - File.GetLastWriteTimeUtc(missing) < TimeSpan.FromDays(7)) return null;

        await Downloads.WaitAsync();
        bool networkError = false;
        void OnNetworkError() => networkError = true;
        try
        {
            foreach (var url in new[]
                     {
                         $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg",
                         $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg",
                     })
            {
                if (await TryGet(url, OnNetworkError) is { } bytes)
                {
                    await File.WriteAllBytesAsync(cached, bytes);
                    return bytes;
                }
            }

            // Newer apps keep art under a hashed path; the store API returns the real header URL.
            if (await TryGetString($"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic", OnNetworkError) is { } json)
            {
                var m = System.Text.RegularExpressions.Regex.Match(json, "\"header_image\"\\s*:\\s*\"([^\"]+)\"");
                if (m.Success && await TryGet(m.Groups[1].Value.Replace("\\/", "/"), OnNetworkError) is { } bytes)
                {
                    await File.WriteAllBytesAsync(cached, bytes);
                    return bytes;
                }
            }
            // Only a definite "no art" answer is remembered; timeouts and offline errors retry next launch.
            if (!networkError) await File.WriteAllTextAsync(missing, "");
            return null;
        }
        finally { Downloads.Release(); }
    }

    /// <summary>Store art by URL (GOG covers etc.), cached under a hash of the URL.</summary>
    /// <summary>
    /// Full-resolution banner for the game-detail hero (only ever requested for the game being viewed).
    /// Reuses the tile's on-disk cache, so it rarely costs a second download.
    /// </summary>
    public static Task<ImageSource?> GetHeroAsync(int? steamAppId, string? artUrl)
    {
        var key = steamAppId is int id ? $"steam-{id}" : artUrl is not null ? $"url-{artUrl}" : null;
        if (key is null) return Task.FromResult<ImageSource?>(null);
        return Heroes.GetOrAdd(key, _ => Task.Run(async () =>
        {
            try
            {
                if (steamAppId is int sid)
                {
                    if (FindLocalSteamHeader(sid) is { } local) return Decode(await File.ReadAllBytesAsync(local), 215);
                    if (await DownloadSteamArt(sid) is { } bytes) return Decode(bytes, 215);
                }
                if (artUrl is not null && await DownloadStoreArt(artUrl, tile: 398) is { } store) return Decode(store, 182);
            }
            catch (Exception ex) { Log.Error($"hero:{steamAppId?.ToString() ?? artUrl}", ex); }
            return (ImageSource?)null;
        }));
    }

    private static async Task<byte[]?> DownloadStoreArt(string url, int tile = 256)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        // GOG serves the full-size cover as a ~1.3 MB PNG; its 256×117 tile variant is the same banner at ~16 KB.
        if (uri.Host == "images.gog-statics.com" && System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, @"^/[0-9a-f]{64}\.(png|jpg)$"))
            url = $"https://images.gog-statics.com{uri.AbsolutePath[..65]}_product_tile_{tile}.jpg";
        var name = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(url)))[..16];
        var cached = Path.Combine(CacheDir, $"url-{name}.img");
        if (File.Exists(cached)) return await File.ReadAllBytesAsync(cached);

        await Downloads.WaitAsync();
        try
        {
            var bytes = await TryGet(url, () => { });
            if (bytes is not null) await File.WriteAllBytesAsync(cached, bytes);
            return bytes;
        }
        finally { Downloads.Release(); }
    }

    private static async Task<byte[]?> TryGet(string url, Action onNetworkError)
    {
        try
        {
            using var resp = await Http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            return bytes.Length < 1000 ? null : bytes;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Net.Sockets.SocketException)
        {
            onNetworkError();
            return null;
        }
    }

    private static async Task<string?> TryGetString(string url, Action onNetworkError)
    {
        try { return await Http.GetStringAsync(url); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Net.Sockets.SocketException)
        {
            onNetworkError();
            return null;
        }
    }

    // Tallest tile is the 64-px detail header; decoding at 72 px keeps it sharp while a banner costs ~45 KB.
    private const int DecodeHeight = 72;

    /// <summary>Decodes the whole image at small size — no cropping, the tiles are shaped to fit the art.</summary>
    private static ImageSource Decode(byte[] bytes, int height = DecodeHeight)
    {
        using var ms = new MemoryStream(bytes);
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.DecodePixelHeight = height;
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }

    // ---------------------------------------------------------------- exe icon

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHDefExtractIcon(string pszIconFile, int iIndex, uint uFlags, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIconSize);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static ImageSource? ExtractExeIcon(string exe)
    {
        // Low word = large icon size, high word = small icon size.
        if (SHDefExtractIcon(exe, 0, 0, out var large, out var small, (16u << 16) | 128u) != 0 || large == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(large, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally
        {
            DestroyIcon(large);
            if (small != IntPtr.Zero) DestroyIcon(small);
        }
    }

    public sealed record GameArt(ImageSource Image, bool Wide);

    public static int? SteamIdFor(CatalogGame catalog, InstalledGame? installed) =>
        installed is { Launcher: Launcher.Steam } && int.TryParse(installed.AppId, out var id) ? id
        : catalog.SteamIds.Length > 0 ? catalog.SteamIds[0] : null;
}
