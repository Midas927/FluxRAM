namespace FluxRAM.Core.Models;

public readonly record struct ProcessProtectionSummary(
    int ProcessNameCount,
    int ExactPathCount,
    int ChildProcessCount,
    int RelatedWindowCount)
{
    public int TotalCount => ProcessNameCount + ExactPathCount + ChildProcessCount + RelatedWindowCount;
}
