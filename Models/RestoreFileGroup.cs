using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SqlBackupBenchmark.Models;

/// <summary>
/// One restorable backup set for a scenario/run: a single .bak file, or (for a striped
/// backup) the N files that together make up one BACKUP DATABASE statement's output,
/// grouped so the restore tab can treat them as a single combobox entry and pass all N
/// paths into one multi-DISK RESTORE statement.
/// </summary>
public class RestoreFileGroup
{
    private static readonly Regex StripeSuffix = new(@"^(?<base>.+)_stripe\d+of\d+\.bak$", RegexOptions.IgnoreCase);
    private static readonly Regex TimestampSuffix = new(@"^(.+)_\d{8}_\d{6}$");

    public string RestoreDbName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public IReadOnlyList<string> FileNames { get; init; } = [];

    public override string ToString() => DisplayName;

    public static IEnumerable<RestoreFileGroup> GroupFrom(IEnumerable<string> fileNames)
    {
        var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in fileNames)
        {
            var match = StripeSuffix.Match(f);
            var key = match.Success ? match.Groups["base"].Value + ".bak" : f;
            if (!groups.TryGetValue(key, out var members)) groups[key] = members = [];
            members.Add(f);
        }

        foreach (var (key, members) in groups)
        {
            var ordered = members.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            var nameNoExt = Path.GetFileNameWithoutExtension(key);
            var tsMatch = TimestampSuffix.Match(nameNoExt);
            var restoreDb = tsMatch.Success ? tsMatch.Groups[1].Value : nameNoExt;
            var displayName = ordered.Count > 1 ? $"{key} ({ordered.Count}x striped)" : key;
            yield return new RestoreFileGroup { RestoreDbName = restoreDb, DisplayName = displayName, FileNames = ordered };
        }
    }
}
