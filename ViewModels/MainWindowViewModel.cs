using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SqlBackupBenchmark.Models;
using SqlBackupBenchmark.Services;

namespace SqlBackupBenchmark.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public ConnectionSettings ConnectionSettings { get; } = new();

    private readonly SqlBackupService _service;
    private readonly SqlAnalysisService _analysisService;

    public RestoreTabViewModel RestoreTab { get; }

    [ObservableProperty] private ObservableCollection<DatabaseInfo> _availableDatabases = [];
    [ObservableProperty] private DatabaseInfo? _selectedDatabase;
    [ObservableProperty] private string _backupPath = @"D:\backups";
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private string _connectionSummary = "";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isParallelRun;
    [ObservableProperty] private int _stripeCount = 1;
    [ObservableProperty] private bool _isFeedbackRunning;
    [ObservableProperty] private string _totalDurationText = "-";
    [ObservableProperty] private ObservableCollection<TableAnalysisRow> _analysisResults = [];

    public string CpuVendor => CpuInfo.VendorName;

    public bool CanAskForFeedback =>
        !IsRunning && !RestoreTab.IsRunning && !IsFeedbackRunning && SelectedDatabase is not null;

    public bool IsSerialRun
    {
        get => !IsParallelRun;
        set => IsParallelRun = !value;
    }

    public string RunButtonText => IsParallelRun ? "▶  Run in Parallel" : "▶  Run Serially";

    // Radio-group options for StripeCount — each setter only reacts to being switched ON,
    // since the RadioButton being switched OFF fires with value:false and the newly-selected
    // one's setter is what actually changes StripeCount.
    public bool IsStripe1 { get => StripeCount == 1; set { if (value) StripeCount = 1; } }
    public bool IsStripe2 { get => StripeCount == 2; set { if (value) StripeCount = 2; } }
    public bool IsStripe4 { get => StripeCount == 4; set { if (value) StripeCount = 4; } }
    public bool IsStripe8 { get => StripeCount == 8; set { if (value) StripeCount = 8; } }

    public ObservableCollection<BackupScenarioItemViewModel> Scenarios { get; } = [];

    private CancellationTokenSource? _cts;

    public MainWindowViewModel()
    {
        _service = new SqlBackupService(ConnectionSettings);
        _analysisService = new SqlAnalysisService(ConnectionSettings);
        RestoreTab = new RestoreTabViewModel(ConnectionSettings);
        ConnectionSummary = ConnectionSettings.Summary;

        RestoreTab.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RestoreTabViewModel.IsRunning))
                OnPropertyChanged(nameof(CanAskForFeedback));
        };

        foreach (var s in BackupScenario.AllScenarios())
            Scenarios.Add(new BackupScenarioItemViewModel(s));

        _ = LoadDatabasesAsync();
    }

    partial void OnSelectedDatabaseChanged(DatabaseInfo? value)
    {
        RunCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanAskForFeedback));
    }

    partial void OnIsRunningChanged(bool value)
    {
        RunCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanAskForFeedback));
    }

    partial void OnIsFeedbackRunningChanged(bool value)
        => OnPropertyChanged(nameof(CanAskForFeedback));

    partial void OnIsParallelRunChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSerialRun));
        OnPropertyChanged(nameof(RunButtonText));
    }

    partial void OnStripeCountChanged(int value)
    {
        OnPropertyChanged(nameof(IsStripe1));
        OnPropertyChanged(nameof(IsStripe2));
        OnPropertyChanged(nameof(IsStripe4));
        OnPropertyChanged(nameof(IsStripe8));
    }

    private async Task LoadDatabasesAsync()
    {
        try
        {
            StatusMessage = "Connecting to localhost...";
            var dbs = await _service.GetDatabasesAsync();
            AvailableDatabases = new ObservableCollection<DatabaseInfo>(dbs);
            SelectedDatabase = dbs.Count > 0 ? dbs[0] : null;
            StatusMessage = dbs.Count > 0
                ? $"Connected — {dbs.Count} databases found"
                : "Connected — no user databases found";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var s in Scenarios.Where(s => !s.IsQatUnsupported)) s.IsChecked = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var s in Scenarios) s.IsChecked = false;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task Run()
    {
        var db = SelectedDatabase?.Name;
        if (string.IsNullOrWhiteSpace(db)) return;
        var databaseSizeMb = SelectedDatabase!.SizeMB;

        var selected = Scenarios.Where(s => s.IsChecked && !s.IsQatUnsupported).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "No scenarios selected.";
            return;
        }

        _cts = new CancellationTokenSource();
        IsRunning = true;
        TotalDurationText = "-";
        var mode = IsParallelRun ? "parallel" : "serial";
        StatusMessage = $"Running {selected.Count} scenario(s) {mode}...";

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        if (IsParallelRun)
        {
            foreach (var s in selected) s.SetRunning();
            var tasks = selected.Select(vm => RunScenarioAsync(vm, db, databaseSizeMb, timestamp, _cts.Token));
            await Task.WhenAll(tasks);
        }
        else
        {
            foreach (var vm in selected)
            {
                if (_cts.Token.IsCancellationRequested) break;
                vm.SetRunning();
                StatusMessage = $"[{vm.Scenario.Name}] running...";
                var result = await _service.RunBackupAsync(vm.Scenario, database: db, backupPath: BackupPath,
                    timestamp: timestamp, stripeCount: StripeCount, ct: _cts.Token);
                vm.ApplyResult(result, databaseSizeMb);
            }
        }

        ComputeRatios();

        var done  = selected.Count(s => s.Status == BackupResultStatus.Done);
        var error = selected.Count(s => s.Status == BackupResultStatus.Error);
        TotalDurationText = FormatDuration(TimeSpan.FromSeconds(selected.Sum(s => s.DurationSeconds)));
        StatusMessage = $"Completed ({mode}): {done} succeeded, {error} failed.";
        IsRunning = false;
    }

    private static string FormatDuration(TimeSpan ts) =>
        ts.TotalSeconds > 0
            ? $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 100}"
            : "-";

    private void ComputeRatios()
    {
        var done = Scenarios.Where(s => s.Status == BackupResultStatus.Done).ToList();
        if (done.Count == 0) return;
        double bestSize = done.Min(s => s.FileSizeMbValue);
        double bestDur  = done.Min(s => s.DurationSeconds);
        foreach (var s in done) s.SetRatios(bestSize, bestDur);
    }

    private bool CanRun() => !IsRunning && SelectedDatabase is not null;

    private async Task RunScenarioAsync(
        BackupScenarioItemViewModel vm,
        string database,
        double databaseSizeMb,
        string timestamp,
        CancellationToken ct)
    {
        var result = await _service.RunBackupAsync(vm.Scenario, database, BackupPath, timestamp, StripeCount, ct: ct);
        Dispatcher.UIThread.Post(() => vm.ApplyResult(result, databaseSizeMb));
    }

    [RelayCommand]
    private async Task Reload() => await LoadDatabasesAsync();

    public void SetBackupPathFromDialog(string path) => BackupPath = path;

    public async Task<bool> RunTableAnalysisAsync()
    {
        if (SelectedDatabase is null) return false;
        var db = SelectedDatabase.Name;
        StatusMessage = $"Analysing {db}...";
        try
        {
            var rows = await _analysisService.GetTableAnalysisAsync(db);
            AnalysisResults = new ObservableCollection<TableAnalysisRow>(rows);
            StatusMessage = $"Analysis complete — {rows.Count} table(s) in {db}.";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Analysis failed: {ex.Message}";
            return false;
        }
    }

    public async Task ApplyConnectionSettingsAsync(ConnectionSettings newSettings)
    {
        ConnectionSettings.CopyFrom(newSettings);
        ConnectionSummary = ConnectionSettings.Summary;
        await LoadDatabasesAsync();
        await RestoreTab.RefreshConnectionAsync();
    }

    /// <summary>
    /// Runs every backup scenario, then every restore scenario against the files just
    /// created, and writes a full text report of both runs. Restores the user's prior
    /// checkbox selections and restore-tab filter/path on the way out. Returns the saved
    /// report's full path, or null if it couldn't run or the write failed.
    /// </summary>
    public async Task<string?> RunFeedbackReportAsync()
    {
        if (!CanAskForFeedback || SelectedDatabase is null) return null;
        var db = SelectedDatabase;

        IsFeedbackRunning = true;

        var backupChecked = Scenarios.ToDictionary(s => s, s => s.IsChecked);
        var restoreChecked = RestoreTab.Scenarios.ToDictionary(s => s, s => s.IsChecked);
        var priorRestorePath = RestoreTab.BackupPath;
        var priorRestoreFilter = RestoreTab.SelectedDatabaseFilter;

        try
        {
            var timestamp = DateTime.Now;

            StatusMessage = "Ask for Feedback: running all backup scenarios...";
            SelectAllCommand.Execute(null);
            await RunCommand.ExecuteAsync(null);

            RestoreTab.StatusMessage = "Ask for Feedback: scanning for new backup files...";
            RestoreTab.BackupPath = BackupPath;
            RestoreTab.SelectedDatabaseFilter = db.Name;
            RestoreTab.RescanFiles();

            RestoreTab.StatusMessage = "Ask for Feedback: running all restore scenarios...";
            RestoreTab.SelectAllCommand.Execute(null);
            await RestoreTab.RunCommand.ExecuteAsync(null);

            var report = FeedbackReportService.Build(
                timestamp, db.Name, db.SizeMB, BackupPath, StripeCount,
                Scenarios.Select(ToReportRow).ToList(),
                RestoreTab.Scenarios.Select(ToReportRow).ToList());

            var fileName = $"{db.Name}_FeedbackReport_{timestamp:yyyyMMdd_HHmmss}.txt";
            var filePath = Path.Combine(BackupPath, fileName);
            await File.WriteAllTextAsync(filePath, report);

            StatusMessage = $"Feedback report saved: {fileName}";
            return filePath;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Feedback report failed: {ex.Message}";
            return null;
        }
        finally
        {
            foreach (var (s, wasChecked) in backupChecked) s.IsChecked = wasChecked;
            foreach (var (s, wasChecked) in restoreChecked) s.IsChecked = wasChecked;
            RestoreTab.BackupPath = priorRestorePath;
            RestoreTab.SelectedDatabaseFilter = priorRestoreFilter;
            RestoreTab.RescanFiles();
            IsFeedbackRunning = false;
        }
    }

    private static ScenarioReportRow ToReportRow(BackupScenarioItemViewModel s) => new(
        s.Scenario.Name, s.StatusText, s.DurationText, s.FileSizeMbText, s.MbPerSecText,
        s.CompressionRatioText, s.LastSql, s.ErrorMessage);

    private static ScenarioReportRow ToReportRow(RestoreScenarioItemViewModel s) => new(
        s.Scenario.Name, s.StatusText, s.DurationText, s.FileSizeMbText, s.MbPerSecText,
        s.FileSizeRatioText, s.LastSql, s.ErrorMessage);
}
