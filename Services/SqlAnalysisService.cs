using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SqlBackupBenchmark.Models;

namespace SqlBackupBenchmark.Services;

public class SqlAnalysisService(ConnectionSettings connectionSettings)
{
    private const string AnalysisSql = """
        SELECT TOP (25)
               QUOTENAME(s.name) + '.' + QUOTENAME(t.name) AS table_name,
               sz.reserved_mb,
               cmp.storage_compression,
               col.lob_cols, col.guid_cols, col.unicode_cols, col.fixed_width_cols, col.float_cols
        FROM sys.tables AS t
        JOIN sys.schemas AS s ON s.schema_id = t.schema_id
        CROSS APPLY (SELECT SUM(ps.reserved_page_count) * 8 / 1024.0 AS reserved_mb
                     FROM sys.dm_db_partition_stats AS ps
                     WHERE ps.object_id = t.object_id) AS sz
        CROSS APPLY (SELECT STRING_AGG(d.data_compression_desc, ', ') AS storage_compression
                     FROM (SELECT DISTINCT p.data_compression_desc
                           FROM sys.partitions AS p
                           WHERE p.object_id = t.object_id) AS d) AS cmp
        CROSS APPLY (SELECT
                        SUM(CASE WHEN c.max_length = -1 OR ty.name IN ('text','ntext','image','xml') THEN 1 ELSE 0 END) AS lob_cols,
                        SUM(CASE WHEN ty.name = 'uniqueidentifier'                  THEN 1 ELSE 0 END) AS guid_cols,
                        SUM(CASE WHEN ty.name IN ('nchar','nvarchar','ntext')       THEN 1 ELSE 0 END) AS unicode_cols,
                        SUM(CASE WHEN ty.name IN ('char','nchar','binary')          THEN 1 ELSE 0 END) AS fixed_width_cols,
                        SUM(CASE WHEN ty.name IN ('float','real')                   THEN 1 ELSE 0 END) AS float_cols
                     FROM sys.columns AS c
                     JOIN sys.types AS ty ON ty.user_type_id = c.system_type_id
                     WHERE c.object_id = t.object_id) AS col
        ORDER BY sz.reserved_mb DESC;
        """;

    public async Task<List<TableAnalysisRow>> GetTableAnalysisAsync(string database, CancellationToken ct = default)
    {
        var results = new List<TableAnalysisRow>();

        var csb = new SqlConnectionStringBuilder(connectionSettings.BuildConnectionString())
        {
            InitialCatalog = database
        };

        await using var conn = new SqlConnection(csb.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(AnalysisSql, conn) { CommandTimeout = 60 };
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new TableAnalysisRow
            {
                TableName          = reader.GetString(0),
                ReservedMb         = reader.IsDBNull(1) ? 0 : Convert.ToDouble(reader.GetValue(1)),
                StorageCompression = reader.IsDBNull(2) ? null : reader.GetString(2),
                LobCols            = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                GuidCols           = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                UnicodeCols        = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                FixedWidthCols     = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                FloatCols          = reader.IsDBNull(7) ? 0 : reader.GetInt32(7)
            });
        }
        return results;
    }
}
