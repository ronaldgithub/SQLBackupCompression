using Avalonia;
using System;
using System.Globalization;

namespace SqlBackupBenchmark;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // European number formatting: 10.633,1 instead of 10,633.1
        var nl = CultureInfo.GetCultureInfo("nl-NL");
        CultureInfo.DefaultThreadCurrentCulture   = nl;
        CultureInfo.DefaultThreadCurrentUICulture = nl;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
