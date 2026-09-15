using Avalonia.Controls;
using Avalonia.Interactivity;
using SqlBackupBenchmark.ViewModels;

namespace SqlBackupBenchmark.Views;

public partial class AnalysisWindow : Window
{
    public AnalysisWindow() => InitializeComponent();

    public AnalysisWindow(AnalysisWindowViewModel vm) : this()
    {
        DataContext = vm;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
