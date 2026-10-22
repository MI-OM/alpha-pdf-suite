using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Themes.Fluent;

namespace PdfSuite.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}

internal sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

// Spike shell (S0). Spike item S1 adds the tiled document view backed by the worker process.
internal sealed class MainWindow : Window
{
    public MainWindow()
    {
        Title = "PDF Suite (spike)";
        Width = 900;
        Height = 700;
        Content = new TextBlock
        {
            Text = "Spike shell. Document viewing is built in spike item S1.",
            Margin = new Thickness(24),
            TextWrapping = TextWrapping.Wrap,
        };
    }
}
