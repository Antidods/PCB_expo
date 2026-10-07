using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;

namespace PcbExpo.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--version"])
        {
            Console.WriteLine($"PCB Expo {AppVersion.Current}");
            return 0;
        }
        if (args.Length > 0 && args[0] is "--inspect" or "--check-runtime")
        {
            try
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                if (args[0] == "--check-runtime") DiagnosticCli.CheckRuntime();
                else DiagnosticCli.Run(args.Skip(1).ToArray());
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<PcbExpoApplication>()
        .UsePlatformDetect().LogToTrace();
}

public sealed class PcbExpoApplication : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Light;
        UiTheme.Apply(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
