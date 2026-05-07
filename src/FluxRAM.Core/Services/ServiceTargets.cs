using System.Collections.Frozen;

namespace FluxRAM.Core.Services;

public static class ServiceTargets
{
    private static readonly FrozenSet<string> ServiceNames = new[]
    {
        "DiagTrack",
        "WSearch",
        "DmWappushService",
        "CDPSvc",
        "PimIndexMaintenanceSvc",
        "CopilotService"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> WindowsBackgroundServices => ServiceNames;

    public static bool Contains(string serviceName)
    {
        return ServiceNames.Contains(serviceName);
    }
}
