using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using Sentinel.Core;
using Sentinel.Models;
using Sentinel.Services;
using Sentinel.Services.Catalog;
using Sentinel.Services.Games;
using Sentinel.Services.Hardware;
using Sentinel.Services.Tweaks;

namespace Sentinel.ViewModels;

public enum Page { Dashboard, Library, Catalog, Tweaks }

public sealed record SpecCard(string Icon, string Title, string Value, string Detail, double Score, string ScoreLabel, System.Windows.Media.Color Tint)
{
    public System.Windows.Media.Brush AccentBrush { get; } = Frozen(new System.Windows.Media.SolidColorBrush(Tint));
    public System.Windows.Media.Brush SoftBrush { get; } = Frozen(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x2E, Tint.R, Tint.G, Tint.B)));

    private static System.Windows.Media.Brush Frozen(System.Windows.Media.Brush b)
    {
        b.Freeze();
        return b;
    }
}

public sealed class MainViewModel : ObservableObject
{
    private HardwareProfile _hw = new();
    public HardwareProfile Hardware { get => _hw; private set => Set(ref _hw, value); }

    private Page _page = Page.Dashboard;
    public Page CurrentPage { get => _page; set => Set(ref _page, value); }

    private bool _scanning;
    public bool IsScanning { get => _scanning; private set => Set(ref _scanning, value); }
    private string _status = "Starting…";
    public string StatusText { get => _status; private set => Set(ref _status, value); }

    // ---- dashboard
    public ObservableCollection<SpecCard> Specs { get; } = [];
    public string CpuLine => Hardware.Cpu.Name;
    public string GpuLine => Hardware.Gpu.Name;
    public int OverallScore => Hardware.OverallScore;
    public string Tier => Hardware.Tier;
    public string TierBlurb => Hardware.Tier switch
    {
        "Enthusiast" => "Top-tier rig. Ultra settings and high refresh are on the table in almost everything.",
        "High-end" => "Excellent gaming PC. High/Ultra at 1440p in most games.",
        "Upper mid-range" => "Strong all-rounder. High settings at 1080p–1440p with smart upscaling.",
        "Mid-range" => "Solid 1080p machine. Expect Medium–High, with upscaling in new releases.",
        "Entry level" => "Esports and older titles run great; new releases need Low–Medium and upscaling.",
        _ => "Focus on lighter games and use the lowest presets plus upscaling in demanding ones."
    };
    public int GameCount => Games.Count;
    public int AutoTuneCount => Games.Count(g => g.CanAutoApply);
    public int RecommendedTweakCount => Tweaks.Count(t => t.Status is TweakStatus.Recommended or TweakStatus.Warning);
    public int CatalogCount => GameCatalog.Instance.Games.Count;
    public string CatalogCountLabel => CatalogCount.ToString("N0");
    public string VersionLabel { get; } = typeof(MainViewModel).Assembly.GetName().Version is { } v ? $"v{v.Major}.{v.Minor}.{v.Build}" : "v1.0.0";
    public RelayCommand OpenRepoCommand { get; } = new(() => TweakService.OpenUrl("https://github.com/BenScriptsRBX/Sentinel"));
    public string LibrarySubtitle => $"{GameCount} installed · {AutoTuneCount} can be auto-tuned";
    public string CatalogSubtitle => $"{CatalogCountLabel} games from Steam, GOG and other launchers · estimates use your hardware";
    public string CatalogCountShort => CatalogCount >= 1000 ? $"{CatalogCount / 1000d:0.#}k" : CatalogCount.ToString();
    public IEnumerable<Tweak> TopTweaks => Tweaks.Where(t => t.Status != TweakStatus.Optimal).Take(4).ToList();

