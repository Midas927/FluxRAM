namespace FluxRAM.App.Configuration;

public sealed record ExtremeCloseCandidate(
    string ProcessName,
    IReadOnlyList<int> ProcessIds,
    long WorkingSetBytes,
    double CpuUsagePercent,
    double IoBytesPerSecond,
    bool HasForegroundProcess,
    bool HasVisibleWindow,
    bool IsDefaultSelected);
