using System.IO;
using FluxRAM.Core.Models;
using FluxRAM.Core.Services;

namespace FluxRAM.App.Configuration;

public static class ExtremeCloseCandidateFactory
{
    private const long MinimumGroupWorkingSetBytes = 256L * 1024 * 1024;
    private const double ActiveCpuPercent = 20d;
    private const double ActiveIoBytesPerSecond = 16d * 1024 * 1024;

    public static IReadOnlyList<ExtremeCloseCandidate> FromSnapshots(
        IReadOnlyList<ProcessSnapshot> snapshots,
        IReadOnlyCollection<string>? protectedProcessNames = null,
        IReadOnlyCollection<string>? protectedProcessPaths = null,
        int? currentProcessId = null,
        bool enableAdvancedProtection = true)
    {
        var protectionContext = ProcessProtectionMatcher.CreateContext(
            snapshots,
            protectedProcessNames,
            protectedProcessPaths);

        return snapshots
            .Where(snapshot => !IsCurrentProcess(snapshot, currentProcessId))
            .Where(snapshot => !SystemProcessWhitelist.Contains(snapshot.ProcessName))
            .Where(snapshot => !GamingProcessProtectionCatalog.Contains(snapshot.ProcessName))
            .Where(snapshot => ProcessProtectionMatcher.Match(
                snapshot,
                protectionContext,
                enableAdvancedProtection) == ProcessProtectionMatch.None)
            .GroupBy(snapshot => NormalizeProcessName(snapshot.ProcessName), StringComparer.OrdinalIgnoreCase)
            .Select(group => CreateCandidate(group.ToArray()))
            .Where(candidate => candidate.WorkingSetBytes >= MinimumGroupWorkingSetBytes)
            .OrderByDescending(candidate => candidate.IsDefaultSelected)
            .ThenByDescending(candidate => candidate.WorkingSetBytes)
            .ThenBy(candidate => candidate.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ExtremeCloseCandidate CreateCandidate(IReadOnlyList<ProcessSnapshot> snapshots)
    {
        var workingSetBytes = snapshots.Sum(snapshot => Math.Max(0L, snapshot.WorkingSetBytes));
        var cpuUsagePercent = snapshots.Sum(snapshot => Math.Max(0d, snapshot.CpuUsagePercent));
        var ioBytesPerSecond = snapshots.Sum(snapshot => Math.Max(0d, snapshot.IoBytesPerSecond));
        var hasForegroundProcess = snapshots.Any(snapshot => snapshot.IsForeground);
        var hasVisibleWindow = snapshots.Any(snapshot => snapshot.HasVisibleWindow);
        var isActive = cpuUsagePercent >= ActiveCpuPercent || ioBytesPerSecond >= ActiveIoBytesPerSecond;

        return new ExtremeCloseCandidate(
            snapshots[0].ProcessName,
            snapshots.Select(snapshot => snapshot.ProcessId).Distinct().ToArray(),
            workingSetBytes,
            cpuUsagePercent,
            ioBytesPerSecond,
            hasForegroundProcess,
            hasVisibleWindow,
            IsDefaultSelected: !hasForegroundProcess && !isActive);
    }

    private static bool IsCurrentProcess(ProcessSnapshot snapshot, int? currentProcessId)
    {
        return currentProcessId.HasValue && snapshot.ProcessId == currentProcessId.Value;
    }

    private static string NormalizeProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return string.Empty;
        }

        var normalized = processName.Trim();
        return normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^4].ToLowerInvariant()
            : normalized.ToLowerInvariant();
    }

}
