using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SqlBackupBenchmark.Models;
using SqlBackupBenchmark.Services;

namespace SqlBackupBenchmark.ViewModels;

public partial class RestoreTabViewModel : ViewModelBase
{
    private readonly SqlRestoreService _service;

    [ObservableProperty] private string _backupPath = @"D:\backups";
    [ObservableProperty] private ObservableCollection<string> _availableDatabases = [];
    [ObservableProperty] private string? _selectedDatabaseFilter;
    [ObservableProperty] private bool _overwriteExisting = true;
    [ObservableProperty] private string _statusMessage = "Connecting...";
    [ObservableProperty] private bool _isRunning;

    public ObservableCollection<RestoreScenarioItemViewModel> Scenarios { get; } = [];

    private CancellationTokenSource? _cts;

    public RestoreTabViewModel(ConnectionSettings connectionSettings)
    {
        _service = new SqlRestoreService(connectionSettings);

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
            var files = allBakFiles
                .Where(f => f.Contains(marker, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f);
            vm.SetFiles(files);
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
        StatusMessage = $"Restoring {selected.Count} scenario(s) serially...";

        try
        {
            var (dataPath, logPath) = await _service.GetDefaultPathsAsync(_cts.Token);

            foreach (var vm in selected)
            {
                if (_cts.Token.IsCancellationRequested) break;

                vm.SetRunning();
                var targetDb = DeriveRestoreDb(vm.SelectedFile!);
                StatusMessage = $"[{vm.Scenario.Name}] → {targetDb}...";

                var filePath = Path.Combine(BackupPath, vm.SelectedFile!);

                var result = await _service.RunRestoreAsync(
                    vm.Scenario, filePath, targetDb, dataPath, logPath,
                    overwrite: OverwriteExisting,
                    ct: _cts.Token);

                vm.ApplyResult(result);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Run failed: {ex.Message}";
            IsRunning = false;
            return;
        }

        ComputeRatios();

        var done  = selected.Count(s => s.Status == BackupResultStatus.Done);
        var error = selected.Count(s => s.Status == BackupResultStatus.Error);
        StatusMessage = $"Completed: {done} succeeded, {error} failed.";
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

    private bool CanRun() =>
        !IsRunning && Scenarios.Any(s => s.IsChecked && s.FileFound);

    private static string DeriveRestoreDb(string fileName)
    {
        var nameNoExt = Path.GetFileNameWithoutExtension(fileName);
        var match = Regex.Match(nameNoExt, @"^(.+)_\d{8}_\d{6}$");
        return match.Success ? match.Groups[1].Value : nameNoExt;
    }

    public void SetBackupPathFromDialog(string path) => BackupPath = path;

    public async Task RefreshConnectionAsync() => await LoadDatabasesAsync();
}
