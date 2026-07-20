using FluxRAM.Core.Models;

namespace FluxRAM.App.Configuration;

public static class DeepReleaseCandidateDeduplicator
{
    public static IReadOnlyList<ExtremeCloseCandidate> RemoveServiceDuplicates(
        IReadOnlyList<ExtremeCloseCandidate> applications,
        IReadOnlyList<OptionalServiceCandidate> services)
    {
        var serviceProcessIds = services
            .Where(candidate => candidate.ProcessId > 0)
            .Select(candidate => candidate.ProcessId)
            .ToHashSet();
        var serviceNames = services
            .Select(candidate => candidate.ServiceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return applications
            .Where(candidate =>
                !serviceNames.Contains(candidate.ProcessName) &&
                !candidate.ProcessIds.All(serviceProcessIds.Contains))
            .ToArray();
    }
}
