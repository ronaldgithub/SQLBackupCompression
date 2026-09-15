using System;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SqlBackupBenchmark.Views;

public partial class AboutWindow : Window
{
    private const string ContactEmail = "ronald.de.groot@opendata.nl";

    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {Version}";
    }

    private static string Version =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion?.Split('+')[0]
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "0.0.0";

    private static string Diagnostics =>
        $"SQL Server 2025 Backup/Restore Benchmark {Version}\n" +
        $"Microsoft.Data.SqlClient: {typeof(Microsoft.Data.SqlClient.SqlConnection).Assembly.GetName().Version}\n" +
        $"Runtime: {Environment.Version} · OS: {Environment.OSVersion}";

    private async void OnCopyDiagnosticsClick(object? sender, RoutedEventArgs e)
    {
        var clipboard = GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(Diagnostics);
    }

    private async void OnContactClick(object? sender, RoutedEventArgs e)
    {
        var launcher = GetTopLevel(this)?.Launcher;
        if (launcher is null) return;

        const string subject = "SQL Server 2025 Backup/Restore Benchmark - help";
        var body =
            "Describe what you need help with:\n\n\n" +
            "----- version / environment (leave this in) -----\n" +
            Diagnostics + "\n";

        var mailto = $"mailto:{ContactEmail}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
        try
        {
            await launcher.LaunchUriAsync(new Uri(mailto));
        }
        catch
        {
            // no mail client configured — nothing sensible to do
        }
    }

    private async void OnLinkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url }) return;
        var launcher = GetTopLevel(this)?.Launcher;
        if (launcher is null) return;

        try
        {
            await launcher.LaunchUriAsync(new Uri(url));
        }
        catch
        {
            // no handler registered for this link — nothing sensible to do
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
