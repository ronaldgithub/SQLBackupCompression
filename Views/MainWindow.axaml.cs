using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
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
