using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SqlBackupBenchmark.Models;

namespace SqlBackupBenchmark.Services;

public class SqlBackupService(ConnectionSettings connectionSettings)
{
    private string ConnectionString => connectionSettings.BuildConnectionString();

    public async Task<List<DatabaseInfo>> GetDatabasesAsync(CancellationToken ct = default)
    {
        var databases = new List<DatabaseInfo>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT d.name, SUM(mf.size) * 8.0 / 1024 AS SizeMB
              FROM sys.databases d
              JOIN sys.master_files mf ON mf.database_id = d.database_id
              WHERE d.database_id > 4 AND d.state_desc = 'ONLINE'
              GROUP BY d.name
              ORDER BY d.name",
            conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            databases.Add(new DatabaseInfo
            {
                Name = reader.GetString(0),
                SizeMB = Convert.ToDouble(reader.GetValue(1))
            });
        return databases;
    }

    public async Task<BackupResult> RunBackupAsync(
        BackupScenario scenario,
        string database,
        string backupPath,
        string timestamp,
        int stripeCount = 1,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var result = new BackupResult
        {
            Scenario = scenario,
            Status = BackupResultStatus.Running,
            BackupFilePaths = scenario.GetBackupFilePaths(database, backupPath, timestamp, stripeCount)
        };

        var sql = scenario.GenerateSql(database, backupPath, timestamp, stripeCount);
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

            var totalBytes = result.BackupFilePaths
                .Where(File.Exists)
                .Sum(p => new FileInfo(p).Length);
            if (totalBytes > 0)
            {
                result.FileSizeMB = totalBytes / (1024.0 * 1024.0);
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
