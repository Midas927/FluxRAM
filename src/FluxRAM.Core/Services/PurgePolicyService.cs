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
        if (!forcePurge && !shouldBypassThreshold && memorySnapshot.AvailablePhysicalMemoryBytes >= effectiveThreshold)
        {
            return new PurgePlan(
                false,
                "Memory pressure is low; purge skipped.",
                Array.Empty<ProcessSnapshot>());
        }

        var cooldown = TimeSpan.FromSeconds(settings.ProcessCooldownSeconds);
        var protectedNames = BuildProtectedNameSet(protectedProcessNames);
        var protectedPaths = BuildProtectedPathSet(protectedProcessPaths);
        var protectedTitleTokens = enableAdvancedProtection
            ? BuildProtectedTitleTokens(protectedNames, protectedPaths)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var protectedRootProcessIds = enableAdvancedProtection
            ? BuildProtectedRootProcessIds(snapshots, protectedNames, protectedPaths)
            : new HashSet<int>();
        var parentProcessIds = snapshots.ToDictionary(
            snapshot => snapshot.ProcessId,
            snapshot => snapshot.ParentProcessId);

        var orderedCandidates = snapshots
            .Where(snapshot => settings.AllowForegroundProcessPurge || !snapshot.IsForeground)
            .Where(snapshot => snapshot.WorkingSetBytes >= settings.MinimumCandidateWorkingSetBytes)
            .Where(snapshot => snapshot.ColdnessScore >= settings.MinimumColdnessScore)
            .Where(snapshot => !IsActivityRiskTooHigh(snapshot, settings))
            .Where(snapshot => !IsProtectedSnapshot(
                snapshot,
                protectedNames,
                protectedPaths,
                protectedTitleTokens,
                protectedRootProcessIds,
                parentProcessIds,
                enableAdvancedProtection))
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
                "No eligible process met safety criteria.",
                candidates);
        }

        return new PurgePlan(
            true,
            forcePurge
                ? $"Boost Now plan with {candidates.Length} candidate(s)."
                : shouldBypassThreshold
                    ? $"Extreme Performance bypassed threshold with {candidates.Length} candidate(s)."
                : $"Purge plan ready with {candidates.Length} candidate(s), coldness >= {settings.MinimumColdnessScore:0}.",
            candidates);
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

    private static HashSet<string> BuildProtectedPathSet(IReadOnlyCollection<string>? protectedProcessPaths)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (protectedProcessPaths is null)
        {
            return normalized;
        }

        foreach (var processPath in protectedProcessPaths)
        {
            var normalizedPath = NormalizePath(processPath);
            if (normalizedPath.Length > 0)
            {
                normalized.Add(normalizedPath);
            }
        }

        return normalized;
    }

    private static HashSet<int> BuildProtectedRootProcessIds(
        IReadOnlyList<ProcessSnapshot> snapshots,
        IReadOnlySet<string> protectedNames,
        IReadOnlySet<string> protectedPaths)
    {
        return snapshots
            .Where(snapshot =>
                protectedNames.Contains(NormalizeProcessName(snapshot.ProcessName)) ||
                IsProtectedByPath(snapshot.ExecutablePath, protectedPaths))
            .Select(snapshot => snapshot.ProcessId)
            .ToHashSet();
    }

    private static HashSet<string> BuildProtectedTitleTokens(
        IReadOnlySet<string> protectedNames,
        IReadOnlySet<string> protectedPaths)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var protectedName in protectedNames)
        {
            AddTitleToken(tokens, protectedName);
        }

        foreach (var protectedPath in protectedPaths)
        {
            AddTitleToken(tokens, Path.GetFileNameWithoutExtension(protectedPath));
            var directoryName = Path.GetFileName(Path.GetDirectoryName(protectedPath));
            AddTitleToken(tokens, directoryName);
        }

        return tokens;
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

    private static bool IsProtectedByPath(string? executablePath, IReadOnlySet<string> protectedPaths)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        return protectedPaths.Contains(NormalizePath(executablePath));
    }

    private static bool IsProtectedSnapshot(
        ProcessSnapshot snapshot,
        IReadOnlySet<string> protectedNames,
        IReadOnlySet<string> protectedPaths,
        IReadOnlySet<string> protectedTitleTokens,
        IReadOnlySet<int> protectedRootProcessIds,
        IReadOnlyDictionary<int, int?> parentProcessIds,
        bool enableAdvancedProtection)
    {
        if (protectedNames.Contains(NormalizeProcessName(snapshot.ProcessName)))
        {
            return true;
        }

        if (!enableAdvancedProtection)
        {
            return false;
        }

        return
            IsProtectedByPath(snapshot.ExecutablePath, protectedPaths) ||
            IsDescendantOfProtectedRoot(snapshot, protectedRootProcessIds, parentProcessIds) ||
            (snapshot.HasVisibleWindow && IsProtectedByWindowTitle(snapshot.MainWindowTitle, protectedTitleTokens));
    }

    private static bool IsDescendantOfProtectedRoot(
        ProcessSnapshot snapshot,
        IReadOnlySet<int> protectedRootProcessIds,
        IReadOnlyDictionary<int, int?> parentProcessIds)
    {
        var seenProcessIds = new HashSet<int> { snapshot.ProcessId };
        var parentProcessId = snapshot.ParentProcessId;
        for (var depth = 0; depth < 16 && parentProcessId.HasValue; depth += 1)
        {
            if (protectedRootProcessIds.Contains(parentProcessId.Value))
            {
                return true;
            }

            if (!seenProcessIds.Add(parentProcessId.Value))
            {
                return false;
            }

            parentProcessId = parentProcessIds.TryGetValue(parentProcessId.Value, out var nextParentProcessId)
                ? nextParentProcessId
                : null;
        }

        return false;
    }

    private static bool IsProtectedByWindowTitle(string? mainWindowTitle, IReadOnlySet<string> protectedTitleTokens)
    {
        if (string.IsNullOrWhiteSpace(mainWindowTitle) || protectedTitleTokens.Count == 0)
        {
            return false;
        }

        var normalizedTitle = NormalizeSearchToken(mainWindowTitle);
        return protectedTitleTokens.Any(token => normalizedTitle.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddTitleToken(ISet<string> tokens, string? rawToken)
    {
        var token = NormalizeSearchToken(rawToken);
        if (token.Length >= 4 && !IsGenericTitleToken(token))
        {
            tokens.Add(token);
        }
    }

    private static bool IsGenericTitleToken(string token)
    {
        return token is "app" or "game" or "client" or "helper" or "launcher" or "setup" or "update" or "updater";
    }

    private static string NormalizeSearchToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();
        return new string(chars);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.Trim().Replace('/', '\\').ToLowerInvariant();
    }
}
