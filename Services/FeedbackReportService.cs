using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using SqlBackupBenchmark.Models;

namespace SqlBackupBenchmark.Services;

public static class FeedbackReportService
{
    private static string AppVersion =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion?.Split('+')[0]
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "0.0.0";

    public static string Build(
        DateTime timestamp,
        string databaseName,
        double databaseSizeMb,
        string backupPath,
        int stripeCount,
        IReadOnlyList<ScenarioReportRow> backupRows,
        IReadOnlyList<ScenarioReportRow> restoreRows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SQL Server 2025 Backup/Restore Benchmark - Feedback Report");
        sb.AppendLine(new string('=', 70));
        sb.AppendLine($"Generated:    {timestamp:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"App version:  {AppVersion}");
        sb.AppendLine($"CPU:          {CpuInfo.VendorName} ({CpuInfo.LogicalProcessorCount} logical processors)");
        sb.AppendLine($"OS:           {Environment.OSVersion}");
        sb.AppendLine($"Runtime:      {Environment.Version}");
        sb.AppendLine($"Database:     {databaseName} ({databaseSizeMb:N1} MB)");
        sb.AppendLine($"Backup path:  {backupPath}");
        sb.AppendLine($"Stripe count: {stripeCount}x");
        sb.AppendLine();

        AppendSection(sb, "BACKUP RESULTS", backupRows);
        AppendSection(sb, "RESTORE RESULTS", restoreRows);

        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, string title, IReadOnlyList<ScenarioReportRow> rows)
    {
        sb.AppendLine(title);
        sb.AppendLine(new string('-', 70));
        foreach (var r in rows)
        {
            sb.AppendLine(
                $"{r.Name,-20} status={r.Status,-9} duration={r.Duration,-10} " +
                $"size={r.FileSizeMb,-9} MB  rate={r.MbPerSec,-8} MB/s  ratio={r.Ratio}");
            if (!string.IsNullOrEmpty(r.Error))
                sb.AppendLine($"    ERROR: {r.Error}");
        }
        sb.AppendLine();

        sb.AppendLine($"{title} - SQL statements:");
        foreach (var r in rows)
        {
            sb.AppendLine($"-- {r.Name} --");
            sb.AppendLine(string.IsNullOrEmpty(r.Sql) ? "(not run)" : r.Sql);
        }
        sb.AppendLine();
    }
}
