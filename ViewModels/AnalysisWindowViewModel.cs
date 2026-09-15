using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SqlBackupBenchmark.Models;

namespace SqlBackupBenchmark.ViewModels;

public partial class AnalysisWindowViewModel : ViewModelBase
{
    public string DatabaseName { get; }
    public ObservableCollection<TableAnalysisRow> Rows { get; }

    public string Subtitle => Rows.Count switch
    {
        0 => $"No user tables found in {DatabaseName}.",
        1 => $"1 table in {DatabaseName}, largest first.",
        _ => $"Top {Rows.Count} tables in {DatabaseName}, largest first."
    };

    public bool HasRows => Rows.Count > 0;

    private double TotalSizeMb => Rows.Sum(r => r.ReservedMb);

    public string TotalSizeText => TotalSizeMb >= 1024
        ? $"{TotalSizeMb / 1024:N1} GB"
        : $"{TotalSizeMb:N1} MB";

    public int TotalLobCols        => Rows.Sum(r => r.LobCols);
    public int TotalGuidCols       => Rows.Sum(r => r.GuidCols);
    public int TotalUnicodeCols    => Rows.Sum(r => r.UnicodeCols);
    public int TotalFixedWidthCols => Rows.Sum(r => r.FixedWidthCols);
    public int TotalFloatCols      => Rows.Sum(r => r.FloatCols);

    public AnalysisWindowViewModel(string databaseName, IEnumerable<TableAnalysisRow> rows)
    {
        DatabaseName = databaseName;
        Rows = new ObservableCollection<TableAnalysisRow>(rows);
    }
}
