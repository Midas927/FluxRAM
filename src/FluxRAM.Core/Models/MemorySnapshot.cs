namespace FluxRAM.Core.Models;

public readonly record struct MemorySnapshot(
    ulong AvailablePhysicalMemoryBytes,
    ulong TotalPhysicalMemoryBytes,
    uint MemoryLoadPercent);