    // PC health = share of actionable checks that are already in good shape (tips don't count either way).
    public int OptimalCount => Tweaks.Count(t => t.Status == TweakStatus.Optimal);
    public int RecommendedCount => Tweaks.Count(t => t.Status == TweakStatus.Recommended);
    public int AttentionCount => Tweaks.Count(t => t.Status == TweakStatus.Warning);
    public int TipCount => Tweaks.Count(t => t.Status == TweakStatus.Info);
    public int HealthPercent
    {
        get
        {
            int actionable = OptimalCount + RecommendedCount + AttentionCount;
            return actionable == 0 ? 100 : (int)Math.Round(OptimalCount * 100d / actionable);
        }
    }
    public string HealthLabel => HealthPercent switch { >= 90 => "Excellent", >= 70 => "Good", >= 50 => "Fair", _ => "Needs work" };
    public System.Windows.GridLength OptimalStar => Star(OptimalCount);
    public System.Windows.GridLength RecommendedStar => Star(RecommendedCount);
    public System.Windows.GridLength AttentionStar => Star(AttentionCount);
    public System.Windows.GridLength TipStar => Star(TipCount);
    private static System.Windows.GridLength Star(int n) => new(Math.Max(n, 0.0001), System.Windows.GridUnitType.Star);

    private void RaiseHealth()
    {
        foreach (var p in new[] { nameof(OptimalCount), nameof(RecommendedCount), nameof(AttentionCount), nameof(TipCount), nameof(HealthPercent), nameof(HealthLabel),
                                  nameof(OptimalStar), nameof(RecommendedStar), nameof(AttentionStar), nameof(TipStar), nameof(RecommendedTweakCount), nameof(TopTweaks) })
            OnPropertyChanged(p);
    }

    // ---- library
    public ObservableCollection<GameViewModel> Games { get; } = [];
    public ICollectionView GamesView { get; }
    private string _search = "";
    public string Search { get => _search; set { if (Set(ref _search, value)) GamesView.Refresh(); } }
    private bool _autoOnly;
    public bool AutoOnly { get => _autoOnly; set { if (Set(ref _autoOnly, value)) GamesView.Refresh(); } }
    private GameViewModel? _selected;
    public GameViewModel? SelectedGame
    {
        get => _selected;
        set { if (Set(ref _selected, value)) value?.EnsureComputed(); }
    }

    // ---- catalog
    public ObservableCollection<GameViewModel> CatalogGames { get; } = [];
    public ICollectionView CatalogView { get; }
    private string _catalogSearch = "";
    public string CatalogSearch { get => _catalogSearch; set { if (Set(ref _catalogSearch, value)) CatalogView.Refresh(); } }
    public IReadOnlyList<string> GenreFilters { get; private set; } = ["All genres"];
    public IReadOnlyList<string> SortModes { get; } = ["Most popular", "A–Z"];
    private string _sortMode = "Most popular";
    public string SortMode
    {
        get => _sortMode;
        set
        {
            if (!Set(ref _sortMode, value)) return;
            ((ListCollectionView)CatalogView).CustomSort = value == "A–Z" ? AlphabeticalSort.Instance : null;
        }
    }

    private sealed class AlphabeticalSort : System.Collections.IComparer
    {
        public static readonly AlphabeticalSort Instance = new();
        public int Compare(object? x, object? y) =>
            StringComparer.OrdinalIgnoreCase.Compare((x as GameViewModel)?.Catalog.SortName, (y as GameViewModel)?.Catalog.SortName);
    }
    private string _genre = "All genres";
    public string GenreFilter { get => _genre; set { if (Set(ref _genre, value)) CatalogView.Refresh(); } }
    private GameViewModel? _selectedCatalog;
    public GameViewModel? SelectedCatalogGame
    {
        get => _selectedCatalog;
        set { if (Set(ref _selectedCatalog, value)) value?.EnsureComputed(); }
    }

    // ---- tweaks
    public ObservableCollection<Tweak> Tweaks { get; } = [];

    public AsyncCommand RescanCommand { get; }
    public RelayCommand NavigateCommand { get; }
    public AsyncCommand ApplyTweakCommand { get; }
    public AsyncCommand RevertTweakCommand { get; }
    public AsyncCommand ApplyAllTweaksCommand { get; }
    public RelayCommand OpenGameCommand { get; }

