namespace SqlBackupBenchmark.Models;

public record ScenarioReportRow(
    string Name,
    string Status,
    string Duration,
    string FileSizeMb,
    string MbPerSec,
    string Ratio,
    string Sql,
    string? Error);
