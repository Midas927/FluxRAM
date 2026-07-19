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

        var orderedCandidates = snapshots
            .Where(snapshot => settings.AllowForegroundProcessPurge || !snapshot.IsForeground)
            .Where(snapshot => snapshot.WorkingSetBytes >= settings.MinimumCandidateWorkingSetBytes)
            .Where(snapshot => snapshot.ColdnessScore >= settings.MinimumColdnessScore)
            .Where(snapshot => !IsActivityRiskTooHigh(snapshot, settings))
            .Where(snapshot => ProcessProtectionMatcher.Match(
                snapshot,
                protectionContext,
                enableAdvancedProtection) == ProcessProtectionMatch.None)
            .Where(snapshot => !IsInCooldown(snapshot.ProcessId, now, cooldown, lastPurgeTimesByProcessId))
            .OrderByDescending(CalculateCandidatePriorityScore)
            .ThenByDescending(snapshot => snapshot.WorkingSetBytes)
            .ThenByDescending(snapshot => snapshot.ColdnessScore)
            .ToArray();

        var effectiveCandidateLimit = CalculateEffectiveCandidateLimit(
            settings,
            memorySnapshot,
            effectiveThreshold,
            orderedCandidates.Length);
        var candidates = effectiveCandidateLimit <= 0
            ? orderedCandidates
            : orderedCandidates
                .Take(effectiveCandidateLimit)
                .ToArray();

        if (candidates.Length == 0)
        {
            return new PurgePlan(
                false,
                BuildNoEligibleProcessMessage(
                    snapshots,
                    settings,
                    now,
                    cooldown,
                    protectionContext,
                    lastPurgeTimesByProcessId,
                    enableAdvancedProtection),
                candidates,
                protectionSummary);
        }

        return new PurgePlan(
            true,
            forcePurge
                ? $"Boost Now plan with {candidates.Length} candidate(s)."
                : shouldBypassThreshold
                    ? $"Extreme bypassed threshold with {candidates.Length} candidate(s)."
                : $"Purge plan ready with {candidates.Length} candidate(s), coldness >= {settings.MinimumColdnessScore:0}.",
            candidates,
            protectionSummary);
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
        OptimizerSettings settings,
        DateTimeOffset now,
        TimeSpan cooldown,
        ProcessProtectionContext protectionContext,
        IReadOnlyDictionary<int, DateTimeOffset> lastPurgeTimesByProcessId,
        bool enableAdvancedProtection)
    {
        if (snapshots.Count == 0)
        {
            return "No eligible process met safety criteria: no user processes could be scanned.";
        }

        var foreground = 0;
        var tooSmall = 0;
        var notCold = 0;
        var active = 0;
        var protectedCount = 0;
        var cooldownCount = 0;

        foreach (var snapshot in snapshots)
        {
            if (!settings.AllowForegroundProcessPurge && snapshot.IsForeground)
            {
                foreground += 1;
                continue;
            }

            if (snapshot.WorkingSetBytes < settings.MinimumCandidateWorkingSetBytes)
            {
                tooSmall += 1;
                continue;
            }

            if (snapshot.ColdnessScore < settings.MinimumColdnessScore)
            {
                notCold += 1;
                continue;
            }

            if (IsActivityRiskTooHigh(snapshot, settings))
            {
                active += 1;
                continue;
            }

            if (ProcessProtectionMatcher.Match(
                snapshot,
                protectionContext,
                enableAdvancedProtection) != ProcessProtectionMatch.None)
            {
                protectedCount += 1;
                continue;
            }

            if (IsInCooldown(snapshot.ProcessId, now, cooldown, lastPurgeTimesByProcessId))
            {
                cooldownCount += 1;
            }
        }

        var reasons = new List<string>();
        AddReason(reasons, foreground, "foreground");
        AddReason(reasons, tooSmall, "below size threshold");
        AddReason(reasons, notCold, "not cold enough");
        AddReason(reasons, active, "active CPU/I/O");
        AddReason(reasons, protectedCount, "protected");
        AddReason(reasons, cooldownCount, "cooldown");

        return reasons.Count == 0
            ? "No eligible process met safety criteria: no safe background candidate remained."
            : $"No eligible process met safety criteria: {string.Join(", ", reasons)}.";
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

}
