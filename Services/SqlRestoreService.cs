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

public class SqlRestoreService
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
        string backupFilePath,
        CancellationToken ct = default)
    {
        var files = new List<(string, string)>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            $"RESTORE FILELISTONLY FROM DISK = N'{backupFilePath}'",
            conn) { CommandTimeout = 60 };
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            files.Add((reader.GetString(reader.GetOrdinal("LogicalName")),
                       reader.GetString(reader.GetOrdinal("Type"))));
        return files;
    }

    public async Task<RestoreResult> RunRestoreAsync(
        BackupScenario scenario,
        string backupFilePath,
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
            BackupFilePath = backupFilePath
        };

        if (File.Exists(backupFilePath))
            result.FileSizeMB = new FileInfo(backupFilePath).Length / (1024.0 * 1024.0);

        var sw = Stopwatch.StartNew();
        try
        {
            var fileList = await GetFileListAsync(backupFilePath, ct);
            var sql = BuildRestoreSql(targetDatabase, backupFilePath, fileList, dataPath, logPath, overwrite);
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

    private static string BuildRestoreSql(
        string targetDatabase,
        string backupFilePath,
        List<(string LogicalName, string FileType)> fileList,
        string dataPath,
        string logPath,
        bool overwrite)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"RESTORE DATABASE [{targetDatabase}]");
        sb.AppendLine($"FROM DISK = N'{backupFilePath}'");
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
