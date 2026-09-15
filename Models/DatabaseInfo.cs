namespace SqlBackupBenchmark.Models;

public class DatabaseInfo
{
    public string Name { get; set; } = "";
    public double SizeMB { get; set; }

    public string SizeText => SizeMB >= 1024
        ? $"{SizeMB / 1024:0.#} GB"
        : $"{SizeMB:0} MB";

    public string SizeBracketText => $"[{SizeText}]";

    public string DisplayText => $"{Name} {SizeBracketText}";
}
