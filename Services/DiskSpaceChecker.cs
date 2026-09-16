using System.IO;

namespace SqlBackupBenchmark.Services;

public static class DiskSpaceChecker
{
    /// <summary>Percentage of free space remaining on the drive containing the given path.
    /// Returns 100 if the drive can't be resolved, so a bad/unmounted path never falsely
    /// blocks a run — the backup/restore itself will fail with a clearer error in that case.</summary>
    public static double GetFreeSpacePercent(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(root)) return 100;

        var drive = new DriveInfo(root);
        return drive.TotalSize > 0 ? (double)drive.AvailableFreeSpace / drive.TotalSize * 100 : 100;
    }
}
