using System;

namespace SqlBackupBenchmark.Models;

/// <summary>
/// One tick of live CPU/IO/memory data captured by <see cref="Services.PerformanceSamplingService"/>
/// while a backup or restore run is in progress. CPU fields are null on ticks where SQL Server's
/// own ~60-second ring buffer hasn't produced a new sample yet — never a stale repeat.
/// </summary>
public record PerformanceSample(
    DateTime Timestamp,
    double? TotalCpuPercent,
    double? SqlCpuPercent,
    double ReadLatencyMs,
    double WriteLatencyMs,
    double BufferPoolMb,
    double MemoryGrantsMb);
