using FluxRAM.Core.Models;

namespace FluxRAM.Core.Services;

public sealed class PurgePolicyService
{
    private const double StrictCpuActivityRiskPercent = 8d;
    private const double HardCpuActivityRiskPercent = 25d;
    private const double StrictIoActivityRiskBytesPerSecond = 4d * 1024 * 1024;
    private const double HardIoActivityRiskBytesPerSecond = 16d * 1024 * 1024;
    private const int MaxAdaptiveTargetLimit = 12;
    public PurgePlan CreatePlan(
        IReadOnlyList<ProcessSnapshot> snapshots,
        MemorySnapshot memorySnapshot,
        OptimizerSettings settings,
        DateTimeOffset now,
        IReadOnlyDictionary<int, DateTimeOffset> lastPurgeTimesByProcessId,
        bool forcePurge = false,
        IReadOnlyCollection<string>? protectedProcessNames = null,
        IReadOnlyCollection<string>? protectedProcessPaths = null,
        bool enableAdvancedProtection = true)
    {
        var shouldBypassThreshold = settings.IgnoreMemoryPressureThreshold;
        var effectiveThreshold = CalculateEffectiveThreshold(memorySnapshot, settings);
        var protectedNames = BuildProtectedNameSet(protectedProcessNames);
        if (settings.EnableGamingProcessProtection)
        {
            protectedNames.UnionWith(GamingProcessProtectionCatalog.ProcessNames);
        }

        var protectionContext = ProcessProtectionMatcher.CreateContext(
            snapshots,
            protectedNames,
            protectedProcessPaths);
        var protectionSummary = ProcessProtectionMatcher.Summarize(
            snapshots,
            protectionContext,
            enableAdvancedProtection);

        if (!forcePurge && !shouldBypassThreshold && memorySnapshot.AvailablePhysicalMemoryBytes >= effectiveThreshold)
        {
            return new PurgePlan(
                false,
                $"Memory pressure is low; purge skipped. Available {FormatBytes(memorySnapshot.AvailablePhysicalMemoryBytes)} is above threshold {FormatBytes(effectiveThreshold)}.",
                Array.Empty<ProcessSnapshot>(),
                protectionSummary);
        }

        var cooldown = TimeSpan.FromSeconds(settings.ProcessCooldownSeconds);

        var assessedGroups = BuildCandidateGroups(snapshots, settings)
            .Select(group => new CandidateGroupAssessment(
                group,
                GetRejectionReason(
                    group,
                    settings,
                    now,
                    cooldown,
                    protectionContext,
                    lastPurgeTimesByProcessId,
                    enableAdvancedProtection)))
            .ToArray();
        var orderedGroups = assessedGroups
            .Where(assessment => assessment.RejectionReason == CandidateGroupRejectionReason.None)
            .Select(assessment => assessment.Group)
            .OrderByDescending(group => CalculateCandidatePriorityScore(ToAggregateSnapshot(group)))
            .ThenByDescending(group => group.WorkingSetBytes)
            .ThenByDescending(group => group.ColdnessScore)
            .ToArray();

        var effectiveCandidateLimit = CalculateEffectiveCandidateLimit(
            settings,
            memorySnapshot,
            effectiveThreshold,
            orderedGroups.Length);
        var candidateGroups = effectiveCandidateLimit <= 0
            ? orderedGroups
            : orderedGroups
                .Take(effectiveCandidateLimit)
                .ToArray();
        var candidates = candidateGroups
            .SelectMany(group => group.Processes)
            .ToArray();

        if (candidates.Length == 0)
        {
            return new PurgePlan(
                false,
                BuildNoEligibleProcessMessage(snapshots, assessedGroups),
                candidates,
                protectionSummary,
                Array.Empty<PurgeCandidateGroup>());
        }

        return new PurgePlan(
            true,
            forcePurge
                ? $"Boost Now plan with {candidateGroups.Length} application(s), {candidates.Length} process(es)."
                : shouldBypassThreshold
                    ? $"Extreme bypassed threshold with {candidateGroups.Length} application(s), {candidates.Length} process(es)."
                : $"Purge plan ready with {candidateGroups.Length} application(s), {candidates.Length} process(es), coldness >= {settings.MinimumColdnessScore:0}.",
            candidates,
            protectionSummary,
            candidateGroups);
    }

