using System;
using System.Collections.Generic;
using System.IO;

namespace SqlBackupBenchmark.Models;

public class BackupScenario
{
    public string Name { get; init; } = "";
    public bool UseCompression { get; init; }
    public string? Algorithm { get; init; }   // null | MS_XPRESS | QAT_DEFLATE | ZSTD
    public string? Level { get; init; }       // null | LOW | MEDIUM | HIGH

    public string GenerateSql(string database, string backupPath, string timestamp)
    {
        var fileName = $"{database}_{Name}_{timestamp}.bak";
        var filePath = Path.Combine(backupPath, fileName);
        var withClause = BuildWithClause();
        return $"BACKUP DATABASE [{database}]\r\nTO DISK = N'{filePath}'\r\nWITH {withClause},\r\n     STATS = 10;\r\n";
    }

    public string GetBackupFilePath(string database, string backupPath, string timestamp)
        => Path.Combine(backupPath, $"{database}_{Name}_{timestamp}.bak");

    private string BuildWithClause()
    {
        if (!UseCompression) return "NO_COMPRESSION";

        // SQL Server 2025 syntax: COMPRESSION (ALGORITHM = X, LEVEL = Y)
        var inner = new List<string>();
        if (Algorithm is not null) inner.Add($"ALGORITHM = {Algorithm}");
        if (Level is not null)     inner.Add($"LEVEL = {Level}");

        return inner.Count == 0 ? "COMPRESSION" : $"COMPRESSION ({string.Join(", ", inner)})";
    }

    public static IReadOnlyList<BackupScenario> AllScenarios()
    {
        var scenarios = new List<BackupScenario>
        {
            new() { Name = "NO_COMPRESSION", UseCompression = false },
            new() { Name = "DEFAULT",        UseCompression = true,  Algorithm = null }
        };

        string[] algorithms = ["MS_XPRESS", "QAT_DEFLATE", "ZSTD"];
        string[] levels     = ["LOW", "MEDIUM", "HIGH"];

        foreach (var algo in algorithms)
            foreach (var level in levels)
                scenarios.Add(new BackupScenario
                {
                    Name      = $"{algo}_{level}",
                    UseCompression = true,
                    Algorithm = algo,
                    Level     = level
                });

        return scenarios;
    }
}
