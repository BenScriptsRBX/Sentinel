using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sentinel.ViewModels;

namespace Sentinel;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly string? _snapshotDir;
    private readonly string? _snapshotSearch;

    public MainWindow(string? snapshotDir, string? snapshotSearch = null)
    {
        _snapshotDir = snapshotDir;
        _snapshotSearch = snapshotSearch;
        InitializeComponent();
        DataContext = _vm;
        Loaded += async (_, _) =>
        {
            var scan = _vm.ScanAsync();
            if (_snapshotDir is not null)
            {
                Directory.CreateDirectory(_snapshotDir);
                await Task.Delay(700); // the loading screen is only visible while scanning
                Capture("loading.png");
            }
            await scan;
            if (_snapshotDir is not null) await CaptureSnapshotsAsync();
        };
        StateChanged += (_, _) => Root.BorderThickness = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(1);
        SourceInitialized += (_, _) => ApplyWindowsChrome();
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Windows 11: rounded corners, dark frame and a border that matches the theme. Ignored on Windows 10.</summary>
    private void ApplyWindowsChrome()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        int dark = 1, round = 2 /* DWMWCP_ROUND */, border = 0x00382B25 /* COLORREF 0x00BBGGRR = #252B38 */;
        DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
        DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, sizeof(int));
        DwmSetWindowAttribute(hwnd, 34 /* DWMWA_BORDER_COLOR */, ref border, sizeof(int));
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximize(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>`--snapshot dir` renders every page to PNG and exits — used for visual checks without a human at the screen.</summary>
    private async Task CaptureSnapshotsAsync()
    {
        Directory.CreateDirectory(_snapshotDir!);
        foreach (var page in Enum.GetValues<Page>())
        {
            _vm.CurrentPage = page;
            if (page == Page.Catalog && _snapshotSearch is not null)
            {
                _vm.CatalogSearch = _snapshotSearch;
                _vm.SelectedCatalogGame = _vm.CatalogView.Cast<GameViewModel>().FirstOrDefault();
            }
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            await Task.Delay(page == Page.Catalog ? 5000 : 400); // let catalog art download
            Capture($"{(int)page}-{page}.png");

            // Second shot scrolled to the bottom, so below-the-fold layout gets checked too.
            if (FindScrollable(this) is { } scroller)
            {
                scroller.ScrollToEnd();
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                await Task.Delay(300);
                Capture($"{(int)page}-{page}-bottom.png");
                scroller.ScrollToHome();
            }
        }
        Close();
    }

    private void Capture(string file)
    {
        UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this);
        var bmp = new RenderTargetBitmap((int)(ActualWidth * dpi.DpiScaleX), (int)(ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(this);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(Path.Combine(_snapshotDir!, file));
        enc.Save(fs);
    }

    /// <summary>The visible page's main scroll area (the largest scrollable ScrollViewer on screen).</summary>
    private static System.Windows.Controls.ScrollViewer? FindScrollable(DependencyObject root)
    {
        System.Windows.Controls.ScrollViewer? best = null;
        void Walk(DependencyObject d)
        {
            if (d is UIElement { IsVisible: false }) return;
            if (d is System.Windows.Controls.ScrollViewer sv && sv.ScrollableHeight > 0 && (best is null || sv.ExtentHeight > best.ExtentHeight)) best = sv;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(root);
        return best;
    }
}
