namespace FluxRAM.Core.Models;

public readonly record struct MemoryPurgeResult(
    int ProcessId,
    bool Success,
    long BeforeWorkingSetBytes,
    long AfterWorkingSetBytes,
    string? ErrorMessage)
{
    public long DeltaBytes => BeforeWorkingSetBytes - AfterWorkingSetBytes;

    public static MemoryPurgeResult Failed(int processId, string errorMessage)
    {
        return new MemoryPurgeResult(processId, false, 0, 0, errorMessage);
    }

    public static MemoryPurgeResult Succeeded(int processId, long beforeWorkingSetBytes, long afterWorkingSetBytes)
    {
        return new MemoryPurgeResult(processId, true, beforeWorkingSetBytes, afterWorkingSetBytes, null);
    }
}
