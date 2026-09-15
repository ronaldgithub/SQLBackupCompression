namespace SqlBackupBenchmark.Models;

public class TableAnalysisRow
{
    public string TableName { get; set; } = "";
    public double ReservedMb { get; set; }
    public string? StorageCompression { get; set; }
    public int LobCols { get; set; }
    public int GuidCols { get; set; }
    public int UnicodeCols { get; set; }
    public int FixedWidthCols { get; set; }
    public int FloatCols { get; set; }

    public string ReservedMbText => ReservedMb >= 1024
        ? $"{ReservedMb / 1024:N1} GB"
        : $"{ReservedMb:N1} MB";

    public string CompressionText => string.IsNullOrWhiteSpace(StorageCompression) ? "NONE" : StorageCompression;
}
