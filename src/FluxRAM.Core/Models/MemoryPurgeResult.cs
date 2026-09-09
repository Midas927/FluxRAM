namespace FluxRAM.Core.Models;

public readonly record struct MemoryPurgeResult(
    int ProcessId,
    bool Success,
    long BeforeWorkingSetBytes,
    long AfterWorkingSetBytes,
    string? ErrorMessage,
    bool HasMeasurement = true)
{
    public long DeltaBytes => HasMeasurement ? BeforeWorkingSetBytes - AfterWorkingSetBytes : 0;

    public static MemoryPurgeResult Failed(int processId, string errorMessage)
    {
        return new MemoryPurgeResult(processId, false, 0, 0, errorMessage, HasMeasurement: false);
    }

    public static MemoryPurgeResult Succeeded(int processId, long beforeWorkingSetBytes, long afterWorkingSetBytes)
    {
        return new MemoryPurgeResult(processId, true, beforeWorkingSetBytes, afterWorkingSetBytes, null);
    }

    public static MemoryPurgeResult SucceededWithoutMeasurement(int processId, long beforeWorkingSetBytes)
    {
        // The after value is a placeholder, not a measured zero.
        return new MemoryPurgeResult(processId, true, beforeWorkingSetBytes, 0, null, HasMeasurement: false);
    }
}
