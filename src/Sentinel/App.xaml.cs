using System.Windows;
using System.Windows.Threading;
using Sentinel.Core;

namespace Sentinel;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        var args = e.Args;
        int report = Array.IndexOf(args, "--report");
        if (report >= 0)
        {
            var path = args.ElementAtOrDefault(report + 1) ?? "sentinel-report.json";
            try { Services.Report.Write(path); }
            catch (Exception ex) { Log.Error("report", ex); File.WriteAllText(path + ".error.txt", ex.ToString()); }
            Shutdown();
            return;
        }

        AppMode.Demo = args.Contains("--demo", StringComparer.OrdinalIgnoreCase);

        // --snapshot takes an output folder and an optional catalog search; another --switch after it isn't one of them.
        int snapshot = Array.IndexOf(args, "--snapshot");
        string? Value(int i) => args.ElementAtOrDefault(i) is { } v && !v.StartsWith("--") ? v : null;
        var window = new MainWindow(snapshot >= 0 ? Value(snapshot + 1) ?? "snapshots" : null,
                                    snapshot >= 0 ? Value(snapshot + 2) : null);
        MainWindow = window;
        window.Show();
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("ui", e.Exception);
        MessageBox.Show($"Something went wrong:\n\n{e.Exception.Message}\n\nDetails were written to {AppPaths.Log}", "Sentinel",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
