using Avalonia.Controls;
using Avalonia.Interactivity;
using SqlBackupBenchmark.ViewModels;

namespace SqlBackupBenchmark.Views;

public partial class ConnectionDialog : Window
{
    public ConnectionDialog() => InitializeComponent();

    public ConnectionDialog(ConnectionDialogViewModel vm) : this()
    {
        DataContext = vm;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
