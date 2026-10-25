using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Sentinel.Views;

/// <summary>
/// One fps scale showing what matters for a plan: the requested target band, the expected range (conservative →
/// typical), the hardware ceiling, the display refresh and the game's own fps lock.
/// </summary>
public sealed class FpsRangeBar : FrameworkElement
{
    private static FrameworkPropertyMetadata Meta(double d = 0) => new(d, FrameworkPropertyMetadataOptions.AffectsRender);

    public static readonly DependencyProperty TargetMinProperty = DependencyProperty.Register(nameof(TargetMin), typeof(double), typeof(FpsRangeBar), Meta());
    public static readonly DependencyProperty TargetMaxProperty = DependencyProperty.Register(nameof(TargetMax), typeof(double), typeof(FpsRangeBar), Meta());
    public static readonly DependencyProperty EstimateProperty = DependencyProperty.Register(nameof(Estimate), typeof(double), typeof(FpsRangeBar), Meta());
    public static readonly DependencyProperty TypicalProperty = DependencyProperty.Register(nameof(Typical), typeof(double), typeof(FpsRangeBar), Meta());
    public static readonly DependencyProperty CeilingProperty = DependencyProperty.Register(nameof(Ceiling), typeof(double), typeof(FpsRangeBar), Meta());
    public static readonly DependencyProperty RefreshProperty = DependencyProperty.Register(nameof(Refresh), typeof(double), typeof(FpsRangeBar), Meta());
    public static readonly DependencyProperty EngineCapProperty = DependencyProperty.Register(nameof(EngineCap), typeof(double), typeof(FpsRangeBar), Meta());

    public double TargetMin { get => (double)GetValue(TargetMinProperty); set => SetValue(TargetMinProperty, value); }
    public double TargetMax { get => (double)GetValue(TargetMaxProperty); set => SetValue(TargetMaxProperty, value); }
    public double Estimate { get => (double)GetValue(EstimateProperty); set => SetValue(EstimateProperty, value); }
    public double Typical { get => (double)GetValue(TypicalProperty); set => SetValue(TypicalProperty, value); }
    public double Ceiling { get => (double)GetValue(CeilingProperty); set => SetValue(CeilingProperty, value); }
    public double Refresh { get => (double)GetValue(RefreshProperty); set => SetValue(RefreshProperty, value); }
    public double EngineCap { get => (double)GetValue(EngineCapProperty); set => SetValue(EngineCapProperty, value); }

    private static readonly Brush Track = Freeze(new SolidColorBrush(Color.FromRgb(0x23, 0x29, 0x37)));
    private static readonly Brush BandFill = Freeze(new SolidColorBrush(Color.FromArgb(0x38, 0x7C, 0x5C, 0xFF)));
    private static readonly Pen BandEdge = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(0xB0, 0x9B, 0x7C, 0xFF))), 1));
    private static readonly Brush RangeFill = Freeze(new LinearGradientBrush(Color.FromRgb(0x8B, 0x5C, 0xF6), Color.FromRgb(0x22, 0xD3, 0xEE), 0));
    private static readonly Brush Label = Freeze(new SolidColorBrush(Color.FromRgb(0x8B, 0x93, 0xA7)));
    private static readonly Brush Bright = Freeze(new SolidColorBrush(Color.FromRgb(0xE9, 0xEC, 0xF2)));
    private static readonly Pen CeilingPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24))), 1.5) { DashStyle = Freeze(new DashStyle([2, 2], 0)) });
    private static readonly Pen RefreshPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(0xC0, 0x22, 0xD3, 0xEE))), 1.5));
    private static readonly Pen LockPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71))), 1.5));
    private static readonly Typeface Face = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    protected override Size MeasureOverride(Size available) => new(double.IsInfinity(available.Width) ? 300 : available.Width, 66);

    // Layout rows: value label (0–15) · bar with target band (18–38) · marker labels (40–52, overflow row 53–65).
    private const double BarY = 22, BarH = 10, LabelRow = 41, LabelRow2 = 53;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        if (w < 40) return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Scale: comfortably past every marker, rounded to a tidy number.
        double top = new[] { TargetMax, Typical, Ceiling, Refresh, EngineCap, 60 }.Max() * 1.12;
        double step = top switch { <= 150 => 25, <= 400 => 50, <= 800 => 100, _ => 250 };
        top = Math.Ceiling(top / step) * step;
        double X(double fps) => Math.Clamp(fps / top, 0, 1) * (w - 12) + 6;

        dc.DrawRoundedRectangle(Track, null, new Rect(0, BarY, w, BarH), 5, 5);

        if (TargetMax > 0)
        {
            var band = new Rect(X(TargetMin), BarY - 5, Math.Max(X(TargetMax) - X(TargetMin), 3), BarH + 10);
            dc.DrawRoundedRectangle(BandFill, BandEdge, band, 4, 4);
        }

        // Expected range: conservative → typical, with a bright marker on the conservative value.
        if (Estimate > 0)
        {
            double a = X(Estimate), b = Math.Max(X(Typical), a + 4);
            dc.DrawRoundedRectangle(RangeFill, null, new Rect(a, BarY + 2, b - a, BarH - 4), 3, 3);
            dc.DrawEllipse(Bright, new Pen(RangeFill, 2.5), new Point(a, BarY + BarH / 2), 5, 5);
            Text(dc, $"{Estimate:0} fps", a, 0, Bright, 11.5, dpi, w);
        }

        // Reference markers below the bar; a label that would collide drops to a second row.
        var placed = new List<Rect>();
        var markers = new List<(double X, Pen Pen, string Label)>();
        if (Refresh > 0) markers.Add((X(Refresh), RefreshPen, $"{Refresh:0} Hz display"));
        if (EngineCap > 0) markers.Add((X(EngineCap), LockPen, $"game lock {EngineCap:0}"));
        else if (Ceiling > 0) markers.Add((X(Ceiling), CeilingPen, Ceiling >= 1000 ? "max 1000+" : $"max {Ceiling:0}"));
        markers.Add((X(0), null!, "0"));
        markers.Add((X(top), null!, top.ToString("0", CultureInfo.InvariantCulture)));

        foreach (var (x, pen, label) in markers)
        {
            if (pen is not null) dc.DrawLine(pen, new Point(x, BarY - 7), new Point(x, BarY + BarH + 7));
            var ft = Format(label, pen?.Brush ?? Label, 10, dpi, bold: pen is not null);
            double left = Math.Clamp(x - ft.Width / 2, 0, w - ft.Width);
            var rect = new Rect(left - 4, LabelRow, ft.Width + 8, 12);
            if (placed.Any(r => r.IntersectsWith(rect)))
            {
                if (pen is null) continue; // axis numbers simply yield to real markers
                rect.Y = LabelRow2;
            }
            placed.Add(rect);
            dc.DrawText(ft, new Point(left, rect.Y));
        }
    }

    private static FormattedText Format(string s, Brush brush, double size, double dpi, bool bold)
    {
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush, dpi);
        if (!bold) ft.SetFontWeight(FontWeights.Normal);
        return ft;
    }

    private static void Text(DrawingContext dc, string s, double x, double y, Brush brush, double size, double dpi, double width)
    {
        var ft = Format(s, brush, size, dpi, bold: true);
        dc.DrawText(ft, new Point(Math.Clamp(x - ft.Width / 2, 0, width - ft.Width), y));
    }
}
