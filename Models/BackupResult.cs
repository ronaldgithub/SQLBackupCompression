using System;

namespace SqlBackupBenchmark.Models;

public enum BackupResultStatus { Pending, Running, Done, Error }

public class BackupResult
{
    public BackupScenario Scenario { get; set; } = null!;
    public BackupResultStatus Status { get; set; } = BackupResultStatus.Pending;
    public TimeSpan Duration { get; set; }
    public double FileSizeMB { get; set; }
    public double MbPerSec { get; set; }
    public string? ErrorMessage { get; set; }
    public string BackupFilePath { get; set; } = "";
    public string SqlStatement { get; set; } = "";
}
