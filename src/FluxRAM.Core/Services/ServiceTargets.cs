using System.Collections.Frozen;

using FluxRAM.Core.Models;

namespace FluxRAM.Core.Services;

public static class ServiceTargets
{
    private static readonly ServiceTargetDefinition[] TargetDefinitions =
    {
        new("DiagTrack", "Connected User Experiences and Telemetry", OptionalServiceKind.System, OptionalServiceStopGuidance.WhenFeatureUnused),
        new("DmWappushService", "WAP Push Message Routing", OptionalServiceKind.System, OptionalServiceStopGuidance.WhenFeatureUnused),
        new("CDPSvc", "Connected Devices Platform", OptionalServiceKind.System, OptionalServiceStopGuidance.WhenFeatureUnused),
        new("CDPUserSvc", "Connected Devices Platform User Service", OptionalServiceKind.System, OptionalServiceStopGuidance.WhenFeatureUnused),
        new("PimIndexMaintenanceSvc", "Contact Data Indexing", OptionalServiceKind.System, OptionalServiceStopGuidance.WhenFeatureUnused),
        new("CopilotService", "Microsoft Copilot Service", OptionalServiceKind.Application, OptionalServiceStopGuidance.WithApplication),
        new("WSearch", "Windows Search", OptionalServiceKind.System, OptionalServiceStopGuidance.KeepRunning)
    };
    private static readonly FrozenSet<string> ServiceNames = TargetDefinitions
        .Select(definition => definition.NamePrefix)
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> WindowsBackgroundServices => ServiceNames;

    public static bool Contains(string serviceName)
    {
        return TargetDefinitions.Any(definition => Matches(definition.NamePrefix, serviceName));
    }

    public static IReadOnlyList<OptionalServiceCandidate> ResolveCandidates(
        IEnumerable<string> installedServiceNames)
    {
        return installedServiceNames
            .Select(serviceName => new
            {
                ServiceName = serviceName,
                Definition = TargetDefinitions.FirstOrDefault(definition =>
                    Matches(definition.NamePrefix, serviceName))
            })
            .Where(item => item.Definition is not null)
            .Select(item => new OptionalServiceCandidate(
                item.ServiceName,
                item.Definition!.DisplayName,
                Kind: item.Definition.Kind,
                StopGuidance: item.Definition.StopGuidance))
            .DistinctBy(candidate => candidate.ServiceName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool IsRelatedApplicationService(
        string serviceName,
        IEnumerable<string> applicationNames)
    {
        var normalizedServiceName = NormalizeName(serviceName);
        return applicationNames
            .Select(NormalizeName)
            .Where(applicationName => applicationName.Length >= 4)
            .Any(applicationName =>
                normalizedServiceName.StartsWith(applicationName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool Matches(string prefix, string serviceName)
    {
        return serviceName.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
            serviceName.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeName(string value)
    {
        var normalized = value.Trim();
        return normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^4]
            : normalized;
    }

    private sealed record ServiceTargetDefinition(
        string NamePrefix,
        string DisplayName,
        OptionalServiceKind Kind,
        OptionalServiceStopGuidance StopGuidance);
}
