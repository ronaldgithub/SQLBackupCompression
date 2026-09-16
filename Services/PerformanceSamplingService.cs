using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SqlBackupBenchmark.Models;

namespace SqlBackupBenchmark.Services;

/*
 * CPU/IO/memory DMV shapes adapted from Erik Darling's SQL Server Performance Monitor
 * (https://github.com/erikdarlingdata/PerformanceMonitor), MIT licensed,
 * Copyright (c) 2026 Erik Darling, Darling Data LLC. The CPU ring-buffer query in
 * particular (tick-to-walltime conversion, incomplete-record and Linux SystemIdle=0
 * guards) is adapted from that project's PerformanceMonitor.Collectors/CpuUtilizationCollector.cs.
 */

/// <summary>
/// Polls CPU/IO/memory DMVs on a dedicated connection while a backup or restore run is
/// in progress, independent of whichever connection is executing the BACKUP/RESTORE
/// statement itself. SQL Server's own CPU ring buffer only advances roughly once every
/// 60 seconds — short scenario runs will see sparse/flat CPU data, which is a known
/// limitation of the source DMV, not a bug. IO latency reflects the scoped database's
/// own data/log files (source DB reads during backup, target DB writes during restore),
/// not the destination .bak path's own throughput.
/// </summary>
public sealed class PerformanceSamplingService(ConnectionSettings connectionSettings) : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1500);

    private readonly object _lock = new();
    private readonly Dictionary<int, IoCounters> _previousIo = new();

    private SqlConnection? _connection;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private string? _databaseScope;
    private DateTime? _lastEmittedCpuSampleTime;

    public event Action<PerformanceSample>? SampleReceived;
    public event Action<Exception>? SamplingError;

    public async Task StartAsync(string? database, CancellationToken ct)
    {
        _connection = new SqlConnection(connectionSettings.BuildConnectionString());
        await _connection.OpenAsync(ct);

        lock (_lock)
        {
            _databaseScope = database;
            _previousIo.Clear();
            _lastEmittedCpuSampleTime = null;
        }

        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = RunLoopAsync(_loopCts.Token);
    }

    /// <summary>Re-targets IO sampling to a different database (e.g. each restore scenario's
    /// own target database) and clears the delta baseline so the next tick seeds fresh
    /// instead of diffing across two different databases' files.</summary>
    public void SetDatabaseScope(string? database)
    {
        lock (_lock)
        {
            _databaseScope = database;
            _previousIo.Clear();
        }
    }

    public async Task StopAsync()
    {
        if (_loopCts is not null)
        {
            _loopCts.Cancel();
            try
            {
                if (_loopTask is not null) await _loopTask;
            }
            catch (OperationCanceledException) { }
            finally
            {
                _loopCts.Dispose();
                _loopCts = null;
                _loopTask = null;
            }
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private async Task RunLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await SampleOnceAsync(ct);
        }
        catch (OperationCanceledException) { }
    }

    private async Task SampleOnceAsync(CancellationToken ct)
    {
        try
        {
            var (readMs, writeMs) = await QueryIoAsync(ct);
            var (bufferPoolMb, grantsMb) = await QueryMemoryAsync(ct);
            var (totalCpu, sqlCpu) = await QueryCpuAsync(ct);
            SampleReceived?.Invoke(new PerformanceSample(DateTime.Now, totalCpu, sqlCpu, readMs, writeMs, bufferPoolMb, grantsMb));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Transient DMV/connection hiccup — skip this tick, keep the loop alive.
            SamplingError?.Invoke(ex);
        }
    }

    private readonly record struct IoCounters(long Reads, long Writes, long StallReadMs, long StallWriteMs);

    private const string IoQuerySql = @"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    file_id = vfs.file_id,
    num_of_reads = vfs.num_of_reads,
    num_of_writes = vfs.num_of_writes,
    io_stall_read_ms = vfs.io_stall_read_ms,
    io_stall_write_ms = vfs.io_stall_write_ms