    public MainViewModel()
    {
        GamesView = CollectionViewSource.GetDefaultView(Games);
        GamesView.Filter = o => o is GameViewModel g &&
                                (Search.Length == 0 || g.Name.Contains(Search, StringComparison.OrdinalIgnoreCase)) &&
                                (!AutoOnly || g.CanAutoApply);
        CatalogView = new ListCollectionView(CatalogGames)
        {
            Filter = o => o is GameViewModel g &&
                          (CatalogSearch.Length == 0 || g.Name.Contains(CatalogSearch, StringComparison.OrdinalIgnoreCase) ||
                           g.Engine.Contains(CatalogSearch, StringComparison.OrdinalIgnoreCase)) &&
                          (GenreFilter == "All genres" || g.Genre == GenreFilter)
        };

        RescanCommand = new AsyncCommand(ScanAsync);
        NavigateCommand = new RelayCommand(p => { if (p is Page page) CurrentPage = page; else if (Enum.TryParse<Page>(p?.ToString(), out var pg)) CurrentPage = pg; });
        ApplyTweakCommand = new AsyncCommand(p => RunTweak(p as Tweak, revert: false));
        RevertTweakCommand = new AsyncCommand(p => RunTweak(p as Tweak, revert: true));
        ApplyAllTweaksCommand = new AsyncCommand(ApplyAllSafe, () => Tweaks.Any(t => t.IsSafeAuto && !t.RequiresAdmin));
        OpenGameCommand = new RelayCommand(p =>
        {
            if (p is not GameViewModel g) return;
            SelectedGame = Games.FirstOrDefault(x => x.Id == g.Id) ?? g;
            CurrentPage = Page.Library;
        });
    }

    public async Task ScanAsync()
    {
        IsScanning = true;
        try
        {
            StatusText = "Detecting hardware…";
            Hardware = await HardwareDetector.DetectAsync();
            BuildSpecs();

            StatusText = "Scanning launchers for games…";
            var installed = await GameScanner.ScanAsync();

            StatusText = "Building recommendations…";
            var hw = Hardware;
            var (games, catalog, tweaks) = await Task.Run(() =>
            {
                var g = installed.Select(i => new GameViewModel(hw, i.Catalog ?? Report.Fallback(i), i)).ToList();
                var installedIds = g.Select(x => x.Id).ToHashSet();
                var c = GameCatalog.Instance.Games.Select(cg => g.FirstOrDefault(x => x.Id == cg.Id) ?? new GameViewModel(hw, cg, null)).ToList();
                return (g, c, TweakService.Build(hw, installed));
            });

            Games.Clear();
            foreach (var g in games) Games.Add(g);
            CatalogGames.Clear();
            foreach (var c in catalog) CatalogGames.Add(c);
            // Only genres with a meaningful number of games, so the filter stays usable with 11k entries.
            GenreFilters = ["All genres", .. catalog.GroupBy(c => c.Genre).Where(g => g.Count() >= 15).Select(g => g.Key).Order()];
            OnPropertyChanged(nameof(GenreFilters));
            Tweaks.Clear();
            foreach (var t in tweaks) Tweaks.Add(t);
            BuildSpecs();

            SelectedGame ??= Games.FirstOrDefault(g => g.CanAutoApply) ?? Games.FirstOrDefault();
            SelectedCatalogGame ??= CatalogGames.FirstOrDefault();
            StatusText = $"{Games.Count} games · {CatalogCount} in catalog";
        }
        catch (Exception ex)
        {
            Log.Error("scan", ex);
            StatusText = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            foreach (var p in new[] { nameof(CpuLine), nameof(GpuLine), nameof(OverallScore), nameof(Tier), nameof(TierBlurb), nameof(GameCount), nameof(AutoTuneCount), nameof(RecommendedTweakCount), nameof(TopTweaks), nameof(LibrarySubtitle), nameof(CatalogSubtitle) })
                OnPropertyChanged(p);
            RaiseHealth();
        }
    }

