using FluxRAM.Core.Models;
using FluxRAM.Core.Services;
using Xunit;

namespace FluxRAM.Core.Tests;

public sealed class ApplicationYieldTrackerTests
{
    private const long MiB = 1024 * 1024;
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.Parse("2026-09-09T00:00:00Z");

    [Fact]
    public void Observe_UsesFixedCheckpointsAndFreezesCompletedReport()
    {
        var tracker = new ApplicationYieldTracker();
        var process = Snapshot();
        Record(tracker, process, StartedAt);
        var original = Assert.Single(tracker.Reports);

        tracker.Observe(new[] { process }, StartedAt.AddSeconds(29));
        Assert.All(tracker.Reports[0].Checkpoints, sample => Assert.Null(sample.ObservedAt));
        tracker.Observe(new[] { process with { WorkingSetBytes = 30 * MiB } }, StartedAt.AddSeconds(30));
        tracker.Observe(new[] { process with { WorkingSetBytes = 40 * MiB } }, StartedAt.AddSeconds(60));
        tracker.Observe(new[] { process with { WorkingSetBytes = 50 * MiB } }, StartedAt.AddSeconds(120));

        var report = Assert.Single(tracker.Reports);
        Assert.Equal("Product", report.ApplicationName);
        Assert.Equal(StartedAt, report.StartedAt);
        Assert.Equal(ApplicationYieldStatus.Completed, report.Status);
        Assert.Equal(StartedAt.AddSeconds(120), report.CompletedAt);
        Assert.Equal(new[] { 30, 60, 120 }, report.Checkpoints.Select(sample => sample.ElapsedSeconds));
        Assert.Equal(new long?[] { 30 * MiB, 40 * MiB, 50 * MiB }, report.Checkpoints.Select(sample => sample.WorkingSetBytes));
        Assert.Equal(50 * MiB, report.Checkpoints[2].RetainedBytes);
        Assert.Equal(37.5d, report.Checkpoints[2].ReconstructedPercent);
        Assert.False(report.IsLowYield);
        Assert.All(original.Checkpoints, sample => Assert.Null(sample.ObservedAt));

        tracker.Observe(Array.Empty<ProcessSnapshot>(), StartedAt.AddHours(1));
        Assert.Same(report, tracker.Reports[0]);
    }

    [Fact]
    public void Observe_OnlyTracksMeasuredSuccessfulTargetsNotFailedUntrimmedOrOtherApps()
    {
        var tracker = new ApplicationYieldTracker();
        var target = Snapshot();
        var failed = Snapshot(2);
        var untrimmed = Snapshot(3);
        var group = Group(target, failed) with { ObservedProcesses = new[] { target, failed, untrimmed } };
        tracker.Record(group, new[]
        {
            (target, MemoryPurgeResult.Succeeded(target.ProcessId, 100 * MiB, 20 * MiB)),
            (failed, MemoryPurgeResult.Failed(failed.ProcessId, "denied"))
        }, StartedAt);

        Complete(tracker, StartedAt, target with { WorkingSetBytes = 30 * MiB },
            Snapshot(99, @"C:\Apps\Other\Product.exe") with { WorkingSetBytes = 10000 * MiB });

        var report = Assert.Single(tracker.Reports);
        Assert.Equal(2, report.TargetProcessCount);
        Assert.Equal(1, report.MeasuredProcessCount);
        Assert.Equal(100 * MiB, report.BeforeWorkingSetBytes);
        Assert.Equal(20 * MiB, report.AfterWorkingSetBytes);
        Assert.All(report.Checkpoints, sample => Assert.Equal(30 * MiB, sample.WorkingSetBytes));
        Assert.Equal(ApplicationYieldStatus.Completed, report.Status);
        Assert.False(tracker.IsDeferred(group, StartedAt.AddSeconds(120)));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("reused-pid")]
    [InlineData("unknown-start")]
    [InlineData("unknown-path")]
    [InlineData("different-path")]
    [InlineData("different-name")]
    [InlineData("unreadable-working-set")]
    [InlineData("duplicate-pid")]
    public void Observe_UnverifiableMemberIsUnknownNotZero(string change)
    {
        var tracker = new ApplicationYieldTracker();
        var first = Snapshot();
        var second = Snapshot(2);
        tracker.Record(Group(first, second), new[]
        {
            (first, MemoryPurgeResult.Succeeded(1, 100 * MiB, 20 * MiB)),
            (second, MemoryPurgeResult.Succeeded(2, 100 * MiB, 20 * MiB))
        }, StartedAt);
        var changed = change switch
        {
            "reused-pid" => second with { StartTimeUtc = StartedAt.AddSeconds(1) },
            "unknown-start" => second with { StartTimeUtc = null },
            "unknown-path" => second with { ExecutablePath = null },
            "different-path" => second with { ExecutablePath = @"C:\Apps\Other\Product.exe" },
            "different-name" => second with { ProcessName = "Unrelated" },
            "unreadable-working-set" => second with { WorkingSetBytes = 0, HasWorkingSetMeasurement = false },
            _ => second
        };
        var samples = change == "missing" ? new[] { first } :
            change == "duplicate-pid" ? new[] { first, changed, changed } : new[] { first, changed };
        Complete(tracker, StartedAt, samples);

        var report = Assert.Single(tracker.Reports);
        Assert.Equal(ApplicationYieldStatus.Unknown, report.Status);
        Assert.Null(report.IsLowYield);
        Assert.All(report.Checkpoints, sample =>
        {
            Assert.Equal(ApplicationYieldSampleStatus.Unknown, sample.Status);
            Assert.Null(sample.WorkingSetBytes);
            Assert.Null(sample.RetainedBytes);
            Assert.Null(sample.ReconstructedPercent);
        });
        Assert.False(tracker.IsDeferred(Group(first, second), StartedAt.AddSeconds(120)));
    }