FROM sys.dm_io_virtual_file_stats(DB_ID(@database), NULL) AS vfs
OPTION (RECOMPILE);";

    private async Task<(double ReadMs, double WriteMs)> QueryIoAsync(CancellationToken ct)
    {
        string? database;
        lock (_lock) { database = _databaseScope; }
        if (database is null) return (0, 0);

        var current = new Dictionary<int, IoCounters>();
        await using (var cmd = new SqlCommand(IoQuerySql, _connection))
        {
            cmd.Parameters.AddWithValue("@database", database);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                current[Convert.ToInt32(reader.GetValue(0))] = new IoCounters(
                    reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4));
        }

        long deltaReads = 0, deltaWrites = 0, deltaStallReadMs = 0, deltaStallWriteMs = 0;
        lock (_lock)
        {
            foreach (var (fileId, counters) in current)
            {
                if (_previousIo.TryGetValue(fileId, out var prev))
                {
                    deltaReads += Math.Max(0, counters.Reads - prev.Reads);
                    deltaWrites += Math.Max(0, counters.Writes - prev.Writes);
                    deltaStallReadMs += Math.Max(0, counters.StallReadMs - prev.StallReadMs);
                    deltaStallWriteMs += Math.Max(0, counters.StallWriteMs - prev.StallWriteMs);
                }
            }
            _previousIo.Clear();
            foreach (var kvp in current) _previousIo[kvp.Key] = kvp.Value;
        }

        var readMs = deltaReads > 0 ? (double)deltaStallReadMs / deltaReads : 0;
        var writeMs = deltaWrites > 0 ? (double)deltaStallWriteMs / deltaWrites : 0;
        return (readMs, writeMs);
    }

    private const string MemoryQuerySql = @"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    buffer_pool_mb = CONVERT(decimal(18,2), pc_buffer.cntr_value / 1024.0),
    grants_mb = CONVERT(decimal(18,2), ISNULL(g.granted_memory_kb, 0) / 1024.0)
FROM
(
    SELECT cntr_value
    FROM sys.dm_os_performance_counters
    WHERE counter_name = N'Database Cache Memory (KB)'
) AS pc_buffer
CROSS JOIN
(
    SELECT granted_memory_kb = SUM(ISNULL(granted_memory_kb, 0))
    FROM sys.dm_exec_query_resource_semaphores
    WHERE max_target_memory_kb IS NOT NULL
) AS g
OPTION (RECOMPILE);";

    private async Task<(double BufferPoolMb, double GrantsMb)> QueryMemoryAsync(CancellationToken ct)
    {
        await using var cmd = new SqlCommand(MemoryQuerySql, _connection);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return (0, 0);
        return (Convert.ToDouble(reader.GetDecimal(0)), Convert.ToDouble(reader.GetDecimal(1)));
    }

    /* Ring buffer + tick-to-walltime conversion + guards adapted verbatim from
       CpuUtilizationCollector.RingBufferQueryText (see file header attribution). */
    private const string CpuQuerySql = @"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

DECLARE
    @ms_ticks bigint,
    @is_linux bit = 0;

SELECT @ms_ticks = dosi.ms_ticks FROM sys.dm_os_sys_info AS dosi;

IF OBJECT_ID(N'sys.dm_os_host_info', N'V') IS NOT NULL
    EXEC sys.sp_executesql
        N'SELECT @linux = CASE WHEN hi.host_platform = N''Linux'' THEN 1 ELSE 0 END FROM sys.dm_os_host_info AS hi;',
        N'@linux bit OUTPUT', @linux = @is_linux OUTPUT;

SELECT TOP (1)
    sample_time = DATEADD(
        MILLISECOND, -((@ms_ticks - t.timestamp) % 1000),
        DATEADD(SECOND, -((@ms_ticks - t.timestamp) / 1000), SYSDATETIME())),
    sqlserver_cpu_utilization = x.process_utilization,
    other_process_cpu_utilization =
        CASE
            WHEN @is_linux = 1 AND x.system_idle = 0
            THEN NULL
            WHEN (100 - x.system_idle - x.process_utilization) < 0
            THEN 0
            ELSE 100 - x.system_idle - x.process_utilization
        END
FROM
(
    SELECT
        dorb.timestamp,
        record = CONVERT(xml, dorb.record)
    FROM sys.dm_os_ring_buffers AS dorb
    WHERE dorb.ring_buffer_type = N'RING_BUFFER_SCHEDULER_MONITOR'
) AS t
CROSS APPLY
(
    SELECT
        process_utilization = t.record.value('(Record/SchedulerMonitorEvent/SystemHealth/ProcessUtilization)[1]', 'integer'),
        system_idle = t.record.value('(Record/SchedulerMonitorEvent/SystemHealth/SystemIdle)[1]', 'integer')
) AS x
/* Skip ring-buffer records lacking a complete SystemHealth block. */
WHERE x.process_utilization IS NOT NULL
AND   x.system_idle IS NOT NULL
ORDER BY t.timestamp DESC
OPTION(RECOMPILE);";

    private async Task<(double? Total, double? Sql)> QueryCpuAsync(CancellationToken ct)
    {
        await using var cmd = new SqlCommand(CpuQuerySql, _connection);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return (null, null);

        var sampleTime = reader.GetDateTime(0);
        lock (_lock)
        {
            if (_lastEmittedCpuSampleTime.HasValue && sampleTime <= _lastEmittedCpuSampleTime.Value)
                return (null, null);
            _lastEmittedCpuSampleTime = sampleTime;
        }

        var sqlCpu = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
        int? otherCpu = reader.IsDBNull(2) ? null : reader.GetInt32(2);
        double total = sqlCpu + (otherCpu ?? 0);
        return (total, sqlCpu);
    }
}
