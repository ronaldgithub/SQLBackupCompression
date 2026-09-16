using System;

namespace SqlBackupBenchmark.Services;

public static class CpuInfo
{
    public static bool IsIntel { get; }
    public static string VendorName { get; }
    public static int LogicalProcessorCount => Environment.ProcessorCount;

    static CpuInfo()
    {
        var id = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "";
        IsIntel = id.Contains("Intel", StringComparison.OrdinalIgnoreCase);
        VendorName = IsIntel ? "INTEL"
            : id.Contains("AMD", StringComparison.OrdinalIgnoreCase) ? "AMD"
            : "UNKNOWN";
    }
}
