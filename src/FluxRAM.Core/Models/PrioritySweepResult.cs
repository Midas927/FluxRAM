namespace FluxRAM.Core.Models;

public readonly record struct PrioritySweepResult(
    int ForegroundBoostedCount,
    int BackgroundDemotedCount,
    int FailedCount,
    string? ErrorMessage);
