using System.IO;
using FluxRAM.Core.Models;
using FluxRAM.Core.Services;

namespace FluxRAM.App.Configuration;

public static class ExtremeCloseCandidateFactory
{
    private const long MinimumBackgroundGroupWorkingSetBytes = 12L * 1024 * 1024;
    private const long MinimumVisibleGroupWorkingSetBytes = 32L * 1024 * 1024;
    private const long DefaultSelectionWorkingSetBytes = 96L * 1024 * 1024;
    private const double ActiveCpuPercent = 20d;
    private const double ActiveIoBytesPerSecond = 16d * 1024 * 1024;
    private const int MaximumCandidateCount = 40;

    public static IReadOnlyList<ExtremeCloseCandidate> FromSnapshots(
        IReadOnlyList<ProcessSnapshot> snapshots,
        IReadOnlyCollection<string>? protectedProcessNames = null,
        IReadOnlyCollection<string>? protectedProcessPaths = null,
        int? currentProcessId = null,
        bool enableAdvancedProtection = true,
        IReadOnlyDictionary<string, BackgroundActivityAssessment>? activityAssessments = null)
    {
        var protectionContext = ProcessProtectionMatcher.CreateContext(
            snapshots,
            protectedProcessNames,
            protectedProcessPaths);

        return ProcessApplicationFamilyGrouper.Group(snapshots)
            .Where(family => family.Processes.All(snapshot => !IsCurrentProcess(snapshot, currentProcessId)))
            .Where(family => family.Processes.All(snapshot => !SystemProcessWhitelist.Contains(snapshot.ProcessName)))
            .Where(family => family.Processes.All(snapshot => !GamingProcessProtectionCatalog.Contains(snapshot.ProcessName)))
            .Where(family => family.Processes.All(snapshot => ProcessProtectionMatcher.Match(
                snapshot,
                protectionContext,
                enableAdvancedProtection) == ProcessProtectionMatch.None))
            .Select(family => CreateCandidate(family, activityAssessments))
            .Where(candidate => candidate.WorkingSetBytes >= (candidate.HasVisibleWindow
                ? MinimumVisibleGroupWorkingSetBytes
                : MinimumBackgroundGroupWorkingSetBytes))
            .OrderBy(candidate => ActivitySortOrder(candidate.ActivityState))
            .ThenByDescending(candidate => candidate.WorkingSetBytes)
            .ThenBy(candidate => candidate.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumCandidateCount)
            .ToArray();
    }

    private static ExtremeCloseCandidate CreateCandidate(
        ProcessApplicationFamily family,
        IReadOnlyDictionary<string, BackgroundActivityAssessment>? activityAssessments)
    {
        var snapshots = family.Processes;
        var workingSetBytes = snapshots.Sum(snapshot => Math.Max(0L, snapshot.WorkingSetBytes));
        var cpuUsagePercent = snapshots.Sum(snapshot => Math.Max(0d, snapshot.CpuUsagePercent));
        var ioBytesPerSecond = snapshots.Sum(snapshot => Math.Max(0d, snapshot.IoBytesPerSecond));
        var hasForegroundProcess = snapshots.Any(snapshot => snapshot.IsForeground);
        var hasVisibleWindow = snapshots.Any(snapshot => snapshot.HasVisibleWindow);
        var isActive = cpuUsagePercent >= ActiveCpuPercent || ioBytesPerSecond >= ActiveIoBytesPerSecond;
        var activity = ResolveActivityAssessment(
            family,
            activityAssessments,
            hasForegroundProcess,
            hasVisibleWindow,
            isActive);

        return new ExtremeCloseCandidate(
            family.DisplayName,
            snapshots.Select(snapshot => snapshot.ProcessId).Distinct().ToArray(),
            workingSetBytes,
            cpuUsagePercent,
            ioBytesPerSecond,
            hasForegroundProcess,
            hasVisibleWindow,
            IsDefaultSelected:
                workingSetBytes >= DefaultSelectionWorkingSetBytes &&
                !hasForegroundProcess &&
                !hasVisibleWindow &&
                !isActive &&
                activity.State == BackgroundActivityState.Idle,
            activity.State,
            activity.ObservedFor,
            activity.IdleFor);
    }

    private static BackgroundActivityAssessment ResolveActivityAssessment(
        ProcessApplicationFamily family,
        IReadOnlyDictionary<string, BackgroundActivityAssessment>? activityAssessments,
        bool hasForegroundProcess,
        bool hasVisibleWindow,
        bool isActive)
    {
        if (activityAssessments is not null &&
            activityAssessments.TryGetValue(family.Key, out var assessment))
        {
            return assessment;
        }

        var state = hasForegroundProcess || hasVisibleWindow
            ? BackgroundActivityState.Visible
            : isActive
                ? BackgroundActivityState.Working
                : BackgroundActivityState.Observing;
        return new BackgroundActivityAssessment(
            family.Key,
            state,
            TimeSpan.Zero,
            TimeSpan.Zero,
            0);
    }

    private static int ActivitySortOrder(BackgroundActivityState state)
    {
        return state switch
        {
            BackgroundActivityState.Idle => 0,
            BackgroundActivityState.Observing => 1,
            BackgroundActivityState.Working => 2,
            BackgroundActivityState.Visible => 3,
            _ => 4
        };
    }

    private static bool IsCurrentProcess(ProcessSnapshot snapshot, int? currentProcessId)
    {
        return currentProcessId.HasValue && snapshot.ProcessId == currentProcessId.Value;
    }

}