    [Theory]
    [InlineData("unknown-start")]
    [InlineData("unknown-path")]
    [InlineData("relative-path")]
    [InlineData("future-start")]
    [InlineData("before-unreadable")]
    [InlineData("after-unreadable")]
    [InlineData("failed")]
    [InlineData("wrong-result-pid")]
    [InlineData("not-a-target")]
    [InlineData("duplicate-result")]
    public void Record_UnmeasuredOrUnverifiableTrimCannotBecomeLowYield(string change)
    {
        var tracker = new ApplicationYieldTracker();
        var process = Snapshot();
        process = change switch
        {
            "unknown-start" => process with { StartTimeUtc = null },
            "unknown-path" => process with { ExecutablePath = null },
            "relative-path" => process with { ExecutablePath = "Product.exe" },
            "future-start" => process with { StartTimeUtc = StartedAt.AddHours(1) },
            _ => process
        };
        var result = change switch
        {
            "before-unreadable" => new MemoryPurgeResult(1, true, 0, 20 * MiB, null, HasMeasurement: false),
            "after-unreadable" => MemoryPurgeResult.SucceededWithoutMeasurement(1, 100 * MiB),
            "failed" => MemoryPurgeResult.Failed(1, "denied"),
            "wrong-result-pid" => MemoryPurgeResult.Succeeded(2, 100 * MiB, 100 * MiB),
            _ => MemoryPurgeResult.Succeeded(1, 100 * MiB, 100 * MiB)
        };
        for (var run = 0; run < 2; run++)
        {
            var at = StartedAt.AddMinutes(run * 3);
            tracker.Record(change == "not-a-target" ? Group(Snapshot(2)) : Group(process),
                change == "duplicate-result" ? new[] { (process, result), (process, result) } : new[] { (process, result) }, at);
            Complete(tracker, at, process);
        }

        Assert.All(tracker.Reports, report =>
        {
            Assert.Equal(ApplicationYieldStatus.Unknown, report.Status);
            Assert.Null(report.BeforeWorkingSetBytes);
            Assert.Null(report.AfterWorkingSetBytes);
            Assert.Null(report.DeferredUntil);
        });
        Assert.False(tracker.IsDeferred(Group(Snapshot()), StartedAt.AddMinutes(5)));
    }

