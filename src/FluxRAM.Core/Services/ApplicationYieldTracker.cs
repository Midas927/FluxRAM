using FluxRAM.Core.Models;

namespace FluxRAM.Core.Services;

// Caller-serialized and clock-driven: Observe receives the existing monitoring loop's snapshots.
public sealed class ApplicationYieldTracker
{
    public const int MaximumReportCount = 128;
    public const int MaximumTrackedApplications = 128;
    public const int MaximumProcessesPerWindow = 256;
    public const long LowYieldThresholdBytes = 16L * 1024 * 1024;
    public static readonly TimeSpan SampleTolerance = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan BackoffDuration = TimeSpan.FromMinutes(10);

    private static readonly int[] CheckpointSeconds = { 30, 60, 120 };
    private readonly List<Window> _windows = new();
    private readonly Dictionary<string, AppHistory> _history = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastTimestamp = DateTimeOffset.MinValue;

    public IReadOnlyList<ApplicationYieldReport> Reports => _windows.Select(window => window.Report).ToArray();

    public void Record(
        PurgeCandidateGroup group,
        IReadOnlyList<(ProcessSnapshot Snapshot, MemoryPurgeResult Result)> results,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(results);
        if (!AcceptTimestamp(now))
        {
            return;
        }

        var successes = results.Where(item => item.Result.Success).ToArray();
        var key = GetApplicationKey(successes.Select(item => item.Snapshot).ToArray(), now)
            ?? GetApplicationKey(group.Processes, now);
        foreach (var window in _windows.Where(window => window.Report.CompletedAt is null))
        {
            if (now > window.Report.StartedAt.AddSeconds(120) + SampleTolerance)
            {
                FinishUnobserved(window, now);
            }
            else if ((key is not null && string.Equals(key, window.Report.ApplicationKey, StringComparison.OrdinalIgnoreCase)) ||
                results.Any(item => window.Targets.Any(target => SameProcess(target, item.Snapshot))))
            {
                // A second trim changes the measured baseline; retain the old window but do not score it.
                window.Invalid = true;
            }
        }

        if (_windows.Count == MaximumReportCount)
        {
            var completedIndex = _windows.FindIndex(window => window.Report.CompletedAt is not null);
            if (completedIndex < 0)
            {
                return;
            }

            _windows.RemoveAt(completedIndex);
        }

        var measured = successes.Length <= MaximumProcessesPerWindow
            ? successes.Where(item =>
                item.Result.ProcessId == item.Snapshot.ProcessId &&
                HasIdentity(item.Snapshot, now) &&
                item.Result.HasMeasurement &&
                item.Result.BeforeWorkingSetBytes >= 0 && item.Result.AfterWorkingSetBytes >= 0 &&
                group.Processes.Any(target => SameProcess(target, item.Snapshot))).ToArray()
            : Array.Empty<(ProcessSnapshot Snapshot, MemoryPurgeResult Result)>();
        var valid = key is not null && measured.Length > 0 && measured.Length == successes.Length &&
            measured.Select(item => item.Snapshot.ProcessId).Distinct().Count() == measured.Length;
        var before = valid ? ToBytes(measured.Sum(item => (decimal)item.Result.BeforeWorkingSetBytes)) : null;
        var after = valid ? ToBytes(measured.Sum(item => (decimal)item.Result.AfterWorkingSetBytes)) : null;
        var history = key is not null ? _history.GetValueOrDefault(key) : null;
        var report = new ApplicationYieldReport(
            key, group.ProcessName, now, null, group.Processes.Count,
            measured.Select(item => item.Snapshot.ProcessId).Distinct().Count(), before, after,
            Array.AsReadOnly(CheckpointSeconds.Select(seconds => new ApplicationYieldCheckpoint(
                seconds, null, null, null, null, ApplicationYieldSampleStatus.Pending)).ToArray()),
            ApplicationYieldStatus.Observing, null, history?.BadRunCount ?? 0,
            ActiveDeferral(history, now));
        _windows.Add(new Window(report, measured.Select(item => item.Snapshot).ToArray())
        {
            Invalid = !before.HasValue || !after.HasValue
        });
    }

