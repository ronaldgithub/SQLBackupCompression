using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SqlBackupBenchmark.Models;

namespace SqlBackupBenchmark.Services;

public class SqlRestoreService(ConnectionSettings connectionSettings)
{
    private string ConnectionString => connectionSettings.BuildConnectionString();

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

    public async Task<(string DataPath, string LogPath)> GetDefaultPathsAsync(CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT SERVERPROPERTY('InstanceDefaultDataPath'), SERVERPROPERTY('InstanceDefaultLogPath')",
            conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return (reader.GetString(0), reader.GetString(1));
    }

    public async Task<List<(string LogicalName, string FileType)>> GetFileListAsync(
        IReadOnlyList<string> backupFilePaths,
        CancellationToken ct = default)
    {
        var files = new List<(string, string)>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        var diskClause = string.Join(", ", backupFilePaths.Select(p => $"DISK = N'{p}'"));
        await using var cmd = new SqlCommand(
            $"RESTORE FILELISTONLY FROM {diskClause}",
            conn) { CommandTimeout = 60 };
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            files.Add((reader.GetString(reader.GetOrdinal("LogicalName")),
                       reader.GetString(reader.GetOrdinal("Type"))));
        return files;
    }

    public async Task<RestoreResult> RunRestoreAsync(
        BackupScenario scenario,
        IReadOnlyList<string> backupFilePaths,
        string targetDatabase,
        string dataPath,
        string logPath,
        bool overwrite = true,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var result = new RestoreResult
        {
            Scenario = scenario,
            Status = BackupResultStatus.Running,
            BackupFilePaths = backupFilePaths
        };

        result.FileSizeMB = backupFilePaths.Where(File.Exists).Sum(p => new FileInfo(p).Length) / (1024.0 * 1024.0);

        var sw = Stopwatch.StartNew();
        try
        {
            var fileList = await GetFileListAsync(backupFilePaths, ct);
            var sql = BuildRestoreSql(targetDatabase, backupFilePaths, fileList, dataPath, logPath, overwrite);
            result.SqlStatement = sql;

            await using var conn = new SqlConnection(ConnectionString);
            conn.InfoMessage += (_, e) => progress?.Report(e.Message);
            await conn.OpenAsync(ct);

            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 0 };
            await cmd.ExecuteNonQueryAsync(ct);

            sw.Stop();
            result.Duration = sw.Elapsed;
            result.Status = BackupResultStatus.Done;

            if (result.FileSizeMB > 0 && result.Duration.TotalSeconds > 0)
                result.MbPerSec = result.FileSizeMB / result.Duration.TotalSeconds;
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

    /// <summary>Drops a restored scratch database, forcing out any lingering connections first
    /// (this app's own restore connection has already closed by the time this is called, but
    /// SINGLE_USER guards against anything else — SSMS, a stray tool — having it open).</summary>
    public async Task DropDatabaseAsync(string database, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];",
            conn) { CommandTimeout = 60 };
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string BuildRestoreSql(
        string targetDatabase,
        IReadOnlyList<string> backupFilePaths,
        List<(string LogicalName, string FileType)> fileList,
        string dataPath,
        string logPath,
        bool overwrite)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"RESTORE DATABASE [{targetDatabase}]");
        sb.AppendLine($"FROM {string.Join(", ", backupFilePaths.Select(p => $"DISK = N'{p}'"))}");
        sb.Append(overwrite ? "WITH REPLACE" : "WITH RECOVERY");

        var dataFiles  = fileList.Where(f => f.FileType == "D").ToList();
        var logFiles   = fileList.Where(f => f.FileType == "L").ToList();

        for (int i = 0; i < dataFiles.Count; i++)
        {
            var ext      = i == 0 ? ".mdf" : ".ndf";
            var suffix   = dataFiles.Count == 1 ? "" : $"_{i + 1}";
            var destPath = Path.Combine(dataPath, $"{targetDatabase}{suffix}{ext}");
            sb.AppendLine(",");
            sb.Append($"     MOVE N'{dataFiles[i].LogicalName}' TO N'{destPath}'");
        }

        for (int i = 0; i < logFiles.Count; i++)
        {
            var suffix   = logFiles.Count == 1 ? "_log" : $"_log{i + 1}";
            var destPath = Path.Combine(logPath, $"{targetDatabase}{suffix}.ldf");
            sb.AppendLine(",");
            sb.Append($"     MOVE N'{logFiles[i].LogicalName}' TO N'{destPath}'");
        }

        sb.AppendLine(",");
        sb.Append("     STATS = 10;");
        return sb.ToString();
    }
}