    private void BuildSpecs()
    {
        var hw = Hardware;
        static System.Windows.Media.Color C(uint rgb) => System.Windows.Media.Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        Specs.Clear();
        Specs.Add(new SpecCard("\uE7F8", "Graphics", hw.Gpu.Name,
            hw.Gpu.IsIntegrated ? "Integrated graphics (shared memory)" : $"{hw.Gpu.VramGb:0} GB VRAM · driver {hw.Gpu.DriverVersion}" + (hw.Gpus.Count > 1 ? $" · +{hw.Gpus.Count - 1} more adapter(s)" : ""),
            Math.Min(hw.Gpu.Score / 2.6, 100), $"score {hw.Gpu.Score:0}", C(0xA78BFA)));
        Specs.Add(new SpecCard("\uE950", "Processor", hw.Cpu.Name,
            $"{hw.Cpu.Cores} cores / {hw.Cpu.Threads} threads · up to {hw.Cpu.MaxClockMhz / 1000d:0.0} GHz",
            Math.Min(hw.Cpu.Score / 1.66, 100), $"score {hw.Cpu.Score:0}", C(0x22D3EE)));
        Specs.Add(new SpecCard("\uE964", "Memory", $"{Math.Round(hw.Memory.InstalledGb)} GB {hw.Memory.Type}".Trim(),
            $"{hw.Memory.ConfiguredSpeedMhz} MT/s · {hw.Memory.ModuleCount} module(s) · {hw.Memory.LoadPercent}% in use",
            Math.Min(hw.Memory.InstalledGb / 64 * 100, 100), $"{Math.Round(hw.Memory.InstalledGb)} GB", C(0x34D399)));
        Specs.Add(new SpecCard("\uE7F4", "Display", $"{hw.Display.Width} × {hw.Display.Height} @ {hw.Display.RefreshHz} Hz",
            $"{hw.Display.FriendlyName}" + (hw.Display.MaxRefreshHz > hw.Display.RefreshHz ? $" · supports {hw.Display.MaxRefreshHz} Hz" : "") + (hw.Displays.Count > 1 ? $" · {hw.Displays.Count} displays" : ""),
            Math.Min(hw.Display.RefreshHz / 360d * 100, 100), $"{hw.Display.RefreshHz} Hz", C(0xFBBF24)));
        var ssd = hw.Disks.Count(d => d.IsSsd);
        Specs.Add(new SpecCard("\uEDA2", "Storage", $"{hw.Disks.Count} drive(s) · {hw.Disks.Sum(d => d.TotalBytes) / 1e12:0.#} TB",
            string.Join(" · ", hw.Disks.Select(d => $"{d.Root.TrimEnd('\\')} {d.MediaType}{(d.Bus.Length > 0 ? $" {d.Bus}" : "")} {d.FreeBytes / 1_073_741_824d:0} GB free")),
            hw.Disks.Count == 0 ? 0 : ssd * 100d / hw.Disks.Count, ssd == hw.Disks.Count ? "All SSD" : $"{ssd}/{hw.Disks.Count} SSD", C(0x60A5FA)));
        int actionable = OptimalCount + RecommendedCount + AttentionCount;
        Specs.Add(new SpecCard("\uE770", "System", hw.System.OsName,
            $"Build {hw.System.OsBuild} · {hw.System.PowerPlanName} power plan" + (hw.System.HasBattery ? (hw.System.OnBattery ? " · on battery" : " · plugged in") : ""),
            HealthPercent, actionable == 0 ? "" : $"{OptimalCount}/{actionable} tuned", C(0xF472B6)));
    }

    private async Task RunTweak(Tweak? t, bool revert)
    {
        if (t is null) return;
        var action = revert ? t.RevertAction : t.ApplyAction;
        if (action is null) return;
        try
        {
            t.Result = await action();
            if (t.ActionLabel == "Apply")
            {
                t.Status = revert ? TweakStatus.Recommended : TweakStatus.Optimal;
                t.CanRevert = !revert && t.RevertAction is not null;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"tweak:{t.Id}", ex);
            t.Result = $"Failed: {ex.Message}";
        }
        t.Refresh();
        RaiseHealth();
    }

    private async Task ApplyAllSafe()
    {
        foreach (var t in Tweaks.Where(t => t.IsSafeAuto && !t.RequiresAdmin).ToList())
            await RunTweak(t, revert: false);
    }
}
