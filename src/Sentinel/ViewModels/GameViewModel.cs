using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media;
using Sentinel.Core;
using Sentinel.Models;
using Sentinel.Services;
using Sentinel.Services.Config;
using Sentinel.Services.Games;
using Sentinel.Services.Optimizer;
using Sentinel.Services.Tweaks;

namespace Sentinel.ViewModels;

public sealed record FpsPreset(string Label, string Range, int Min, int Max);

public sealed record ResolutionOption(string Label, Resolution Value)
{
    public override string ToString() => Label;
}

public sealed record PresetEstimate(string Name, string Fps, double Bar);

/// <summary>An fps preset as offered for one game: greyed out when this PC can't reach it.</summary>
public sealed record PresetOption(FpsPreset Preset, bool Achievable, string Hint);

/// <summary>One game (installed, or a catalog entry) plus its fps-target optimisation state.</summary>
public sealed class GameViewModel : ObservableObject
{
    private readonly HardwareProfile _hw;
    private readonly IConfigAdapter? _adapter;
    private bool _loading;

    public InstalledGame? Installed { get; }
    public CatalogGame Catalog { get; }
    public bool IsInstalled => Installed is not null;
    public bool IsCatalogued => Installed is null || Installed.InCatalog;

    public string Name => Installed?.DisplayName ?? Catalog.Name;
    public string Initial => Name.TrimStart().FirstOrDefault(char.IsLetterOrDigit).ToString().ToUpperInvariant();
    public Brush AvatarBrush { get; }

    // Artwork is requested the first time a row is actually drawn, so an 11k-game catalog only loads what you scroll past.
    // Tiles are banner-shaped (Steam header ratio): wide art fills them, square exe icons sit centred on a dark plate.
    private static readonly Brush IconPlate = Frozen(new SolidColorBrush(Color.FromRgb(0x1E, 0x23, 0x30)));
    private Brush? _bannerBrush;
    private ImageSource? _squareIcon;
    private bool _iconRequested;

    public Brush AvatarBackground
    {
        get
        {
            RequestIcon();
            return _bannerBrush ?? (_squareIcon is not null ? IconPlate : AvatarBrush);
        }
    }
    public ImageSource? SquareIcon => _squareIcon;

    // Full-resolution banner behind the game-detail header; the letter-tile gradient stands in until (or unless) it loads.
    private Brush? _heroBrush;
    private bool _heroRequested;
    public Brush HeroBackground
    {
        get
        {
            RequestHero();
            return _heroBrush ?? AvatarBrush;
        }
    }
    public bool HasHero => _heroBrush is not null;

