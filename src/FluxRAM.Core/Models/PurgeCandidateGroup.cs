namespace FluxRAM.Core.Models;

public sealed record PurgeCandidateGroup(
    string ProcessName,
    string? ExecutablePath,
    IReadOnlyList<ProcessSnapshot> Processes,
    IReadOnlyList<ProcessSnapshot> ObservedProcesses,
    long WorkingSetBytes,
    double CpuUsagePercent,
    double IoBytesPerSecond,
    double ColdnessScore,
    bool HasForegroundProcess,
    bool HasVisibleWindow);
