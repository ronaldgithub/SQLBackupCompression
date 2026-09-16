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

public partial class RestoreTabViewModel : ViewModelBase
{
    private const double DiskSpaceWarningPercent = 20;

    private readonly SqlRestoreService _service;
    private readonly PerformanceSamplingService _perfSampler;
    private readonly List<PerformanceSample> _restoreSamples = [];

    [ObservableProperty] private string _backupPath = @"D:\backups";
    [ObservableProperty] private ObservableCollection<string> _availableDatabases = [];
    [ObservableProperty] private string? _selectedDatabaseFilter;
    [ObservableProperty] private bool _overwriteExisting = true;
    [ObservableProperty] private bool _deleteFilesAfterRestore = true;
    [ObservableProperty] private bool _dropDatabaseAfterRestore = true;
    [ObservableProperty] private string _statusMessage = "Connecting...";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _totalDurationText = "-";

    public ObservableCollection<RestoreScenarioItemViewModel> Scenarios { get; } = [];

    public IReadOnlyList<PerformanceSample> RestorePerformanceSamples => _restoreSamples;

    private CancellationTokenSource? _cts;

    public RestoreTabViewModel(ConnectionSettings connectionSettings)
    {
        _service = new SqlRestoreService(connectionSettings);
        _perfSampler = new PerformanceSamplingService(connectionSettings);
        _perfSampler.SampleReceived += s => _restoreSamples.Add(s);
        _perfSampler.SamplingError += ex => Dispatcher.UIThread.Post(() =>
            StatusMessage = $"[perf sampling] {ex.GetType().Name}: {ex.Message}");

        foreach (var s in BackupScenario.AllScenarios())
        {
            var vm = new RestoreScenarioItemViewModel(s);
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(RestoreScenarioItemViewModel.IsChecked)
                                   or nameof(RestoreScenarioItemViewModel.FileFound))
                    RunCommand.NotifyCanExecuteChanged();
            };
            Scenarios.Add(vm);
        }

        _ = LoadDatabasesAsync();
    }

    partial void OnBackupPathChanged(string value)           { ScanForFiles(); RunCommand.NotifyCanExecuteChanged(); }
    partial void OnSelectedDatabaseFilterChanged(string? value) => ScanForFiles();
    partial void OnIsRunningChanged(bool value)              => RunCommand.NotifyCanExecuteChanged();

    private async Task LoadDatabasesAsync()
    {
        try
        {
            StatusMessage = "Connecting to localhost...";
            var dbs = await _service.GetDatabasesAsync();
            AvailableDatabases = new ObservableCollection<string>(dbs);
            StatusMessage = dbs.Count > 0
                ? $"Connected — {dbs.Count} databases found."
                : "Connected — no user databases found.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection failed: {ex.Message}";
        }

        ScanForFiles();
    }

    private void ScanForFiles()
    {
        if (!Directory.Exists(BackupPath))
        {
            foreach (var vm in Scenarios) vm.SetFiles([]);
            StatusMessage = "Backup path does not exist.";
            return;
        }

        var allBakFiles = Directory
            .EnumerateFiles(BackupPath, "*.bak")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(f => SelectedDatabaseFilter is null ||
                        f.StartsWith(SelectedDatabaseFilter + "_", StringComparison.OrdinalIgnoreCase))
            .ToList();

        int totalFound = 0;
        foreach (var vm in Scenarios)
        {
            var marker = $"_{vm.Scenario.Name}_";
            var files = allBakFiles.Where(f => f.Contains(marker, StringComparison.OrdinalIgnoreCase));
            var groups = RestoreFileGroup.GroupFrom(files)
                .OrderByDescending(g => g.DisplayName, StringComparer.OrdinalIgnoreCase);
            vm.SetFiles(groups);
            if (vm.FileFound) totalFound++;
        }

        var filterNote = SelectedDatabaseFilter is not null ? $" for '{SelectedDatabaseFilter}'" : "";
        StatusMessage = totalFound > 0
            ? $"Found backup files{filterNote} for {totalFound} of {Scenarios.Count} scenarios."
            : $"No matching backup files found{filterNote}.";
    }

    [RelayCommand]
    private void SelectAll()  { foreach (var s in Scenarios) s.IsChecked = true; }

    [RelayCommand]
    private void SelectNone() { foreach (var s in Scenarios) s.IsChecked = false; }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task Run()
    {
        var selected = Scenarios.Where(s => s.IsChecked && s.FileFound).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "No scenarios selected with a matching backup file.";
            return;
        }

        _cts = new CancellationTokenSource();
        IsRunning = true;
        TotalDurationText = "-";
        _restoreSamples.Clear();
        StatusMessage = $"Restoring {selected.Count} scenario(s) serially...";

        await _perfSampler.StartAsync(database: null, _cts.Token);
        try
        {
            try
            {
                var (dataPath, logPath) = await _service.GetDefaultPathsAsync(_cts.Token);

                if (DiskSpaceChecker.GetFreeSpacePercent(dataPath) < DiskSpaceWarningPercent)
                {
                    StatusMessage = $"Restore target path has less than {DiskSpaceWarningPercent:N0}% free disk space — run cancelled.";
                    IsRunning = false;
                    return;
                }

                foreach (var vm in selected)
                {
                    if (_cts.Token.IsCancellationRequested) break;

                    if (DiskSpaceChecker.GetFreeSpacePercent(dataPath) < DiskSpaceWarningPercent)
                    {
                        StatusMessage = $"Stopped before [{vm.Scenario.Name}]: restore target path dropped below {DiskSpaceWarningPercent:N0}% free disk space.";
                        break;
                    }

                    vm.SetRunning(DateTime.Now);
                    var group = vm.SelectedFile!;
                    var targetDb = group.RestoreDbName;
                    StatusMessage = $"[{vm.Scenario.Name}] → {targetDb}...";

                    _perfSampler.SetDatabaseScope(targetDb);

                    var filePaths = group.FileNames.Select(f => Path.Combine(BackupPath, f)).ToList();

                    var result = await _service.RunRestoreAsync(
                        vm.Scenario, filePaths, targetDb, dataPath, logPath,
                        overwrite: OverwriteExisting,
                        ct: _cts.Token);

                    vm.ApplyResult(result, DateTime.Now);

                    if (result.Status == BackupResultStatus.Done)
                        await CleanUpAfterRestoreAsync(vm, targetDb, filePaths, _cts.Token);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Run failed: {ex.Message}";
                IsRunning = false;
                return;
            }
        }
        finally
        {
            await _perfSampler.StopAsync();
        }

        ScanForFiles();
        ComputeRatios();

        var done  = selected.Count(s => s.Status == BackupResultStatus.Done);
        var error = selected.Count(s => s.Status == BackupResultStatus.Error);
        TotalDurationText = FormatDuration(TimeSpan.FromSeconds(selected.Sum(s => s.DurationSeconds)));
        StatusMessage = $"Completed: {done} succeeded, {error} failed.";
        IsRunning = false;
    }

    private async Task CleanUpAfterRestoreAsync(
        RestoreScenarioItemViewModel vm, string targetDb, List<string> filePaths, CancellationToken ct)
    {
        if (DropDatabaseAfterRestore)
        {
            try
            {
                await _service.DropDatabaseAsync(targetDb, ct);
            }
            catch (Exception ex)
            {
                StatusMessage = $"[{vm.Scenario.Name}] cleanup: could not drop {targetDb}: {ex.Message}";
            }
        }

        if (DeleteFilesAfterRestore)
        {
            foreach (var path in filePaths)
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception ex)
                {
                    StatusMessage = $"[{vm.Scenario.Name}] cleanup: could not delete {Path.GetFileName(path)}: {ex.Message}";
                }
            }
        }
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

    private bool CanRun() =>
        !IsRunning && Scenarios.Any(s => s.IsChecked && s.FileFound);

    public void SetBackupPathFromDialog(string path) => BackupPath = path;

    public void RescanFiles() => ScanForFiles();

    public async Task RefreshConnectionAsync() => await LoadDatabasesAsync();
}
