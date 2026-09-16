using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SqlBackupBenchmark.Services;
using SqlBackupBenchmark.ViewModels;

namespace SqlBackupBenchmark.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private async void OnBrowseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var path = await PickFolderAsync("Select backup folder");
        if (path is not null && DataContext is MainWindowViewModel vm)
            vm.SetBackupPathFromDialog(path);
    }

    private async void OnRestoreBrowseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var path = await PickFolderAsync("Select backup folder");
        if (path is not null && DataContext is MainWindowViewModel vm)
            vm.RestoreTab.SetBackupPathFromDialog(path);
    }

    private async void OnAboutClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => await new AboutWindow().ShowDialog(this);

    private async void OnInfoClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => await new InfoWindow().ShowDialog(this);

    private async void OnConnectionClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var dialogVm = new ConnectionDialogViewModel(vm.ConnectionSettings);
        var dialog = new ConnectionDialog(dialogVm);
        var accepted = await dialog.ShowDialog<bool>(this);
        if (accepted)
            await vm.ApplyConnectionSettingsAsync(dialogVm.ToSettings());
    }

    private async void OnAnalyzeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.SelectedDatabase is null) return;

        var databaseName = vm.SelectedDatabase.Name;
        var ok = await vm.RunTableAnalysisAsync();
        if (ok)
            await new AnalysisWindow(new AnalysisWindowViewModel(databaseName, vm.AnalysisResults)).ShowDialog(this);
    }

    private async void OnAskForFeedbackClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var logPath = await vm.RunFeedbackReportAsync();
        if (logPath is null) return;

        var launcher = GetTopLevel(this)?.Launcher;
        if (launcher is null) return;

        const string subject = "SQL Server 2025 Backup/Restore Benchmark - feedback";
        var body =
            $"Feedback report saved at:\n{logPath}\n\n" +
            "Please attach this file before sending — mailto links can't attach files automatically.\n\n" +
            "Notes:\n\n";

        var mailto = $"mailto:{AppContact.Email}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
        try
        {
            await launcher.LaunchUriAsync(new Uri(mailto));
        }
        catch
        {
            // no mail client configured — nothing sensible to do
        }

        var folder = Path.GetDirectoryName(logPath);
        if (folder is not null)
        {
            try
            {
                await launcher.LaunchUriAsync(new Uri(folder));
            }
            catch
            {
                // nothing sensible to do
            }
        }
    }

    private async System.Threading.Tasks.Task<string?> PickFolderAsync(string title)
    {
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }
}
