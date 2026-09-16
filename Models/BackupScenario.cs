using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SqlBackupBenchmark.Models;

public class BackupScenario
{
    public string Name { get; init; } = "";
    public bool UseCompression { get; init; }
    public string? Algorithm { get; init; }   // null | MS_XPRESS | QAT_DEFLATE | ZSTD
    public string? Level { get; init; }       // null | LOW | MEDIUM | HIGH

    public string GenerateSql(string database, string backupPath, string timestamp, int stripeCount = 1)
    {
        var filePaths = GetBackupFilePaths(database, backupPath, timestamp, stripeCount);
        var diskClause = string.Join(",\r\n     ",
            filePaths.Select((p, i) => (i == 0 ? "TO DISK = N'" : "DISK = N'") + p + "'"));
        var withClause = BuildWithClause();
        return $"BACKUP DATABASE [{database}]\r\n{diskClause}\r\nWITH COPY_ONLY, {withClause},\r\n     STATS = 10;\r\n";
    }

    /// <summary>
    /// Returns the backup file path(s) for this scenario/run. A single-element list when
    /// stripeCount is 1 (using the original, unsuffixed filename), otherwise one path per
    /// stripe file, named "..._stripe{i}of{N}.bak" so the restore tab can group them back
    /// into one restorable set.
    /// </summary>
    public IReadOnlyList<string> GetBackupFilePaths(string database, string backupPath, string timestamp, int stripeCount = 1)
    {
        if (stripeCount <= 1)
            return [Path.Combine(backupPath, $"{database}_{Name}_{timestamp}.bak")];

        return Enumerable.Range(1, stripeCount)
            .Select(i => Path.Combine(backupPath, $"{database}_{Name}_{timestamp}_stripe{i}of{stripeCount}.bak"))
            .ToList();
    }

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
