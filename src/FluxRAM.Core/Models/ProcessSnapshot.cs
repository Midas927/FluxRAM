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
    string? MainWindowTitle = null,
    // Explicit snapshots describe measured values; the live scraper sets validity for each reading.
    bool HasCpuMeasurement = true,
    bool HasIoMeasurement = true)
{
    public bool HasMeasuredActivity => HasCpuMeasurement && HasIoMeasurement;
}
