using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SqlBackupBenchmark.Models;

namespace SqlBackupBenchmark.ViewModels;

public partial class BackupScenarioItemViewModel : ViewModelBase
{
    public BackupScenario Scenario { get; }

    [ObservableProperty] private bool _isChecked = true;
    [ObservableProperty] private BackupResultStatus _status = BackupResultStatus.Pending;
    [ObservableProperty] private string _durationText = "-";
    [ObservableProperty] private string _fileSizeMbText = "-";
    [ObservableProperty] private string _mbPerSecText = "-";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _lastSql = "";
    [ObservableProperty] private string _fileSizeRatioText = "";
    [ObservableProperty] private string _durationRatioText = "";

    // Raw values used by MainWindowViewModel.ComputeRatios()
    public double FileSizeMbValue  { get; private set; }
    public double DurationSeconds  { get; private set; }

    public BackupScenarioItemViewModel(BackupScenario scenario)
    {
        Scenario = scenario;
    }

    public string StatusText => Status switch
    {
        BackupResultStatus.Pending  => "Pending",
        BackupResultStatus.Running  => "Running...",
        BackupResultStatus.Done     => "Done",
        BackupResultStatus.Error    => "Error",
        _                           => ""
    };

    public IBrush StatusColor => Status switch
    {
        BackupResultStatus.Running  => new SolidColorBrush(Color.Parse("#4EC9B0")),
        BackupResultStatus.Done     => new SolidColorBrush(Color.Parse("#6A9955")),
        BackupResultStatus.Error    => new SolidColorBrush(Color.Parse("#F44747")),
        _                           => new SolidColorBrush(Color.Parse("#888888"))
    };

    public void ApplyResult(BackupResult result)
    {
        Status          = result.Status;
        FileSizeMbValue = result.FileSizeMB;
        DurationSeconds = result.Duration.TotalSeconds;
        LastSql         = result.SqlStatement;

        DurationText = result.Duration.TotalSeconds > 0
            ? $"{(int)result.Duration.TotalMinutes:D2}:{result.Duration.Seconds:D2}.{result.Duration.Milliseconds / 100}"
            : "-";
        FileSizeMbText = result.FileSizeMB > 0 ? $"{result.FileSizeMB:N1}" : "-";
        MbPerSecText   = result.MbPerSec   > 0 ? $"{result.MbPerSec:N1}"   : "-";
        ErrorMessage   = result.ErrorMessage;

        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusColor));
    }

    public IBrush FileSizeRatioColor => FileSizeRatioText == "100%"
        ? new SolidColorBrush(Color.Parse("#6A9955"))
        : new SolidColorBrush(Color.Parse("#AAAAAA"));

    public IBrush DurationRatioColor => DurationRatioText == "100%"
        ? new SolidColorBrush(Color.Parse("#6A9955"))
        : new SolidColorBrush(Color.Parse("#AAAAAA"));

    public void SetRatios(double bestSizeMb, double bestDurationSec)
    {
        FileSizeRatioText = FileSizeMbValue > 0
            ? $"{FileSizeMbValue / bestSizeMb * 100:N0}%"
            : "";
        DurationRatioText = DurationSeconds > 0
            ? $"{DurationSeconds / bestDurationSec * 100:N0}%"
            : "";
        OnPropertyChanged(nameof(FileSizeRatioColor));
        OnPropertyChanged(nameof(DurationRatioColor));
    }

    public void SetRunning()
    {
        Status            = BackupResultStatus.Running;
        DurationText      = FileSizeMbText = MbPerSecText = "-";
        FileSizeRatioText = DurationRatioText = "";
        ErrorMessage      = null;
        LastSql           = "";
        FileSizeMbValue   = 0;
        DurationSeconds   = 0;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusColor));
    }

    public void Reset()
    {
        Status            = BackupResultStatus.Pending;
        DurationText      = FileSizeMbText = MbPerSecText = "-";
        FileSizeRatioText = DurationRatioText = "";
        ErrorMessage      = null;
        LastSql           = "";
        FileSizeMbValue   = 0;
        DurationSeconds   = 0;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusColor));
    }
}
