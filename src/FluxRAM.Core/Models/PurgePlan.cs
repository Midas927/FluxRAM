namespace FluxRAM.Core.Models;

public readonly record struct PurgePlan(
    bool ShouldPurge,
    string DecisionMessage,
    IReadOnlyList<ProcessSnapshot> Candidates,
    ProcessProtectionSummary ProtectionSummary = default);
