using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Sentinel.Services.Tweaks;

namespace Sentinel.Views;

public sealed class BoolToVisibility : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        bool b = value switch { bool x => x, null => false, string s => s.Length > 0, int i => i != 0, _ => true };
        return b ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Visible when the bound value's string form equals the converter parameter.</summary>
public sealed class EqualsToVisibility : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        string.Equals(value?.ToString(), p?.ToString(), StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>TwoWay bridge for nav radio buttons: checked when value equals parameter.</summary>
public sealed class EqualsToBool : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => string.Equals(value?.ToString(), p?.ToString(), StringComparison.Ordinal);
    public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
        value is true && p is string s && Enum.TryParse(t, s, out var e) ? e! : Binding.DoNothing;
}

public static class Palette
{
    public static Brush Get(string key) => (Brush)Application.Current.Resources[key];
}

/// <summary>Quality level (0-3) → colour, from warm (Low) to accent (Ultra).</summary>
public sealed class LevelToBrush : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value switch
    {
        0 => new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71)),
        1 => new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)),
        2 => new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)),
        _ => new SolidColorBrush(Color.FromRgb(0xA7, 0x8B, 0xFA)),
    };
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Quality level (0-3) → bar width percentage.</summary>
public sealed class LevelToPercent : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is int l ? (l + 1) * 25.0 : 0.0;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class TweakStatusToBrush : IValueConverter
{
    public bool Soft { get; set; }
    public object Convert(object value, Type t, object p, CultureInfo c) => value switch
    {
        TweakStatus.Recommended => Palette.Get(Soft ? "AccentSoft" : "Accent"),
        TweakStatus.Optimal => Palette.Get(Soft ? "GoodSoft" : "Good"),
        TweakStatus.Warning => Palette.Get(Soft ? "WarnSoft" : "Warn"),
        _ => Palette.Get(Soft ? "InfoSoft" : "Accent2"),
    };
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class KindToBrush : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => (value as string) switch
    {
        "good" => Palette.Get("Good"),
        "off" => Palette.Get("Muted"),
        _ => Palette.Get("Accent2"),
    };
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class KindToIcon : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => (value as string) switch
    {
        "good" => "", // checkmark
        "off" => "",  // cancel
        _ => "",      // info
    };
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class ImpactToBrush : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => (value as string) switch
    {
        "High" => Palette.Get("Bad"),
        "Medium" => Palette.Get("Warn"),
        _ => Palette.Get("Muted"),
    };
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Score 0-100 → arc geometry for the dashboard gauge.</summary>
public sealed class ScoreToArc : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        double score = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
        double radius = 62, cx = 70, cy = 70;
        double sweep = Math.Clamp(score, 0.5, 99.9) / 100 * 270;
        double start = 135;
        Point Pt(double deg) => new(cx + radius * Math.Cos(deg * Math.PI / 180), cy + radius * Math.Sin(deg * Math.PI / 180));
        var fig = new PathFigure { StartPoint = Pt(start), IsClosed = false };
        fig.Segments.Add(new ArcSegment(Pt(start + sweep), new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, true));
        var g = new PathGeometry([fig]);
        g.Freeze();
        return g;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Tweak category → Segoe Fluent glyph for the card badge.</summary>
public sealed class CategoryToGlyph : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => (value as string) switch
    {
        "Power" => "",       // lightning
        "Windows" => "",     // PC
        "Display" => "",     // monitor
        "Input" => "",       // mouse
        "Graphics" => "",    // GPU / device
        "Memory" => "",      // RAM
        "Storage" => "",     // drive
        "Security" => "",    // shield
        "Background" => "",  // apps
        _ => "",
    };
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