    [Theory]
    [InlineData(83, false)]
    [InlineData(84, true)]
    [InlineData(100, true)]
    [InlineData(120, true)]
    public void Observe_TwoBadRunsUseInclusiveReconstructionThresholdAndBackoffExpires(int workingSetMiB, bool deferred)
    {
        var tracker = new ApplicationYieldTracker();
        var process = Snapshot();
        Record(tracker, process, StartedAt);
        Complete(tracker, StartedAt, process with { WorkingSetBytes = workingSetMiB * MiB });
        Assert.False(tracker.IsDeferred(Group(process), StartedAt.AddSeconds(120)));
        Record(tracker, process, StartedAt.AddMinutes(3));
        Complete(tracker, StartedAt.AddMinutes(3), process with { WorkingSetBytes = workingSetMiB * MiB });
        var completedAt = StartedAt.AddMinutes(5);

        Assert.Equal(deferred, tracker.IsDeferred(Group(process), completedAt));
        Assert.Equal(deferred, tracker.IsDeferred(Group(process), completedAt.AddMinutes(10).AddTicks(-1)));
        Assert.False(tracker.IsDeferred(Group(process), completedAt.AddMinutes(10)));
        Assert.Equal(deferred ? completedAt.AddMinutes(10) : (DateTimeOffset?)null, tracker.Reports[1].DeferredUntil);
        Assert.InRange(tracker.Reports[0].Checkpoints[2].ReconstructedPercent!.Value, 0d, 100d);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(15, true)]
    [InlineData(16, false)]
    [InlineData(17, false)]
    public void Observe_TwoConsecutiveLowYieldsRequiredAndZeroTrimWaitsForWindow(int retainedMiB, bool deferred)
    {
        var tracker = new ApplicationYieldTracker();
        var process = Snapshot();
        for (var run = 0; run < 2; run++)
        {
            var at = StartedAt.AddMinutes(run * 3);
            var after = (100 - retainedMiB) * MiB;
            Record(tracker, process, at, after);
            Assert.False(tracker.IsDeferred(Group(process), at));
            tracker.Observe(new[] { process with { WorkingSetBytes = after } }, at.AddSeconds(30));
            tracker.Observe(new[] { process with { WorkingSetBytes = after } }, at.AddSeconds(60));
            Assert.False(tracker.IsDeferred(Group(process), at.AddSeconds(119)));
            tracker.Observe(new[] { process with { WorkingSetBytes = after } }, at.AddSeconds(120));
            Assert.Equal(run == 1 && deferred, tracker.IsDeferred(Group(process), at.AddSeconds(120)));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Observe_LowYieldAndReconstructionShareOneBadStreakCappedAtTwo(bool lowYieldFirst)
    {
        var tracker = new ApplicationYieldTracker();
        var process = Snapshot();
        for (var run = 0; run < 3; run++)
        {
            var lowYield = (run == 0) == lowYieldFirst;
            var at = StartedAt.AddMinutes(run * 3);
            Record(tracker, process, at, lowYield ? 95 * MiB : 20 * MiB);
            Complete(tracker, at, process with { WorkingSetBytes = lowYield ? 95 * MiB : 84 * MiB });
            Assert.Equal(lowYield, tracker.Reports[run].IsLowYield);
            Assert.Equal(Math.Min(2, run + 1), tracker.Reports[run].ConsecutiveBadRunCount);
            Assert.Equal(run > 0, tracker.IsDeferred(Group(process), at.AddSeconds(120)));
        }
    }

    [Fact]
    public void Observe_ValidGoodWindowBreaksReconstructionStreak()
    {
        var tracker = new ApplicationYieldTracker();
        var process = Snapshot();
        foreach (var run in new[] { 0, 1, 2 })
        {
            var at = StartedAt.AddMinutes(run * 3);
            Record(tracker, process, at);
            Complete(tracker, at, process with { WorkingSetBytes = (run == 1 ? 20 : 84) * MiB });
            Assert.False(tracker.IsDeferred(Group(process), at.AddSeconds(120)));
        }

        Assert.Equal(new[] { 1, 0, 1 }, tracker.Reports.Select(report => report.ConsecutiveBadRunCount));
    }

    [Fact]
    public void Observe_SumsOnlyOriginalMeasuredFamilyMembersAndDoesNotAdoptNewMembers()
    {
        var tracker = new ApplicationYieldTracker();
        var first = Snapshot();
        var helper = Snapshot(2, @"C:\Apps\Product\Helper.exe") with { ProcessName = "Helper" };
        tracker.Record(Group(first, helper), new[]
        {
            (first, MemoryPurgeResult.Succeeded(1, 100 * MiB, 20 * MiB)),
            (helper, MemoryPurgeResult.Succeeded(2, 50 * MiB, 10 * MiB))
        }, StartedAt);
        Complete(tracker, StartedAt, first with { WorkingSetBytes = 30 * MiB },
            helper with { WorkingSetBytes = 20 * MiB }, Snapshot(3) with { WorkingSetBytes = 1000 * MiB });

        var report = Assert.Single(tracker.Reports);
        Assert.Equal(2, report.MeasuredProcessCount);
        Assert.Equal(150 * MiB, report.BeforeWorkingSetBytes);
        Assert.Equal(30 * MiB, report.AfterWorkingSetBytes);
        Assert.All(report.Checkpoints, sample =>
        {
            Assert.Equal(50 * MiB, sample.WorkingSetBytes);
            Assert.Equal(100 * MiB, sample.RetainedBytes);
        });
    }

    [Fact]
    public void Observe_TransientMissingIdentityCannotRecoverWithinTheSameWindow()
    {
        var tracker = new ApplicationYieldTracker();
        var process = Snapshot();
        Record(tracker, process, StartedAt);
        tracker.Observe(Array.Empty<ProcessSnapshot>(), StartedAt.AddSeconds(10));
        Complete(tracker, StartedAt, process);

        Assert.Equal(ApplicationYieldStatus.Unknown, tracker.Reports[0].Status);
        Assert.Equal(0, tracker.Reports[0].ConsecutiveBadRunCount);
    }

    [Fact]
    public void Record_OneUnmeasuredSuccessfulTargetCannotTurnUnknownAggregateIntoZero()
    {
        var tracker = new ApplicationYieldTracker();
        var first = Snapshot();
        var second = Snapshot(2);
        tracker.Record(Group(first, second), new[]
        {
            (first, MemoryPurgeResult.Succeeded(1, 100 * MiB, 20 * MiB)),
            (second, MemoryPurgeResult.SucceededWithoutMeasurement(2, 100 * MiB))
        }, StartedAt);
        Complete(tracker, StartedAt, first, second);

        var report = Assert.Single(tracker.Reports);
        Assert.Equal(1, report.MeasuredProcessCount);
        Assert.Equal(ApplicationYieldStatus.Unknown, report.Status);
        Assert.Null(report.BeforeWorkingSetBytes);
        Assert.Null(report.AfterWorkingSetBytes);
        Assert.Equal(0, report.ConsecutiveBadRunCount);
    }

    [Fact]
    public void Record_OverflowingAggregateIsUnknown()
    {
        var tracker = new ApplicationYieldTracker();
        var first = Snapshot();
        var second = Snapshot(2);
        tracker.Record(Group(first, second), new[]
        {
            (first, MemoryPurgeResult.Succeeded(1, long.MaxValue, long.MaxValue)),
            (second, MemoryPurgeResult.Succeeded(2, long.MaxValue, long.MaxValue))
        }, StartedAt);
        Complete(tracker, StartedAt, first, second);

        Assert.Equal(ApplicationYieldStatus.Unknown, tracker.Reports[0].Status);
        Assert.Null(tracker.Reports[0].BeforeWorkingSetBytes);
        Assert.Equal(0, tracker.Reports[0].ConsecutiveBadRunCount);
    }

    [Fact]
    public void Record_AfterUnobservedWindowExpiresKeepsOldReportUnknown()
    {
        var tracker = new ApplicationYieldTracker();
        var process = Snapshot();
        Record(tracker, process, StartedAt);
        Record(tracker, process, StartedAt.AddMinutes(10));

        Assert.Equal(2, tracker.Reports.Count);
        Assert.Equal(StartedAt, tracker.Reports[0].StartedAt);
        Assert.Equal(ApplicationYieldStatus.Unknown, tracker.Reports[0].Status);
        Assert.All(tracker.Reports[0].Checkpoints, sample => Assert.Null(sample.WorkingSetBytes));
        Assert.Equal(ApplicationYieldStatus.Observing, tracker.Reports[1].Status);
        Assert.Equal(0, tracker.Reports[1].ConsecutiveBadRunCount);
    }

    [Fact]
    public void Observe_ZeroImmediateTrimIsLowYieldEvenIfWorkingSetLaterFalls()
    {
        var tracker = new ApplicationYieldTracker();
        var process = Snapshot();
        Record(tracker, process, StartedAt, 100 * MiB);
        Complete(tracker, StartedAt, process with { WorkingSetBytes = 20 * MiB });

        Assert.True(tracker.Reports[0].IsLowYield);
        Assert.Null(tracker.Reports[0].Checkpoints[2].ReconstructedPercent);
        Assert.False(tracker.IsDeferred(Group(process), StartedAt.AddMinutes(2)));
    }

    [Fact]
    public void Observe_MeasuredZeroIsValidAndUnknownFlagNeverBecomesZero()
    {
        var tracker = new ApplicationYieldTracker();
        var process = Snapshot();
        Record(tracker, process, StartedAt, after: 0);
        Complete(tracker, StartedAt, process with { WorkingSetBytes = 0 });
        var report = tracker.Reports[0];
        Assert.Equal(ApplicationYieldStatus.Completed, report.Status);
        Assert.Equal(0, report.AfterWorkingSetBytes);
        Assert.All(report.Checkpoints, sample =>
        {
            Assert.Equal(0, sample.WorkingSetBytes);
            Assert.Equal(100 * MiB, sample.RetainedBytes);
            Assert.Equal(0d, sample.ReconstructedPercent);
        });

        var at = StartedAt.AddMinutes(3);
        Record(tracker, process, at);
        Complete(tracker, at, process with { WorkingSetBytes = 100 * MiB, HasWorkingSetMeasurement = false });
        Assert.Equal(ApplicationYieldStatus.Unknown, tracker.Reports[1].Status);
        Assert.All(tracker.Reports[1].Checkpoints, sample => Assert.Null(sample.WorkingSetBytes));
        Assert.False(tracker.IsDeferred(Group(process), at.AddSeconds(120)));
    }

    [Fact]
    public void Observe_SuccessWithoutMeasurementCannotCreateOrClearBadStreak()
    {
        var tracker = new ApplicationYieldTracker();
        var process = Snapshot();
        Record(tracker, process, StartedAt, 95 * MiB);
        Complete(tracker, StartedAt, process with { WorkingSetBytes = 95 * MiB });
        tracker.Record(Group(process), new[]
        {
            (process, MemoryPurgeResult.SucceededWithoutMeasurement(1, 100 * MiB))
        }, StartedAt.AddMinutes(3));
        Complete(tracker, StartedAt.AddMinutes(3), process with { WorkingSetBytes = 20 * MiB });
        Assert.Equal(1, tracker.Reports[1].ConsecutiveLowYieldCount);
        Assert.False(tracker.IsDeferred(Group(process), StartedAt.AddMinutes(5)));
        Record(tracker, process, StartedAt.AddMinutes(6), 95 * MiB);
        Complete(tracker, StartedAt.AddMinutes(6), process with { WorkingSetBytes = 95 * MiB });
        Assert.True(tracker.IsDeferred(Group(process), StartedAt.AddMinutes(8)));
    }

    [Fact]
    public void Observe_UnknownDoesNotCreateOrClearStreakButValidGoodWindowResetsIt()
    {
        var tracker = new ApplicationYieldTracker();
        var process = Snapshot();
        Record(tracker, process, StartedAt, 95 * MiB);
        Complete(tracker, StartedAt, process with { WorkingSetBytes = 95 * MiB });
        Record(tracker, process, StartedAt.AddMinutes(3));
        Complete(tracker, StartedAt.AddMinutes(3));
        Assert.False(tracker.IsDeferred(Group(process), StartedAt.AddMinutes(5)));
        Record(tracker, process, StartedAt.AddMinutes(6), 95 * MiB);
        Complete(tracker, StartedAt.AddMinutes(6), process with { WorkingSetBytes = 95 * MiB });
        Assert.True(tracker.IsDeferred(Group(process), StartedAt.AddMinutes(8)));
        var until = tracker.Reports[2].DeferredUntil;

        Record(tracker, process, StartedAt.AddMinutes(9));
        Complete(tracker, StartedAt.AddMinutes(9));
        Assert.Equal(until, tracker.Reports[3].DeferredUntil);
        Assert.True(tracker.IsDeferred(Group(process), StartedAt.AddMinutes(11)));
        Record(tracker, process, StartedAt.AddMinutes(12));
        Complete(tracker, StartedAt.AddMinutes(12), process with { WorkingSetBytes = 20 * MiB });
        Assert.True(tracker.IsDeferred(Group(process), StartedAt.AddMinutes(14)));
        Assert.Equal(0, tracker.Reports[4].ConsecutiveLowYieldCount);
        Record(tracker, process, StartedAt.AddMinutes(19), 95 * MiB);
        Complete(tracker, StartedAt.AddMinutes(19), process with { WorkingSetBytes = 95 * MiB });
        Assert.False(tracker.IsDeferred(Group(process), StartedAt.AddMinutes(21)));
    }

    [Fact]
    public void HasPendingObservation_BlocksUntilValidWindowFinishes()
    {
        var tracker = new ApplicationYieldTracker();
        var process = Snapshot();
        var group = Group(process);
        Assert.False(tracker.HasPendingObservation(group, StartedAt));
        Record(tracker, process, StartedAt);
        Assert.False(tracker.HasPendingObservation(group, StartedAt.AddTicks(-1)));
        Assert.True(tracker.HasPendingObservation(group, StartedAt));

        foreach (var seconds in new[] { 30, 60 })
        {
            tracker.Observe(new[] { process }, StartedAt.AddSeconds(seconds));
            Assert.True(tracker.HasPendingObservation(group, StartedAt.AddSeconds(seconds)));
        }

        Assert.True(tracker.HasPendingObservation(group, StartedAt.AddSeconds(120)));
        tracker.Observe(new[] { process }, StartedAt.AddSeconds(120));
        Assert.False(tracker.HasPendingObservation(group, StartedAt.AddSeconds(120)));
        Assert.False(tracker.IsDeferred(group, StartedAt.AddSeconds(120)));
    }

    [Fact]
    public void HasPendingObservation_PartialWindowHasFixedDeadlineAndQueryDoesNotMutateIt()
    {
        var tracker = new ApplicationYieldTracker();
        var process = Snapshot();
        Record(tracker, process, StartedAt);
        tracker.Observe(new[] { process }, StartedAt.AddSeconds(30));
        tracker.Observe(new[] { process }, StartedAt.AddSeconds(60));
        var report = Assert.Single(tracker.Reports);
        var deadline = StartedAt.AddSeconds(120) + ApplicationYieldTracker.SampleTolerance;

        Assert.True(tracker.HasPendingObservation(Group(process), deadline));
        Assert.False(tracker.HasPendingObservation(Group(process), deadline.AddTicks(1)));
        Assert.False(tracker.HasPendingObservation(Group(process), StartedAt.AddSeconds(180)));
        Assert.Same(report, tracker.Reports[0]);
        Assert.Null(report.CompletedAt);
    }

    [Theory]
    [InlineData("unmeasured-trim")]
    [InlineData("missing-member")]
    [InlineData("missed-checkpoint")]
    public void HasPendingObservation_UnknownWindowDoesNotBlockEvenWhileReportIsObserving(string reason)
    {
        var tracker = new ApplicationYieldTracker();
        var process = Snapshot();
        var result = reason == "unmeasured-trim"
            ? MemoryPurgeResult.SucceededWithoutMeasurement(process.ProcessId, 100 * MiB)
            : MemoryPurgeResult.Succeeded(process.ProcessId, 100 * MiB, 20 * MiB);
        tracker.Record(Group(process), new[] { (process, result) }, StartedAt);
        var now = StartedAt;
        if (reason != "unmeasured-trim")
        {
            now = StartedAt.AddSeconds(reason == "missing-member" ? 30 : 60);
            tracker.Observe(reason == "missing-member" ? Array.Empty<ProcessSnapshot>() : new[] { process }, now);
        }

        Assert.Equal(ApplicationYieldStatus.Observing, tracker.Reports[0].Status);
        Assert.Null(tracker.Reports[0].CompletedAt);
        Assert.False(tracker.HasPendingObservation(Group(process), now));
    }

    [Fact]
    public void HasPendingObservation_RequiresOriginalProcessIdentityNotJustSameApplicationKey()
    {
        var tracker = new ApplicationYieldTracker();
        var process = Snapshot();
        Record(tracker, process, StartedAt);

        Assert.True(tracker.HasPendingObservation(Group(process with { ExecutablePath = @"c:\APPS\product\Product.exe" }), StartedAt));
        Assert.False(tracker.HasPendingObservation(Group(Snapshot(2)), StartedAt));
        Assert.False(tracker.HasPendingObservation(Group(process with { StartTimeUtc = StartedAt.AddMinutes(-1) }), StartedAt));
        Assert.False(tracker.HasPendingObservation(Group(Snapshot(1, @"C:\Apps\Product\Helper.exe")), StartedAt));
        Assert.False(tracker.HasPendingObservation(Group(Snapshot(1, @"C:\Apps\Other\Product.exe")), StartedAt));
        Assert.False(tracker.HasPendingObservation(Group(process with { ExecutablePath = null }), StartedAt));
        Assert.False(tracker.HasPendingObservation(Group(process with { StartTimeUtc = null }), StartedAt));
    }

    [Fact]
    public void HasPendingObservation_RequiresEveryMeasuredTargetToStillMatch()
    {
        var tracker = new ApplicationYieldTracker();
        var first = Snapshot();
        var second = Snapshot(2);
        tracker.Record(Group(first, second), new[]
        {
            (first, MemoryPurgeResult.Succeeded(1, 100 * MiB, 20 * MiB)),
            (second, MemoryPurgeResult.Succeeded(2, 100 * MiB, 20 * MiB))
        }, StartedAt);

        Assert.True(tracker.HasPendingObservation(Group(second, first), StartedAt));
        Assert.False(tracker.HasPendingObservation(Group(first), StartedAt));
        Assert.False(tracker.HasPendingObservation(Group(first, second with { StartTimeUtc = StartedAt.AddMinutes(-1) }), StartedAt));
        Assert.False(tracker.HasPendingObservation(Group(first, second with { ExecutablePath = @"C:\Apps\Product\Helper.exe" }), StartedAt));
    }

    [Fact]
    public void HasPendingObservation_CompletedSuccessfulSubsetDoesNotWaitForFailedTargets()
    {
        var tracker = new ApplicationYieldTracker();
        var process = Snapshot();
        var failed = Snapshot(2);
        var group = Group(process, failed);
        tracker.Record(group, new[]
        {
            (process, MemoryPurgeResult.Succeeded(process.ProcessId, 100 * MiB, 20 * MiB)),
            (failed, MemoryPurgeResult.Failed(failed.ProcessId, "denied"))
        }, StartedAt);
        Assert.True(tracker.HasPendingObservation(group, StartedAt));
        Complete(tracker, StartedAt, process);

        Assert.Equal(ApplicationYieldStatus.Completed, tracker.Reports[0].Status);
        Assert.Equal(1, tracker.Reports[0].MeasuredProcessCount);
        Assert.False(tracker.HasPendingObservation(group, StartedAt.AddSeconds(120)));
    }

    [Fact]
    public void IsDeferred_UsesStableFamilyIdentityNotNameOrPid()
    {
        var tracker = new ApplicationYieldTracker();
        var process = Snapshot();
        Record(tracker, process, StartedAt);
        Complete(tracker, StartedAt, process with { WorkingSetBytes = 100 * MiB });
        Record(tracker, process, StartedAt.AddMinutes(3));
        Complete(tracker, StartedAt.AddMinutes(3), process with { WorkingSetBytes = 100 * MiB });
        var now = StartedAt.AddMinutes(6);
        var restartedHelper = Snapshot(2, @"c:\APPS\product\Helper.exe") with { StartTimeUtc = now.AddSeconds(-1) };

        Assert.True(tracker.IsDeferred(Group(restartedHelper), now));
        Assert.False(tracker.IsDeferred(Group(Snapshot(1, @"C:\Apps\Other\Product.exe")), now));
        Assert.False(tracker.IsDeferred(Group(process with { ExecutablePath = null }), now));
        Assert.False(tracker.IsDeferred(Group(process with { StartTimeUtc = null }), now));
    }

    [Fact]
    public void Record_OverlappingRunDoesNotOverwriteBaselineAndCannotContaminateOldDecision()
    {
        var tracker = new ApplicationYieldTracker();
        var process = Snapshot();
        Record(tracker, process, StartedAt);
        tracker.Observe(new[] { process with { WorkingSetBytes = 50 * MiB } }, StartedAt.AddSeconds(30));
        var checkpoint = tracker.Reports[0].Checkpoints[0];
        Record(tracker, process, StartedAt.AddSeconds(40), 10 * MiB);
        Assert.Equal(2, tracker.Reports.Count);
        Assert.Equal(StartedAt, tracker.Reports[0].StartedAt);
        Assert.Equal(20 * MiB, tracker.Reports[0].AfterWorkingSetBytes);
        Assert.Equal(10 * MiB, tracker.Reports[1].AfterWorkingSetBytes);
        tracker.Observe(new[] { process }, StartedAt.AddSeconds(60));
        tracker.Observe(new[] { process with { WorkingSetBytes = 20 * MiB } }, StartedAt.AddSeconds(70));
        tracker.Observe(new[] { process with { WorkingSetBytes = 20 * MiB } }, StartedAt.AddSeconds(100));
        tracker.Observe(new[] { process }, StartedAt.AddSeconds(120));
        tracker.Observe(new[] { process with { WorkingSetBytes = 20 * MiB } }, StartedAt.AddSeconds(160));

        Assert.Equal(checkpoint, tracker.Reports[0].Checkpoints[0]);
        Assert.Equal(ApplicationYieldStatus.Unknown, tracker.Reports[0].Status);
        Assert.Equal(ApplicationYieldStatus.Completed, tracker.Reports[1].Status);
        Assert.False(tracker.IsDeferred(Group(process), StartedAt.AddSeconds(160)));
    }

    [Fact]
    public void Observe_MissingEarlierCheckpointCannotBeBackfilledOrChangeStreak()
    {
        var tracker = new ApplicationYieldTracker();
        var process = Snapshot();
        Record(tracker, process, StartedAt);
        tracker.Observe(new[] { process }, StartedAt.AddSeconds(60));
        tracker.Observe(new[] { process }, StartedAt.AddSeconds(120));

        var report = Assert.Single(tracker.Reports);
        Assert.Null(report.Checkpoints[0].WorkingSetBytes);
        Assert.Null(report.Checkpoints[0].ObservedAt);
        Assert.Equal(StartedAt.AddSeconds(60), report.Checkpoints[1].ObservedAt);
        Assert.Equal(ApplicationYieldStatus.Unknown, report.Status);
        Assert.False(tracker.IsDeferred(Group(process), StartedAt.AddSeconds(120)));
    }

    [Fact]
    public void Observe_UsesActualSampleTimeWithinToleranceAndRejectsVeryLateFinalSample()
    {
        var tracker = new ApplicationYieldTracker();
        var process = Snapshot();
        Record(tracker, process, StartedAt);
        tracker.Observe(new[] { process }, StartedAt.AddSeconds(31));
        tracker.Observe(new[] { process }, StartedAt.AddSeconds(61));
        tracker.Observe(new[] { process }, StartedAt.AddMinutes(30));

        Assert.Equal(StartedAt.AddSeconds(31), tracker.Reports[0].Checkpoints[0].ObservedAt);
        Assert.Equal(StartedAt.AddSeconds(61), tracker.Reports[0].Checkpoints[1].ObservedAt);
        Assert.Null(tracker.Reports[0].Checkpoints[2].WorkingSetBytes);
        Assert.Equal(ApplicationYieldStatus.Unknown, tracker.Reports[0].Status);
        Assert.False(tracker.IsDeferred(Group(process), StartedAt.AddMinutes(30)));
    }

    [Fact]
    public void Observe_BackwardTimeAndDuplicateTicksCannotRewriteSamples()
    {
        var tracker = new ApplicationYieldTracker();
        var process = Snapshot();
        Record(tracker, process, StartedAt);
        tracker.Observe(new[] { process with { WorkingSetBytes = 20 * MiB } }, StartedAt.AddSeconds(30));
        tracker.Observe(new[] { process }, StartedAt.AddSeconds(30));
        tracker.Observe(new[] { process }, StartedAt.AddSeconds(29));
        Record(tracker, process, StartedAt.AddSeconds(29));

        Assert.Single(tracker.Reports);
        Assert.Equal(20 * MiB, tracker.Reports[0].Checkpoints[0].WorkingSetBytes);
    }

    [Fact]
    public void Reports_AreBoundedAndNewAppsDoNotEvictActiveWindowsOrDeferrals()
    {
        var tracker = new ApplicationYieldTracker();
        var deferred = Snapshot();
        Record(tracker, deferred, StartedAt);
        Complete(tracker, StartedAt, deferred);
        Record(tracker, deferred, StartedAt.AddMinutes(3));
        Complete(tracker, StartedAt.AddMinutes(3), deferred);
        var activeAt = StartedAt.AddMinutes(6);
        var active = Snapshot(2, @"C:\Apps\Active\Product.exe");
        Record(tracker, active, activeAt);
        for (var index = 0; index < ApplicationYieldTracker.MaximumReportCount + 10; index++)
        {
            Record(tracker, Snapshot(index + 10, $@"C:\Apps\Product{index}\Product.exe"), activeAt);
        }

        Assert.Equal(ApplicationYieldTracker.MaximumReportCount, tracker.Reports.Count);
        Assert.Contains(tracker.Reports, report => report.ApplicationKey!.Contains(@"\active"));
        Assert.True(tracker.IsDeferred(Group(deferred), activeAt));
        Complete(tracker, activeAt, active with { WorkingSetBytes = 20 * MiB });
        Assert.Contains(tracker.Reports, report => report.ApplicationKey!.Contains(@"\active") && report.Status == ApplicationYieldStatus.Completed);
    }

    [Fact]
    public void Reports_FullDecisionHistoryDoesNotEvictUnexpiredDeferrals()
    {
        var tracker = new ApplicationYieldTracker();
        var processes = Enumerable.Range(1, ApplicationYieldTracker.MaximumTrackedApplications)
            .Select(id => Snapshot(id, $@"C:\Apps\Product{id}\Product.exe")).ToArray();
        for (var run = 0; run < 2; run++)
        {
            var at = StartedAt.AddMinutes(run * 3);
            foreach (var process in processes)
            {
                Record(tracker, process, at);
            }

            Complete(tracker, at, processes);
        }

        var newcomer = Snapshot(1000, @"C:\Apps\New\Product.exe");
        Record(tracker, newcomer, StartedAt.AddMinutes(6));
        Complete(tracker, StartedAt.AddMinutes(6), newcomer);

        Assert.All(processes, process => Assert.True(tracker.IsDeferred(Group(process), StartedAt.AddMinutes(8))));
        Assert.True(tracker.Reports.Count <= ApplicationYieldTracker.MaximumReportCount);
    }

    private static ProcessSnapshot Snapshot(int id = 1, string path = @"C:\Apps\Product\Product.exe") =>
        new(id, "Product", 100 * MiB, false, ExecutablePath: path, StartTimeUtc: StartedAt.AddHours(-1));

    private static PurgeCandidateGroup Group(params ProcessSnapshot[] processes) =>
        new("Product", processes.FirstOrDefault().ExecutablePath, processes, processes,
            processes.Sum(process => process.WorkingSetBytes), 0, 0, 0, false, false);

    private static void Record(ApplicationYieldTracker tracker, ProcessSnapshot process, DateTimeOffset at, long after = 20 * MiB) =>
        tracker.Record(Group(process), new[] { (process, MemoryPurgeResult.Succeeded(process.ProcessId, 100 * MiB, after)) }, at);

    private static void Complete(ApplicationYieldTracker tracker, DateTimeOffset at, params ProcessSnapshot[] snapshots)
    {
        foreach (var seconds in new[] { 30, 60, 120 })
        {
            tracker.Observe(snapshots, at.AddSeconds(seconds));
        }
    }
}
