namespace FluxRAM.Core.Models;

public enum ApplicationYieldStatus
{
    Observing,
    Completed,
    Unknown
}

public enum ApplicationYieldSampleStatus
{
    Pending,
    Measured,
    Unknown
}

public sealed record ApplicationYieldCheckpoint(
    int ElapsedSeconds,
    DateTimeOffset? ObservedAt,
    long? WorkingSetBytes,
    long? RetainedBytes,
    double? ReconstructedPercent,
    ApplicationYieldSampleStatus Status);

// Memory totals describe only the measured successful targets, not the whole application.
public sealed record ApplicationYieldReport(
    string? ApplicationKey,
    string ApplicationName,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int TargetProcessCount,
    int MeasuredProcessCount,
    long? BeforeWorkingSetBytes,
    long? AfterWorkingSetBytes,
    IReadOnlyList<ApplicationYieldCheckpoint> Checkpoints,
    ApplicationYieldStatus Status,
    bool? IsLowYield,
    int ConsecutiveLowYieldCount,
    DateTimeOffset? DeferredUntil)
{
    // The initial API name is retained; the streak includes high-reconstruction windows too.
    public int ConsecutiveBadRunCount => ConsecutiveLowYieldCount;
}