    private void RequestHero()
    {
        if (_heroRequested) return;
        _heroRequested = true;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        IconService.GetHeroAsync(IconService.SteamIdFor(Catalog, Installed), Catalog.ArtUrl).ContinueWith(t =>
        {
            if (t.Status != TaskStatus.RanToCompletion || t.Result is not { } image) return;
            dispatcher.BeginInvoke(() =>
            {
                _heroBrush = Frozen(new ImageBrush(image) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Right });
                OnPropertyChanged(nameof(HeroBackground));
                OnPropertyChanged(nameof(HasHero));
            });
        }, TaskScheduler.Default);
    }
    public bool HasIcon => _bannerBrush is not null || _squareIcon is not null;

    private static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }

    private void RequestIcon()
    {
        if (_iconRequested) return;
        _iconRequested = true;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        IconService.GetAsync(IconService.SteamIdFor(Catalog, Installed), Installed?.IconPath ?? Installed?.MainExe, Catalog.ArtUrl).ContinueWith(t =>
        {
            if (t.Status != TaskStatus.RanToCompletion || t.Result is not { } art) return;
            dispatcher.BeginInvoke(() =>
            {
                if (art.Wide) _bannerBrush = Frozen(new ImageBrush(art.Image) { Stretch = Stretch.UniformToFill });
                else _squareIcon = art.Image;
                OnPropertyChanged(nameof(AvatarBackground));
                OnPropertyChanged(nameof(SquareIcon));
                OnPropertyChanged(nameof(HasIcon));
            });
        }, TaskScheduler.Default);
    }
    public string Launcher => Installed?.LauncherLabel ??
        (Catalog.AllStores.Any() ? $"Not installed · {string.Join(", ", Catalog.AllStores)}" : "Not installed");
    public string Engine => Installed?.Engine is { } e && e != "Unknown" ? e : Catalog.Engine;
    public string Genre => string.IsNullOrEmpty(Catalog.Genre) ? "Game" : Catalog.Genre;
    public string? InstallPath => Installed?.InstallPath;
    public string Year => Catalog.Year > 0 ? Catalog.Year.ToString() : "";
    public double GpuDemand => Catalog.GpuDemand * 10;
    public double CpuDemand => Catalog.CpuDemand * 10;
    public string GpuDemandLabel => DemandLabel(Catalog.GpuDemand);
    public string CpuDemandLabel => DemandLabel(Catalog.CpuDemand);
    public IReadOnlyList<string> FeatureLabels { get; }
    public string FeatureLine => FeatureLabels.Count == 0 ? "" : string.Join(" · ", FeatureLabels);

    // ---- auto-apply support
    public bool CanAutoApply => _adapter is not null;
    public string SupportLabel => _adapter is null ? "Guide" : _adapter.Experimental ? "Auto · beta" : "Auto-tune";
    public string SupportDetail => _adapter is null
        ? "Sentinel can't write this game's settings automatically — follow the recommended settings below in the in-game menu."
        : _adapter.Description;
    public string AdapterName => Installed is null ? "Estimates only" : _adapter?.Name ?? "In-game guide";
    public string? ConfigPath => _adapter?.Target;
    public bool HasConfig => ConfigPath is not null;
    public bool HasBackup => BackupService.HasBackup(Id);
    public string Id => Catalog.Id;

    // ---- inputs
    public static IReadOnlyList<FpsPreset> Presets { get; } =
    [
        new("Cinematic", "30–45", 30, 45),
        new("Smooth", "60–75", 60, 75),
        new("High refresh", "90–120", 90, 120),
        new("Competitive", "144–165", 144, 165),
        new("Esports", "240–360", 240, 360),
    ];
    public IReadOnlyList<ResolutionOption> Resolutions { get; }

    private int _minFps = 60, _maxFps = 120;
    public int MinFps { get => _minFps; set { if (Set(ref _minFps, Math.Clamp(value, 15, 1000))) { if (_maxFps < _minFps) MaxFps = _minFps; OnInputChanged(); } } }
    public int MaxFps { get => _maxFps; set { if (Set(ref _maxFps, Math.Clamp(value, 15, 1000))) { if (_minFps > _maxFps) MinFps = _maxFps; OnInputChanged(); } } }

    private FpsPreset? _preset;
    public FpsPreset? SelectedPreset
    {
        get => _preset;
        set
        {
            if (!Set(ref _preset, value) || value is null) return;
            _loading = true;
            MinFps = value.Min;
            MaxFps = value.Max;
            _loading = false;
            OnInputChanged();
            OnPropertyChanged(nameof(SelectedPresetOption));
        }
    }

    public ObservableCollection<PresetOption> PresetOptions { get; } = [];
    public PresetOption? SelectedPresetOption
    {
        get => PresetOptions.FirstOrDefault(o => o.Preset == _preset);
        set { if (value is { Achievable: true }) SelectedPreset = value.Preset; else OnPropertyChanged(); }
    }

    private ResolutionOption _resolution;
    public ResolutionOption Resolution { get => _resolution; set { if (Set(ref _resolution, value)) OnInputChanged(); } }

    private bool _competitive;
    public bool Competitive { get => _competitive; set { if (Set(ref _competitive, value)) OnInputChanged(); } }
    private bool _allowRt = true;
    public bool AllowRayTracing { get => _allowRt; set { if (Set(ref _allowRt, value)) OnInputChanged(); } }
    private bool _allowFg = true;
    public bool AllowFrameGen { get => _allowFg; set { if (Set(ref _allowFg, value)) OnInputChanged(); } }
    private bool _allowUp = true;
    public bool AllowUpscaling { get => _allowUp; set { if (Set(ref _allowUp, value)) OnInputChanged(); } }

    // ---- outputs
    private OptimizationPlan? _plan;
    public OptimizationPlan? Plan { get => _plan; private set => Set(ref _plan, value); }
    public ObservableCollection<PlanRow> Rows { get; } = [];
    public ObservableCollection<ModernSetting> Modern { get; } = [];
    public ObservableCollection<string> Notes { get; } = [];
    public ObservableCollection<PresetEstimate> PresetFps { get; } = [];

    public string EstimatedFps => Plan is null ? "–" : $"{Plan.EstimatedFps:0}";
    public string BaseFpsLine => Plan is null ? "" :
        (Plan.TypicalFps > Plan.EstimatedFps + 1 ? $"expect {Plan.EstimatedFps:0}–{Plan.TypicalFps:0} fps" : "expected fps") +
        (Plan.FrameGen != FrameGenKind.None ? $" · {Plan.BaseFps:0} rendered + frame gen" : "");
    public string CeilingLine => Plan is null ? "" :
        Plan.EngineCap > 0 && Plan.Ceiling >= Plan.EngineCap - 0.5
            ? $"Locked to {Plan.EngineCap} fps by the game — your PC can hold that."
            : (Plan.Ceiling >= 1000 ? "Max achievable here: 1000+ fps" : $"Max achievable here ≈ {Plan.Ceiling:0} fps") +
              (Plan.EngineCap > 0 ? $" (game lock: {Plan.EngineCap})" : "");
    public bool TargetUnreachable => Plan is not null && MinFps > Plan.Ceiling + 0.5;

    // Values for the fps range bar.
    public double PlanEstimate => Plan?.EstimatedFps ?? 0;
    public double PlanTypical => Plan?.TypicalFps ?? 0;
    public double PlanCeiling => Plan?.Ceiling ?? 0;
    public double PlanEngineCap => Plan?.EngineCap ?? 0;
    public double DisplayRefresh => _hw.Display.RefreshHz;
    public string StoresLine => string.Join(" · ", Catalog.AllStores);
    public string BoundLabel => Plan is null ? "" : Plan.CpuBound ? "CPU-bound" : "GPU-bound";
    private string TargetRange => MinFps == MaxFps ? $"{MinFps}" : $"{MinFps}–{MaxFps}";
    public string TargetLabel => Plan is null ? "" : Plan.TargetReached ? $"Target {TargetRange} reached" : $"Below {MinFps} fps target";
    public bool TargetReached => Plan?.TargetReached ?? false;
    public string RenderLine => Plan is null ? "" :
        $"{Resolution.Value} · {(Plan.RenderScale < 100 ? $"{SettingsOptimizer.UpscalerName(Plan.Upscaler)} {SettingsOptimizer.ScaleModeName(Plan.RenderScale)}" : "native")} · cap {Plan.FpsCap}";

    /// <summary>Quick estimate shown in the catalog list: High preset at the chosen resolution.</summary>
    public string HighPresetFps { get; private set; } = "";
    public double HighPresetBar { get; private set; }

    private string? _status;
    public string? Status { get => _status; set => Set(ref _status, value); }
    private bool _statusIsError;
    public bool StatusIsError { get => _statusIsError; set => Set(ref _statusIsError, value); }

    public RelayCommand ApplyCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public RelayCommand OpenConfigCommand { get; }
    public RelayCommand OpenInstallCommand { get; }
    public RelayCommand ForceGpuCommand { get; }
    public RelayCommand SelectPresetCommand { get; }
    public bool ShowForceGpu => _hw.IsHybridGraphics && Installed?.MainExe is not null;

    public GameViewModel(HardwareProfile hw, CatalogGame catalog, InstalledGame? installed)
    {
        _hw = hw;
        Catalog = catalog;
        Installed = installed;
        _adapter = installed is null ? null : ConfigResolver.For(installed);
        FeatureLabels = catalog.Features.Where(f => f is not (GameFeature.Dx11 or GameFeature.Dx12 or GameFeature.Vulkan)).Select(GameFeature.Label).ToList();
        AvatarBrush = MakeAvatarBrush(Name);

        var native = new Resolution(Math.Max(hw.Display.Width, 1280), Math.Max(hw.Display.Height, 720));
        var list = new List<ResolutionOption> { new($"Native · {native}", native) };
        foreach (var (w, h) in new[] { (1280, 720), (1600, 900), (1920, 1080), (2560, 1080), (2560, 1440), (3440, 1440), (3840, 2160) })
            if (w != native.Width || h != native.Height) list.Add(new($"{w} × {h}", new Resolution(w, h)));
        Resolutions = list;
        _resolution = list[0];

        ApplyCommand = new RelayCommand(Apply, () => CanAutoApply && Plan is not null);
        RestoreCommand = new RelayCommand(Restore, () => HasBackup);
        OpenConfigCommand = new RelayCommand(() => Reveal(ConfigPath), () => HasConfig);
        OpenInstallCommand = new RelayCommand(() => Reveal(InstallPath), () => InstallPath is not null);
        ForceGpuCommand = new RelayCommand(ForceGpu, () => ShowForceGpu);
        SelectPresetCommand = new RelayCommand(p => SelectedPreset = p as FpsPreset);

        LoadTargets(hw);
        ComputeQuickEstimate();
    }

    private void LoadTargets(HardwareProfile hw)
    {
        _loading = true;
        if (AppSettings.Current.Games.TryGetValue(Id, out var t))
        {
            _minFps = t.Min; _maxFps = t.Max; _competitive = t.Competitive;
            _allowRt = t.AllowRayTracing; _allowFg = t.AllowFrameGen; _allowUp = t.AllowUpscaling;
            _resolution = Resolutions.FirstOrDefault(r => r.Value.Width == t.ResolutionWidth && r.Value.Height == t.ResolutionHeight) ?? Resolutions[0];
        }
        else
        {
            // Sensible default: competitive games aim at the display's refresh, others at a smooth high-refresh band.
            int refresh = Math.Max(hw.Display.RefreshHz, 60);
            _competitive = Catalog.Competitive;
            (_minFps, _maxFps) = Catalog.Competitive ? (Math.Min(refresh, 240) * 9 / 10, Math.Min(refresh, 360)) : (60, Math.Min(refresh, 120));
        }
        _preset = Presets.FirstOrDefault(p => p.Min == _minFps && p.Max == _maxFps);
        _loading = false;
    }

    /// <summary>Runs the optimizer. Called lazily when the game is first shown, then whenever an input changes.</summary>
    public void EnsureComputed()
    {
        if (Plan is not null) return;
        Recompute();
        // First look at a game with no saved target: don't default to a range this PC (or the game's own fps lock) can't reach.
        bool overCeiling = Plan!.Ceiling + 0.5 < _minFps;
        bool overLock = Plan.EngineCap > 0 && _maxFps > Plan.EngineCap;
        if (!AppSettings.Current.Games.ContainsKey(Id) && (overCeiling || overLock))
        {
            _loading = true;
            if (overCeiling) _minFps = Math.Max(20, (int)(Plan.Ceiling / 5) * 5);
            _maxFps = Math.Max(_maxFps, _minFps + 10);
            if (Plan.EngineCap > 0) _maxFps = Math.Max(_minFps, Math.Min(_maxFps, Plan.EngineCap));
            _preset = Presets.FirstOrDefault(p => p.Min == _minFps && p.Max == _maxFps);
            _loading = false;
            OnPropertyChanged(nameof(MinFps));
            OnPropertyChanged(nameof(MaxFps));
            Recompute();
        }
    }

    private void OnInputChanged()
    {
        if (_loading) return;
        if (_preset is not null && (_preset.Min != _minFps || _preset.Max != _maxFps)) Set(ref _preset, null, nameof(SelectedPreset));
        AppSettings.Current.Games[Id] = new GameTarget
        {
            Min = _minFps, Max = _maxFps, Competitive = _competitive, AllowRayTracing = _allowRt, AllowFrameGen = _allowFg,
            AllowUpscaling = _allowUp, ResolutionWidth = _resolution.Value.Width, ResolutionHeight = _resolution.Value.Height,
        };
        AppSettings.Current.Save();
        Recompute();
        ComputeQuickEstimate();
    }

    private void Recompute()
    {
        var req = new OptimizeRequest
        {
            MinFps = MinFps, MaxFps = MaxFps, Resolution = Resolution.Value, Competitive = Competitive,
            AllowRayTracing = AllowRayTracing, AllowFrameGen = AllowFrameGen, AllowUpscaling = AllowUpscaling,
        };
        var plan = SettingsOptimizer.Optimize(Catalog, _hw, req, _adapter?.PresetOnly ?? false);

        Rows.Clear();
        foreach (var (cat, level) in plan.Levels.OrderBy(k => (int)k.Key))
            Rows.Add(new PlanRow { Category = cat, Level = level, AutoApplied = _adapter?.Controls.Contains(cat) == true });

        Modern.Clear();
        foreach (var m in plan.Modern)
        {
            m.AutoApplied = _adapter is not null && (
                m.Name == "Frame rate cap" && _adapter.ControlsFpsCap ||
                m.Name == "Upscaling" && _adapter.ControlsRenderScale && plan.RenderScale < 100 ||
                m.Name == "V-Sync" && _adapter is UnrealAdapter or ProfileAdapter ||
                m.Name == "Texture quality" && _adapter.Controls.Contains(SettingCategory.Textures));
            Modern.Add(m);
        }

        Notes.Clear();
        foreach (var n in plan.Notes) Notes.Add(n);

        var presets = SettingsOptimizer.PresetEstimates(Catalog, _hw, Resolution.Value);
        double top = Math.Max(presets.Max(), 1);
        PresetFps.Clear();
        for (int i = 0; i < 4; i++)
            PresetFps.Add(new PresetEstimate(QualityLevel.Name(i), $"{presets[i]:0}", presets[i] / top * 100));

        PresetOptions.Clear();
        foreach (var p in Presets)
        {
            bool ok = p.Min <= plan.Ceiling + 0.5;
            PresetOptions.Add(new PresetOption(p, ok, ok ? $"{p.Range} fps is within reach" :
                plan.EngineCap > 0 && p.Min > plan.EngineCap ? $"{Name} is locked to {plan.EngineCap} fps" : $"Needs {p.Min}+ fps — this PC tops out ≈ {plan.Ceiling:0} here"));
        }

        Plan = plan;
        foreach (var p in new[] { nameof(EstimatedFps), nameof(BaseFpsLine), nameof(BoundLabel), nameof(TargetLabel), nameof(TargetReached),
                                  nameof(RenderLine), nameof(CeilingLine), nameof(TargetUnreachable), nameof(SelectedPresetOption),
                                  nameof(PlanEstimate), nameof(PlanTypical), nameof(PlanCeiling), nameof(PlanEngineCap) })
            OnPropertyChanged(p);
    }

    private void ComputeQuickEstimate()
    {
        var fps = SettingsOptimizer.PresetEstimates(Catalog, _hw, Resolution.Value)[QualityLevel.High];
        HighPresetFps = $"{fps:0}";
        HighPresetBar = Math.Min(fps / 240 * 100, 100);
        OnPropertyChanged(nameof(HighPresetFps));
        OnPropertyChanged(nameof(HighPresetBar));
    }

    private void Apply()
    {
        if (_adapter is null || Plan is null || Installed is null) return;
        if (ConfigResolver.IsRunning(Installed))
        {
            SetStatus("Close the game first — it rewrites its settings file when it exits.", error: true);
            return;
        }
        try
        {
            var result = _adapter.Apply(Plan);
            SetStatus(result.Message + (result.Success ? " Settings that are marked Manual still need to be set in-game." : ""), !result.Success);
        }
        catch (Exception ex)
        {
            Log.Error($"apply:{Name}", ex);
            SetStatus($"Couldn't write settings: {ex.Message}", error: true);
        }
        OnPropertyChanged(nameof(HasBackup));
        OnPropertyChanged(nameof(ConfigPath));
        OnPropertyChanged(nameof(HasConfig));
    }

    private void Restore()
    {
        try { SetStatus(BackupService.RestoreLatest(Id), error: false); }
        catch (Exception ex) { SetStatus($"Restore failed: {ex.Message}", error: true); }
        OnPropertyChanged(nameof(HasBackup));
    }

    private void ForceGpu()
    {
        if (Installed?.MainExe is null) return;
        TweakService.SetHighPerformanceGpu([Installed.MainExe]);
        SetStatus($"{Path.GetFileName(Installed.MainExe)} will now run on the {_hw.Gpu.Name}.", error: false);
    }

    private void SetStatus(string message, bool error)
    {
        StatusIsError = error;
        Status = message;
    }

    private static void Reveal(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
        else if (Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path}\"");
    }

    private static string DemandLabel(int d) => d switch { <= 2 => "Very light", <= 4 => "Light", <= 6 => "Moderate", <= 8 => "Heavy", _ => "Extreme" };

    private static readonly Color[] AvatarColors =
    [
        Color.FromRgb(0x7C, 0x5C, 0xFF), Color.FromRgb(0x22, 0xD3, 0xEE), Color.FromRgb(0x34, 0xD3, 0x99), Color.FromRgb(0xF4, 0x72, 0xB6),
        Color.FromRgb(0xFB, 0xBF, 0x24), Color.FromRgb(0x60, 0xA5, 0xFA), Color.FromRgb(0xF8, 0x71, 0x71), Color.FromRgb(0xA3, 0xE6, 0x35),
    ];

    private static Brush MakeAvatarBrush(string name)
    {
        int h = name.Aggregate(17, (a, c) => unchecked(a * 31 + c));
        var c1 = AvatarColors[Math.Abs(h % AvatarColors.Length)];
        var c2 = AvatarColors[Math.Abs((h / 7) % AvatarColors.Length)];
        if (c2 == c1) c2 = Color.FromRgb((byte)(c1.R * 0.55), (byte)(c1.G * 0.55), (byte)(c1.B * 0.55));
        var brush = new LinearGradientBrush(c1, c2, 45);
        brush.Freeze();
        return brush;
    }
}
