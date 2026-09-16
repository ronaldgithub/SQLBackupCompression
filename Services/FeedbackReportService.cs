using System;
using System.Collections.Generic;
using System.Linq;
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

    public static string BuildHeader(
        DateTime timestamp,
        string databaseName,
        double databaseSizeMb,
        string backupPath,
        IReadOnlyList<TableAnalysisRow> tableAnalysis)
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
        sb.AppendLine();

        AppendTableAnalysis(sb, tableAnalysis);

        return sb.ToString();
    }

    /// <summary>One section of the report for a single stripe count sweep — "Ask for
    /// Feedback" calls this once per swept stripe count (Off/2x/4x/8x) and concatenates
    /// the results after a single <see cref="BuildHeader"/>.</summary>
    public static string BuildStripeSection(
        int stripeCount,
        IReadOnlyList<ScenarioReportRow> backupRows,
        IReadOnlyList<ScenarioReportRow> restoreRows,
        IReadOnlyList<PerformanceSample> backupSamples,
        IReadOnlyList<PerformanceSample> restoreSamples)
    {
        var sb = new StringBuilder();
        sb.AppendLine(new string('=', 70));
        sb.AppendLine($"STRIPE COUNT: {stripeCount}x");
        sb.AppendLine(new string('=', 70));
        sb.AppendLine();

        AppendSection(sb, "BACKUP RESULTS", backupRows);
        AppendSection(sb, "RESTORE RESULTS", restoreRows);
        AppendSamples(sb, "BACKUP PERFORMANCE SAMPLES", backupSamples);
        AppendSamples(sb, "RESTORE PERFORMANCE SAMPLES", restoreSamples);

        return sb.ToString();
    }

    private static void AppendTableAnalysis(StringBuilder sb, IReadOnlyList<TableAnalysisRow> rows)
    {
        sb.AppendLine("TABLE ANALYSIS (top tables by size — same data as the Analyse button)");
        sb.AppendLine(new string('-', 70));
        if (rows.Count == 0)
        {
            sb.AppendLine("(no user tables found, or analysis failed)");
            sb.AppendLine();
            return;
        }

        foreach (var r in rows)
        {
            sb.AppendLine(
                $"{r.TableName,-40} size={r.ReservedMbText,-9} compression={r.CompressionText,-14} " +
                $"lob={r.LobCols} guid={r.GuidCols} unicode={r.UnicodeCols} fixedWidth={r.FixedWidthCols} float={r.FloatCols}");
        }

        var totalSizeMb = rows.Sum(r => r.ReservedMb);
        var totalSizeText = totalSizeMb >= 1024 ? $"{totalSizeMb / 1024:N1} GB" : $"{totalSizeMb:N1} MB";
        sb.AppendLine(
            $"{"TOTAL",-40} size={totalSizeText,-9} " +
            $"lob={rows.Sum(r => r.LobCols)} guid={rows.Sum(r => r.GuidCols)} unicode={rows.Sum(r => r.UnicodeCols)} " +
            $"fixedWidth={rows.Sum(r => r.FixedWidthCols)} float={rows.Sum(r => r.FloatCols)}");
        sb.AppendLine();
    }

    private static void AppendSection(StringBuilder sb, string title, IReadOnlyList<ScenarioReportRow> rows)
    {
        sb.AppendLine(title);
        sb.AppendLine(new string('-', 70));
        foreach (var r in rows)
        {
            var started = r.StartedAt is { } s ? s.ToString("HH:mm:ss.fff") : "-";
            var finished = r.FinishedAt is { } f ? f.ToString("HH:mm:ss.fff") : "-";
            sb.AppendLine(
                $"{r.Name,-20} status={r.Status,-9} started={started} finished={finished} duration={r.Duration,-10} " +
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

    private static void AppendSamples(StringBuilder sb, string title, IReadOnlyList<PerformanceSample> samples)
    {
        sb.AppendLine(title);
        sb.AppendLine(new string('-', 70));
        if (samples.Count == 0)
        {
            sb.AppendLine("(no samples captured)");
            sb.AppendLine();
            return;
        }

        foreach (var s in samples)
        {
            var totalCpu = s.TotalCpuPercent is { } tc ? $"{tc:N0}%" : "—";
            var sqlCpu = s.SqlCpuPercent is { } sc ? $"{sc:N0}%" : "—";
            sb.AppendLine(
                $"{s.Timestamp:HH:mm:ss.fff}  CPU total={totalCpu} sql={sqlCpu}  " +
                $"IO read={s.ReadLatencyMs:N1}ms write={s.WriteLatencyMs:N1}ms  " +
                $"Mem bufferPool={s.BufferPoolMb:N1}MB grants={s.MemoryGrantsMb:N1}MB");
        }
        sb.AppendLine();
    }
}
