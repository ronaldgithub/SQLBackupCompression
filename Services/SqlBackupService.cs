using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SqlBackupBenchmark.Models;

namespace SqlBackupBenchmark.Services;

public class SqlBackupService
{
    private const string ConnectionString =
        "Server=localhost;Integrated Security=true;TrustServerCertificate=true;Connection Timeout=5;";

    public async Task<List<string>> GetDatabasesAsync(CancellationToken ct = default)
    {
        var databases = new List<string>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT name FROM sys.databases WHERE database_id > 4 AND state_desc = 'ONLINE' ORDER BY name",
            conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            databases.Add(reader.GetString(0));
        return databases;
    }

    public async Task<BackupResult> RunBackupAsync(
        BackupScenario scenario,
        string database,
        string backupPath,
        string timestamp,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var result = new BackupResult
        {
            Scenario = scenario,
            Status = BackupResultStatus.Running,
            BackupFilePath = scenario.GetBackupFilePath(database, backupPath, timestamp)
        };

        var sql = scenario.GenerateSql(database, backupPath, timestamp);
        result.SqlStatement = sql;

        var sw = Stopwatch.StartNew();
        try
        {
            await using var conn = new SqlConnection(ConnectionString);
            conn.InfoMessage += (_, e) => progress?.Report(e.Message);
            await conn.OpenAsync(ct);

            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 0 };
            await cmd.ExecuteNonQueryAsync(ct);

            sw.Stop();
            result.Duration = sw.Elapsed;
            result.Status = BackupResultStatus.Done;

            if (File.Exists(result.BackupFilePath))
            {
                result.FileSizeMB = new FileInfo(result.BackupFilePath).Length / (1024.0 * 1024.0);
                result.MbPerSec = result.FileSizeMB / result.Duration.TotalSeconds;
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.Duration = sw.Elapsed;
            result.Status = BackupResultStatus.Error;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }
}
