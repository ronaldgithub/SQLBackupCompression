using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SqlBackupBenchmark.Views;

public partial class InfoWindow : Window
{
    public InfoWindow() => InitializeComponent();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
