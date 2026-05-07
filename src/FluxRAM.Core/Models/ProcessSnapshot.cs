namespace FluxRAM.Core.Models;

public readonly record struct ProcessSnapshot(
    int ProcessId,
    string ProcessName,
    long WorkingSetBytes,
    bool IsForeground,
    double CpuUsagePercent = 0d,
    bool HasVisibleWindow = false,
    double ColdnessScore = 0d,
    string? ExecutablePath = null,
    double IoBytesPerSecond = 0d,
    int? ParentProcessId = null,
    string? MainWindowTitle = null);
