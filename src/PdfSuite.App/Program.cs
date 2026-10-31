using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace PdfSuite.App;

/// <summary>
/// Command line: PdfSuite.App [file.pdf] [--benchmark] [--software] [--tile 256|512]
/// [--kill-worker] [--out results.json]. --benchmark runs the S1 measurements on the file and exits.
/// </summary>
internal sealed record AppOptions(string? File, bool Benchmark, bool Software, int TileSize, bool KillWorker, string? OutputPath)
{
    public static AppOptions Parse(string[] args)
    {
        string? file = null, output = null;
        bool benchmark = false, software = false, killWorker = false;
        int tileSize = 256;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--benchmark": benchmark = true; break;
                case "--software": software = true; break;
                case "--kill-worker": killWorker = true; break;
                case "--tile" when i + 1 < args.Length: tileSize = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--out" when i + 1 < args.Length: output = args[++i]; break;
                default: file = args[i]; break;
            }
        }

        return new AppOptions(file, benchmark, software, tileSize, killWorker, output);
    }
}

internal static class Program
{
    public static AppOptions Options { get; private set; } = AppOptions.Parse([]);

    [STAThread]
    public static void Main(string[] args)
    {
        Options = AppOptions.Parse(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        AppBuilder builder = AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
        if (Options.Software)
        {
            builder = builder
                .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
                .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Software] })
                .With(new AvaloniaNativePlatformOptions { RenderingMode = [AvaloniaNativeRenderingMode.Software] });
        }

        return builder;
    }
}

internal sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(Program.Options);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
