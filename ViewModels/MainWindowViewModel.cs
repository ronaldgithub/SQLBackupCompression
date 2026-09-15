using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    public RestoreTabViewModel RestoreTab { get; }

    [ObservableProperty] private ObservableCollection<string> _availableDatabases = [];
    [ObservableProperty] private string? _selectedDatabase;
    [ObservableProperty] private string _backupPath = @"D:\backups";
    [ObservableProperty] private string _sqlPreview = "";
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private string _connectionSummary = "";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isParallelRun = true;
    [ObservableProperty] private BackupScenarioItemViewModel? _selectedScenario;

    public bool IsSerialRun
    {
        get => !IsParallelRun;
        set => IsParallelRun = !value;
    }

    public string RunButtonText => IsParallelRun ? "▶  Run in Parallel" : "▶  Run Serially";

    public ObservableCollection<BackupScenarioItemViewModel> Scenarios { get; } = [];

    private CancellationTokenSource? _cts;

    public MainWindowViewModel()
    {
        _service = new SqlBackupService(ConnectionSettings);
        RestoreTab = new RestoreTabViewModel(ConnectionSettings);
        ConnectionSummary = ConnectionSettings.Summary;

        foreach (var s in BackupScenario.AllScenarios())
            Scenarios.Add(new BackupScenarioItemViewModel(s));

        _ = LoadDatabasesAsync();
    }

    partial void OnSelectedScenarioChanged(BackupScenarioItemViewModel? value)
        => UpdateSqlPreview(value);

    partial void OnSelectedDatabaseChanged(string? value)
    {
        UpdateSqlPreview(SelectedScenario);
        RunCommand.NotifyCanExecuteChanged();
    }

    partial void OnBackupPathChanged(string value)
        => UpdateSqlPreview(SelectedScenario);

    partial void OnIsRunningChanged(bool value)
        => RunCommand.NotifyCanExecuteChanged();

    partial void OnIsParallelRunChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSerialRun));
        OnPropertyChanged(nameof(RunButtonText));
    }

    private void UpdateSqlPreview(BackupScenarioItemViewModel? scenarioVm)
    {
        if (scenarioVm is null || string.IsNullOrWhiteSpace(SelectedDatabase))
        {
            SqlPreview = "";
            return;
        }
        var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        SqlPreview = scenarioVm.Scenario.GenerateSql(SelectedDatabase, BackupPath, ts);
    }

    private async Task LoadDatabasesAsync()
    {
        try
        {
            StatusMessage = "Connecting to localhost...";
            var dbs = await _service.GetDatabasesAsync();
            AvailableDatabases = new ObservableCollection<string>(dbs);
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
        foreach (var s in Scenarios) s.IsChecked = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var s in Scenarios) s.IsChecked = false;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task Run()
    {
        var db = SelectedDatabase;
        if (string.IsNullOrWhiteSpace(db)) return;

        var selected = Scenarios.Where(s => s.IsChecked).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "No scenarios selected.";
            return;
        }

        _cts = new CancellationTokenSource();
        IsRunning = true;
        var mode = IsParallelRun ? "parallel" : "serial";
        StatusMessage = $"Running {selected.Count} scenario(s) {mode}...";

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        if (IsParallelRun)
        {
            foreach (var s in selected) s.SetRunning();
            var tasks = selected.Select(vm => RunScenarioAsync(vm, db, timestamp, _cts.Token));
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
                    timestamp: timestamp, ct: _cts.Token);
                vm.ApplyResult(result);
            }
        }

        ComputeRatios();

        var done  = selected.Count(s => s.Status == BackupResultStatus.Done);
        var error = selected.Count(s => s.Status == BackupResultStatus.Error);
        StatusMessage = $"Completed ({mode}): {done} succeeded, {error} failed.";
        IsRunning = false;
    }

    private void ComputeRatios()
    {
        var done = Scenarios.Where(s => s.Status == BackupResultStatus.Done).ToList();
        if (done.Count == 0) return;
        double bestSize = done.Min(s => s.FileSizeMbValue);
        double bestDur  = done.Min(s => s.DurationSeconds);
        foreach (var s in done) s.SetRatios(bestSize, bestDur);
    }

    private bool CanRun() => !IsRunning && !string.IsNullOrWhiteSpace(SelectedDatabase);

    private async Task RunScenarioAsync(
        BackupScenarioItemViewModel vm,
        string database,
        string timestamp,
        CancellationToken ct)
    {
        var result = await _service.RunBackupAsync(vm.Scenario, database, BackupPath, timestamp, ct: ct);
        Dispatcher.UIThread.Post(() => vm.ApplyResult(result));
    }

    [RelayCommand]
    private async Task Reload() => await LoadDatabasesAsync();

    public void SetBackupPathFromDialog(string path) => BackupPath = path;

    public async Task ApplyConnectionSettingsAsync(ConnectionSettings newSettings)
    {
        ConnectionSettings.CopyFrom(newSettings);
        ConnectionSummary = ConnectionSettings.Summary;
        await LoadDatabasesAsync();
        await RestoreTab.RefreshConnectionAsync();
    }
}
