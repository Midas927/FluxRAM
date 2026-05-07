namespace FluxRAM.Core.Models;

public readonly record struct AppOverheadSnapshot(
    double CpuUsagePercent,
    long WorkingSetBytes,
    long PrivateBytes,
    int HandleCount);