    public void Observe(IReadOnlyList<ProcessSnapshot> snapshots, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        if (!AcceptTimestamp(now))
        {
            return;
        }

        var byId = snapshots.GroupBy(snapshot => snapshot.ProcessId)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var window in _windows.Where(window => window.Report.CompletedAt is null))
        {
            if (now <= window.LastObservedAt)
            {
                continue;
            }

            window.LastObservedAt = now;
            var workingSet = window.Invalid ? null : ReadWorkingSet(window, byId, now);
            window.Invalid |= !workingSet.HasValue;
            var checkpoints = window.Report.Checkpoints.ToArray();
            for (var index = 0; index < checkpoints.Length; index++)
            {
                var checkpoint = checkpoints[index];
                var due = window.Report.StartedAt.AddSeconds(checkpoint.ElapsedSeconds);
                if (checkpoint.Status != ApplicationYieldSampleStatus.Pending || now < due)
                {
                    continue;
                }

                var onTime = now <= due + SampleTolerance;
                var value = onTime ? workingSet : null;
                var immediate = window.Report.BeforeWorkingSetBytes - window.Report.AfterWorkingSetBytes;
                checkpoints[index] = checkpoint with
                {
                    ObservedAt = onTime ? now : null,
                    WorkingSetBytes = value,
                    RetainedBytes = value.HasValue ? Math.Max(0L, window.Report.BeforeWorkingSetBytes!.Value - value.Value) : null,
                    ReconstructedPercent = value.HasValue && immediate > 0
                        ? Math.Clamp((value.Value - window.Report.AfterWorkingSetBytes!.Value) / (double)immediate.Value * 100d, 0d, 100d)
                        : null,
                    Status = value.HasValue ? ApplicationYieldSampleStatus.Measured : ApplicationYieldSampleStatus.Unknown
                };
            }

            window.Report = window.Report with { Checkpoints = Array.AsReadOnly(checkpoints) };
            if (checkpoints[^1].Status != ApplicationYieldSampleStatus.Pending)
            {
                Finish(window, now);
            }
        }
    }

    public bool HasPendingObservation(PurgeCandidateGroup group, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(group);
        var key = GetApplicationKey(group.Processes, now);
        return key is not null && _windows.Any(window =>
            !window.Invalid && window.Report.CompletedAt is null &&
            string.Equals(key, window.Report.ApplicationKey, StringComparison.OrdinalIgnoreCase) &&
            window.Targets.All(target => group.Processes.Any(current => SameProcess(target, current))) &&
            now >= window.Report.StartedAt && now <= window.Report.StartedAt.AddSeconds(120) + SampleTolerance &&
            window.Report.Checkpoints.All(checkpoint => checkpoint.Status != ApplicationYieldSampleStatus.Unknown));
    }

    // Advisory for automatic passes only; manual/severe-pressure bypass belongs to the caller.
    public bool IsDeferred(PurgeCandidateGroup group, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(group);
        var key = GetApplicationKey(group.Processes, now);
        return key is not null && _history.TryGetValue(key, out var history) &&
            now >= history.UpdatedAt && ActiveDeferral(history, now).HasValue;
    }

    private void FinishUnobserved(Window window, DateTimeOffset now)
    {
        window.Invalid = true;
        window.Report = window.Report with
        {
            Checkpoints = Array.AsReadOnly(window.Report.Checkpoints.Select(checkpoint =>
                checkpoint.Status == ApplicationYieldSampleStatus.Pending
                    ? checkpoint with { Status = ApplicationYieldSampleStatus.Unknown }
                    : checkpoint).ToArray())
        };
        Finish(window, now);
    }

    private void Finish(Window window, DateTimeOffset now)
    {
        var report = window.Report;
        var key = report.ApplicationKey;
        var history = key is not null ? _history.GetValueOrDefault(key) : null;
        var valid = !window.Invalid && key is not null &&
            report.Checkpoints.All(checkpoint => checkpoint.Status == ApplicationYieldSampleStatus.Measured);
        bool? lowYield = null;
        if (valid)
        {
            var finalSample = report.Checkpoints[^1];
            lowYield = report.BeforeWorkingSetBytes <= report.AfterWorkingSetBytes ||
                finalSample.RetainedBytes < LowYieldThresholdBytes;
            var badRun = lowYield.Value || finalSample.ReconstructedPercent >= 80d;
            var count = badRun ? Math.Min(2, (history?.BadRunCount ?? 0) + 1) : 0;
            var deferredUntil = ActiveDeferral(history, now);
            if (count >= 2)
            {
                deferredUntil = now + BackoffDuration;
            }

            history = new AppHistory(count, deferredUntil, now);
            if (count == 0 && deferredUntil is null)
            {
                _history.Remove(key!);
            }
            else
            {
                if (!_history.ContainsKey(key!) && _history.Count == MaximumTrackedApplications)
                {
                    var oldest = _history.Where(pair => !ActiveDeferral(pair.Value, now).HasValue)
                        .OrderBy(pair => pair.Value.UpdatedAt).FirstOrDefault().Key;
                    if (oldest is not null)
                    {
                        _history.Remove(oldest);
                    }
                }

                // ponytail: at capacity, retain live deferrals over new identities; raise the budget if needed.
                if (_history.ContainsKey(key!) || _history.Count < MaximumTrackedApplications)
                {
                    _history[key!] = history;
                }
            }
        }

        window.Report = report with
        {
            CompletedAt = now,
            Status = valid ? ApplicationYieldStatus.Completed : ApplicationYieldStatus.Unknown,
            IsLowYield = lowYield,
            ConsecutiveLowYieldCount = history?.BadRunCount ?? 0,
            DeferredUntil = ActiveDeferral(history, now)
        };
        window.Targets = Array.Empty<ProcessSnapshot>();
    }

    private static long? ReadWorkingSet(Window window, IReadOnlyDictionary<int, ProcessSnapshot> snapshots, DateTimeOffset now)
    {
        decimal total = 0;
        foreach (var target in window.Targets)
        {
            if (!snapshots.TryGetValue(target.ProcessId, out var current) ||
                !HasIdentity(current, now) || !SameProcess(target, current) ||
                !current.HasWorkingSetMeasurement || current.WorkingSetBytes < 0)
            {
                return null;
            }

            total += current.WorkingSetBytes;
        }

        return ToBytes(total);
    }

    private static string? GetApplicationKey(IReadOnlyList<ProcessSnapshot> snapshots, DateTimeOffset now)
    {
        if (snapshots.Count == 0 || snapshots.Any(snapshot => !HasIdentity(snapshot, now)) ||
            snapshots.Select(snapshot => snapshot.ProcessId).Distinct().Count() != snapshots.Count)
        {
            return null;
        }

        var families = ProcessApplicationFamilyGrouper.Group(snapshots);
        return families.Count == 1 ? families[0].Key : null;
    }

    private static bool HasIdentity(ProcessSnapshot snapshot, DateTimeOffset now) =>
        ProcessIdentity.IsKnown(snapshot) && snapshot.StartTimeUtc <= now;

    private static bool SameProcess(ProcessSnapshot left, ProcessSnapshot right) =>
        ProcessIdentity.Matches(left, right);

    private bool AcceptTimestamp(DateTimeOffset now)
    {
        if (now < _lastTimestamp)
        {
            return false;
        }

        _lastTimestamp = now;
        return true;
    }

    private static long? ToBytes(decimal value) => value >= 0 && value <= long.MaxValue ? (long)value : null;

    private static DateTimeOffset? ActiveDeferral(AppHistory? history, DateTimeOffset now) =>
        history?.DeferredUntil > now ? history.DeferredUntil : null;

    private sealed record AppHistory(int BadRunCount, DateTimeOffset? DeferredUntil, DateTimeOffset UpdatedAt);

    private sealed class Window(ApplicationYieldReport report, ProcessSnapshot[] targets)
    {
        public ApplicationYieldReport Report { get; set; } = report;
        public ProcessSnapshot[] Targets { get; set; } = targets;
        public DateTimeOffset LastObservedAt { get; set; } = report.StartedAt;
        public bool Invalid { get; set; }
    }
}
