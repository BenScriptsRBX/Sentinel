using Sentinel.Core;

namespace Sentinel.Services.Tweaks;

public enum TweakStatus { Recommended, Optimal, Info, Warning }

public sealed class Tweak : ObservableObject
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    /// <summary>Expected gaming impact: High, Medium, Low.</summary>
    public string Impact { get; init; } = "Medium";
    public bool RequiresAdmin { get; init; }
    public bool RequiresReboot { get; init; }
    public string ActionLabel { get; init; } = "Apply";
    public Func<Task<string>>? ApplyAction { get; init; }
    public Func<Task<string>>? RevertAction { get; init; }

    private TweakStatus _status;
    public TweakStatus Status { get => _status; set { if (Set(ref _status, value)) OnPropertyChanged(nameof(StatusLabel)); } }

    private string? _result;
    public string? Result { get => _result; set => Set(ref _result, value); }

    private bool _canRevert;
    public bool CanRevert { get => _canRevert; set => Set(ref _canRevert, value); }

    public bool CanApply => ApplyAction is not null && Status is TweakStatus.Recommended or TweakStatus.Warning or TweakStatus.Info;
    public bool IsSafeAuto => ApplyAction is not null && Status == TweakStatus.Recommended && ActionLabel == "Apply";

    public string StatusLabel => Status switch
    {
        TweakStatus.Recommended => "Recommended",
        TweakStatus.Optimal => "Optimal",
        TweakStatus.Warning => "Attention",
        _ => "Tip"
    };

    public void Refresh() => OnPropertyChanged(nameof(CanApply));
}
