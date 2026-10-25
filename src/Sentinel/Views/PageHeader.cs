using System.Windows;
using System.Windows.Controls;

namespace Sentinel.Views;

/// <summary>Page title block (glyph badge, title, subtitle). Its Content is shown on the right, for page actions.</summary>
public class PageHeader : ContentControl
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(PageHeader));
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(PageHeader));
    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(PageHeader));

    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Subtitle { get => (string?)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
}