    private static IReadOnlyList<PurgeCandidateGroup> BuildCandidateGroups(
        IReadOnlyList<ProcessSnapshot> snapshots,
        OptimizerSettings settings)
    {
        var minimumProcessWorkingSetBytes = Math.Max(1L, settings.MinimumGroupedProcessWorkingSetBytes);
        return ProcessApplicationFamilyGrouper.Group(snapshots)
            .Select(family => CreateCandidateGroup(family, minimumProcessWorkingSetBytes))
            .ToArray();
    }

    private static PurgeCandidateGroup CreateCandidateGroup(
        ProcessApplicationFamily family,
        long minimumProcessWorkingSetBytes)
    {
        var observedProcesses = family.Processes.ToArray();
        var targetProcesses = observedProcesses
            .Where(snapshot => snapshot.WorkingSetBytes >= minimumProcessWorkingSetBytes)
            .OrderByDescending(snapshot => snapshot.WorkingSetBytes)
            .ThenBy(snapshot => snapshot.ProcessId)
            .ToArray();
        var workingSetBytes = observedProcesses.Sum(snapshot => Math.Max(0L, snapshot.WorkingSetBytes));
        var cpuUsagePercent = observedProcesses.Sum(snapshot => Math.Max(0d, snapshot.CpuUsagePercent));
        var ioBytesPerSecond = observedProcesses.Sum(snapshot => Math.Max(0d, snapshot.IoBytesPerSecond));

        return new PurgeCandidateGroup(
            family.DisplayName,
            observedProcesses.Select(snapshot => snapshot.ExecutablePath).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path)),
            targetProcesses,
            observedProcesses,
            workingSetBytes,
            cpuUsagePercent,
            ioBytesPerSecond,
            CalculateWeightedColdness(observedProcesses),
            observedProcesses.Any(snapshot => snapshot.IsForeground),
            observedProcesses.Any(snapshot => snapshot.HasVisibleWindow));
    }

    private static double CalculateWeightedColdness(IReadOnlyList<ProcessSnapshot> snapshots)
    {
        var totalWeight = snapshots.Sum(snapshot => Math.Max(1L, snapshot.WorkingSetBytes));
        if (totalWeight <= 0)
        {
            return 0d;
        }

        var weightedScore = snapshots.Sum(snapshot =>
            snapshot.ColdnessScore * Math.Max(1L, snapshot.WorkingSetBytes));
        return Math.Clamp(weightedScore / totalWeight, 0d, 100d);
    }

    private static CandidateGroupRejectionReason GetRejectionReason(
        PurgeCandidateGroup group,
        OptimizerSettings settings,
        DateTimeOffset now,
        TimeSpan cooldown,
        ProcessProtectionContext protectionContext,
        IReadOnlyDictionary<int, DateTimeOffset> lastPurgeTimesByProcessId,
        bool enableAdvancedProtection)
    {
        if (!settings.AllowForegroundProcessPurge && group.HasForegroundProcess)
        {
            return CandidateGroupRejectionReason.Foreground;
        }

        if (group.WorkingSetBytes < settings.MinimumCandidateWorkingSetBytes || group.Processes.Count == 0)
        {
            return CandidateGroupRejectionReason.TooSmall;
        }

        if (group.ColdnessScore < settings.MinimumColdnessScore)
        {
            return CandidateGroupRejectionReason.NotCold;
        }

        if (IsActivityRiskTooHigh(ToAggregateSnapshot(group), settings))
        {
            return CandidateGroupRejectionReason.Active;
        }

        if (group.ObservedProcesses.Any(snapshot => ProcessProtectionMatcher.Match(
                snapshot,
                protectionContext,
                enableAdvancedProtection) != ProcessProtectionMatch.None))
        {
            return CandidateGroupRejectionReason.Protected;
        }

        return group.Processes.Any(snapshot => IsInCooldown(
                snapshot.ProcessId,
                now,
                cooldown,
                lastPurgeTimesByProcessId))
            ? CandidateGroupRejectionReason.Cooldown
            : CandidateGroupRejectionReason.None;
    }

    private static ProcessSnapshot ToAggregateSnapshot(PurgeCandidateGroup group)
    {
        return new ProcessSnapshot(
            group.Processes.FirstOrDefault().ProcessId,
            group.ProcessName,
            group.WorkingSetBytes,
            group.HasForegroundProcess,
            group.CpuUsagePercent,
            group.HasVisibleWindow,
            group.ColdnessScore,
            group.ExecutablePath,
            group.IoBytesPerSecond);
    }

    private static HashSet<string> BuildProtectedNameSet(IReadOnlyCollection<string>? protectedProcessNames)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (protectedProcessNames is null)
        {
            return normalized;
        }

        foreach (var processName in protectedProcessNames)
        {
            var normalizedName = NormalizeProcessName(processName);
            if (normalizedName.Length > 0)
            {
                normalized.Add(normalizedName);
            }
        }

        return normalized;
    }

    private static ulong CalculateEffectiveThreshold(MemorySnapshot memorySnapshot, OptimizerSettings settings)
    {
        if (settings.IgnoreMemoryPressureThreshold)
        {
            return 0;
        }

        if (settings.PurgeWhenAvailableMemoryBelowPercentOfTotal <= 0)
        {
            return settings.PurgeWhenAvailableMemoryBelowBytes;
        }

        var percent = Math.Clamp(settings.PurgeWhenAvailableMemoryBelowPercentOfTotal, 1, 95);
        var total = memorySnapshot.TotalPhysicalMemoryBytes;
        var thresholdByPercent = (ulong)(total * (percent / 100d));

        if (settings.PurgeWhenAvailableMemoryBelowBytes == 0)
        {
            return thresholdByPercent;
        }

        return Math.Min(settings.PurgeWhenAvailableMemoryBelowBytes, thresholdByPercent);
    }

    private static int CalculateEffectiveCandidateLimit(
        OptimizerSettings settings,
        MemorySnapshot memorySnapshot,
        ulong effectiveThreshold,
        int candidateCount)
    {
        if (settings.MaxPurgeTargetsPerPass <= 0)
        {
            return 0;
        }

        var limit = settings.MaxPurgeTargetsPerPass;
        if (IsSevereMemoryPressure(memorySnapshot, effectiveThreshold, settings))
        {
            limit = Math.Min(
                Math.Max(settings.MaxPurgeTargetsPerPass * 2, settings.MaxPurgeTargetsPerPass + 2),
                MaxAdaptiveTargetLimit);
        }

        return Math.Min(limit, candidateCount);
    }

    private static bool IsSevereMemoryPressure(
        MemorySnapshot memorySnapshot,
        ulong effectiveThreshold,
        OptimizerSettings settings)
    {
        if (settings.IgnoreMemoryPressureThreshold || effectiveThreshold == 0)
        {
            return false;
        }

        return memorySnapshot.MemoryLoadPercent >= 90 &&
            memorySnapshot.AvailablePhysicalMemoryBytes <= effectiveThreshold / 2;
    }

    private static bool IsActivityRiskTooHigh(ProcessSnapshot snapshot, OptimizerSettings settings)
    {
        if (snapshot.CpuUsagePercent >= HardCpuActivityRiskPercent ||
            snapshot.IoBytesPerSecond >= HardIoActivityRiskBytesPerSecond)
        {
            return true;
        }

        if (settings.AllowForegroundProcessPurge)
        {
            return false;
        }

        if (snapshot.CpuUsagePercent >= StrictCpuActivityRiskPercent ||
            snapshot.IoBytesPerSecond >= StrictIoActivityRiskBytesPerSecond)
        {
            return true;
        }

        var visibleWindowColdnessFloor = Math.Max(settings.MinimumColdnessScore + 15d, 75d);
        return snapshot.HasVisibleWindow && snapshot.ColdnessScore < visibleWindowColdnessFloor;
    }

    private static double CalculateCandidatePriorityScore(ProcessSnapshot snapshot)
    {
        var workingSetMegabytes = Math.Max(0d, snapshot.WorkingSetBytes / (1024d * 1024d));
        var yieldScore = Math.Log(workingSetMegabytes + 1d, 2d) * 10d;
        var cpuPenalty = Math.Max(0d, snapshot.CpuUsagePercent) * 2.5d;
        var ioMegabytesPerSecond = Math.Max(0d, snapshot.IoBytesPerSecond / (1024d * 1024d));
        var ioPenalty = ioMegabytesPerSecond * 4d;
        var visibleWindowPenalty = snapshot.HasVisibleWindow ? 8d : 0d;

        return snapshot.ColdnessScore * 1.5d +
            yieldScore -
            cpuPenalty -
            ioPenalty -
            visibleWindowPenalty;
    }

    private static bool IsInCooldown(
        int processId,
        DateTimeOffset now,
        TimeSpan cooldown,
        IReadOnlyDictionary<int, DateTimeOffset> lastPurgeTimesByProcessId)
    {
        if (!lastPurgeTimesByProcessId.TryGetValue(processId, out var lastPurgeAt))
        {
            return false;
        }

        return now - lastPurgeAt < cooldown;
    }

    private static string BuildNoEligibleProcessMessage(
        IReadOnlyList<ProcessSnapshot> snapshots,
        IReadOnlyList<CandidateGroupAssessment> assessedGroups)
    {
        if (snapshots.Count == 0)
        {
            return "No eligible process met safety criteria: no user processes could be scanned.";
        }

        var reasons = new List<string>();
        AddReason(reasons, CountRejected(assessedGroups, CandidateGroupRejectionReason.Foreground), "foreground application(s)");
        AddReason(reasons, CountRejected(assessedGroups, CandidateGroupRejectionReason.TooSmall), "below size threshold");
        AddReason(reasons, CountRejected(assessedGroups, CandidateGroupRejectionReason.NotCold), "not cold enough");
        AddReason(reasons, CountRejected(assessedGroups, CandidateGroupRejectionReason.Active), "active CPU/I/O");
        AddReason(reasons, CountRejected(assessedGroups, CandidateGroupRejectionReason.Protected), "protected");
        AddReason(reasons, CountRejected(assessedGroups, CandidateGroupRejectionReason.Cooldown), "cooldown");

        return reasons.Count == 0
            ? "No eligible process met safety criteria: no safe background candidate remained."
            : $"No eligible process met safety criteria: {string.Join(", ", reasons)}.";
    }

    private static int CountRejected(
        IReadOnlyList<CandidateGroupAssessment> assessedGroups,
        CandidateGroupRejectionReason reason)
    {
        return assessedGroups.Count(assessment => assessment.RejectionReason == reason);
    }

    private static void AddReason(ICollection<string> reasons, int count, string label)
    {
        if (count > 0)
        {
            reasons.Add($"{count} {label}");
        }
    }

    private static string FormatBytes(ulong bytes)
    {
        return bytes >= 1024UL * 1024 * 1024
            ? $"{bytes / (1024d * 1024d * 1024d):0.0} GB"
            : $"{bytes / (1024d * 1024d):0.0} MB";
    }

    private static string NormalizeProcessName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return string.Empty;
        }

        var normalized = processName.Trim();
        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized.ToLowerInvariant();
    }

    private readonly record struct CandidateGroupAssessment(
        PurgeCandidateGroup Group,
        CandidateGroupRejectionReason RejectionReason);

    private enum CandidateGroupRejectionReason
    {
        None,
        Foreground,
        TooSmall,
        NotCold,
        Active,
        Protected,
        Cooldown
    }

}
