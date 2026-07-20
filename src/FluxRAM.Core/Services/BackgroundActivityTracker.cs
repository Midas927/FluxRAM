using FluxRAM.Core.Models;

namespace FluxRAM.Core.Services;

public sealed class BackgroundActivityTracker
{
    public static readonly TimeSpan MinimumObservationDuration = TimeSpan.FromSeconds(60);
    public const int MinimumSampleCount = 5;

    private const double ActiveCpuPercent = 1.5d;
    private const double ActiveIoBytesPerSecond = 256d * 1024d;
    private readonly Dictionary<string, ActivityHistory> _historyByFamilyKey =
        new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, BackgroundActivityAssessment> _currentAssessments =
        new Dictionary<string, BackgroundActivityAssessment>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, BackgroundActivityAssessment> CurrentAssessments =>
        _currentAssessments;

    public IReadOnlyDictionary<string, BackgroundActivityAssessment> Observe(
        IReadOnlyList<ProcessSnapshot> snapshots,
        DateTimeOffset observedAt)
    {
        var families = ProcessApplicationFamilyGrouper.Group(snapshots);
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assessments = new Dictionary<string, BackgroundActivityAssessment>(StringComparer.OrdinalIgnoreCase);

        foreach (var family in families)
        {
            seenKeys.Add(family.Key);
            var hasForegroundProcess = family.Processes.Any(snapshot => snapshot.IsForeground);
            var hasVisibleWindow = family.Processes.Any(snapshot => snapshot.HasVisibleWindow);
            var cpuUsagePercent = family.Processes.Sum(snapshot => Math.Max(0d, snapshot.CpuUsagePercent));
            var ioBytesPerSecond = family.Processes.Sum(snapshot => Math.Max(0d, snapshot.IoBytesPerSecond));
            var isActive = hasForegroundProcess ||
                hasVisibleWindow ||
                cpuUsagePercent >= ActiveCpuPercent ||
                ioBytesPerSecond >= ActiveIoBytesPerSecond;

            if (!_historyByFamilyKey.TryGetValue(family.Key, out var history))
            {
                history = new ActivityHistory(observedAt, observedAt, 0);
            }

            var lastActiveAt = isActive ? observedAt : history.LastActiveAt;
            var sampleCount = history.SampleCount + 1;
            history = history with
            {
                LastActiveAt = lastActiveAt,
                SampleCount = sampleCount
            };
            _historyByFamilyKey[family.Key] = history;

            var observedFor = NonNegative(observedAt - history.FirstObservedAt);
            var idleFor = NonNegative(observedAt - history.LastActiveAt);
            var state = Classify(
                hasForegroundProcess,
                hasVisibleWindow,
                isActive,
                observedFor,
                idleFor,
                sampleCount);
            assessments[family.Key] = new BackgroundActivityAssessment(
                family.Key,
                state,
                observedFor,
                idleFor,
                sampleCount);
        }

        foreach (var staleKey in _historyByFamilyKey.Keys.Where(key => !seenKeys.Contains(key)).ToArray())
        {
            _historyByFamilyKey.Remove(staleKey);
        }

        _currentAssessments = assessments;
        return assessments;
    }

    private static BackgroundActivityState Classify(
        bool hasForegroundProcess,
        bool hasVisibleWindow,
        bool isActive,
        TimeSpan observedFor,
        TimeSpan idleFor,
        int sampleCount)
    {
        if (hasForegroundProcess || hasVisibleWindow)
        {
            return BackgroundActivityState.Visible;
        }

        if (isActive)
        {
            return BackgroundActivityState.Working;
        }

        if (observedFor < MinimumObservationDuration || sampleCount < MinimumSampleCount)
        {
            return BackgroundActivityState.Observing;
        }

        return idleFor >= MinimumObservationDuration
            ? BackgroundActivityState.Idle
            : BackgroundActivityState.Working;
    }

    private static TimeSpan NonNegative(TimeSpan value)
    {
        return value < TimeSpan.Zero ? TimeSpan.Zero : value;
    }

    private sealed record ActivityHistory(
        DateTimeOffset FirstObservedAt,
        DateTimeOffset LastActiveAt,
        int SampleCount);
}
