using FluxRAM.Core.Models;
using FluxRAM.Core.Services;
using Xunit;

namespace FluxRAM.Core.Tests;

public sealed class BackgroundActivityTrackerTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void Observe_MissingMemberMeasurementsResetIdleHistoryAndRequireFreshObservation(
        bool hasCpuMeasurement,
        bool hasIoMeasurement)
    {
        var tracker = new BackgroundActivityTracker();
        var startedAt = DateTimeOffset.Parse("2026-09-09T00:00:00Z");
        var snapshots = CreateSnapshots();
        for (var sample = 0; sample <= 5; sample++)
        {
            tracker.Observe(snapshots, startedAt.AddSeconds(sample * 15));
        }
        Assert.Equal(BackgroundActivityState.Idle, Assert.Single(tracker.CurrentAssessments.Values).State);

        var failedSnapshots = CreateSnapshots();
        failedSnapshots[1] = failedSnapshots[1] with
        {
            HasCpuMeasurement = hasCpuMeasurement,
            HasIoMeasurement = hasIoMeasurement
        };
        for (var sample = 0; sample <= 5; sample++)
        {
            var failed = Assert.Single(tracker.Observe(
                failedSnapshots, startedAt.AddSeconds(90 + sample * 15)).Values);

            Assert.Equal(BackgroundActivityState.Observing, failed.State);
            Assert.Equal(0, failed.SampleCount);
            Assert.Equal(TimeSpan.Zero, failed.ObservedFor);
            Assert.Equal(TimeSpan.Zero, failed.IdleFor);
        }

        failedSnapshots[0] = failedSnapshots[0] with { CpuUsagePercent = 2d };
        Assert.Equal(BackgroundActivityState.Observing,
            Assert.Single(tracker.Observe(failedSnapshots, startedAt.AddSeconds(180)).Values).State);

        var recoveredAt = startedAt.AddSeconds(240);
        var recovered = Assert.Single(tracker.Observe(snapshots, recoveredAt).Values);
        Assert.Equal(BackgroundActivityState.Observing, recovered.State);
        Assert.Equal(1, recovered.SampleCount);
        Assert.Equal(TimeSpan.Zero, recovered.ObservedFor);
        Assert.Equal(TimeSpan.Zero, recovered.IdleFor);

        for (var sample = 1; sample < BackgroundActivityTracker.MinimumSampleCount; sample++)
        {
            var warming = Assert.Single(tracker.Observe(snapshots, recoveredAt.AddSeconds(sample)).Values);
            Assert.Equal(BackgroundActivityState.Observing, warming.State);
        }

        var tooSoon = Assert.Single(tracker.Observe(snapshots, recoveredAt.AddSeconds(59)).Values);
        Assert.Equal(BackgroundActivityState.Observing, tooSoon.State);
        var idle = Assert.Single(tracker.Observe(snapshots, recoveredAt.AddSeconds(60)).Values);
        Assert.Equal(BackgroundActivityState.Idle, idle.State);
        Assert.Equal(TimeSpan.FromSeconds(60), idle.ObservedFor);
        Assert.Equal(TimeSpan.FromSeconds(60), idle.IdleFor);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Observe_VisibleOverridesMissingMeasurements(bool isForeground, bool hasVisibleWindow)
    {
        var snapshots = CreateSnapshots(hasVisibleWindow: hasVisibleWindow);
        snapshots[0] = snapshots[0] with { IsForeground = isForeground };
        snapshots[1] = snapshots[1] with { HasIoMeasurement = false };

        var assessment = Assert.Single(new BackgroundActivityTracker()
            .Observe(snapshots, DateTimeOffset.UtcNow).Values);

        Assert.Equal(BackgroundActivityState.Visible, assessment.State);
        Assert.Equal(0, assessment.SampleCount);
        Assert.Equal(TimeSpan.Zero, assessment.ObservedFor);
        Assert.Equal(TimeSpan.Zero, assessment.IdleFor);
    }

    [Fact]
    public void Observe_RequiresTimeAndSamplesBeforeClassifyingIdle()
    {
        var tracker = new BackgroundActivityTracker();
        var startedAt = DateTimeOffset.Parse("2026-07-20T10:00:00Z");
        var snapshots = CreateSnapshots();

        var first = Assert.Single(tracker.Observe(snapshots, startedAt).Values);
        Assert.Equal(BackgroundActivityState.Observing, first.State);

        BackgroundActivityAssessment? latest = null;
        for (var sample = 1; sample <= 5; sample++)
        {
            latest = Assert.Single(tracker.Observe(
                snapshots,
                startedAt.AddSeconds(sample * 15)).Values);
        }

        Assert.NotNull(latest);
        Assert.Equal(BackgroundActivityState.Idle, latest.State);
        Assert.True(latest.IdleFor >= TimeSpan.FromSeconds(60));
        Assert.True(latest.SampleCount >= BackgroundActivityTracker.MinimumSampleCount);
    }

    [Fact]
    public void Observe_RecentCpuActivityKeepsFamilyWorking()
    {
        var tracker = new BackgroundActivityTracker();
        var startedAt = DateTimeOffset.Parse("2026-07-20T10:00:00Z");
        var idleSnapshots = CreateSnapshots();

        for (var sample = 0; sample <= 5; sample++)
        {
            tracker.Observe(idleSnapshots, startedAt.AddSeconds(sample * 15));
        }

        var activeSnapshots = CreateSnapshots(cpuUsagePercent: 2d);
        var active = Assert.Single(tracker.Observe(activeSnapshots, startedAt.AddSeconds(90)).Values);
        Assert.Equal(BackgroundActivityState.Working, active.State);

        var recent = Assert.Single(tracker.Observe(idleSnapshots, startedAt.AddSeconds(105)).Values);
        Assert.Equal(BackgroundActivityState.Working, recent.State);
        Assert.Equal(TimeSpan.FromSeconds(15), recent.IdleFor);
    }

    [Fact]
    public void Observe_VisibleWindowResetsIdlePeriod()
    {
        var tracker = new BackgroundActivityTracker();
        var startedAt = DateTimeOffset.Parse("2026-07-20T10:00:00Z");
        var idleSnapshots = CreateSnapshots();

        for (var sample = 0; sample <= 5; sample++)
        {
            tracker.Observe(idleSnapshots, startedAt.AddSeconds(sample * 15));
        }

        var visible = Assert.Single(tracker.Observe(
            CreateSnapshots(hasVisibleWindow: true),
            startedAt.AddSeconds(90)).Values);
        Assert.Equal(BackgroundActivityState.Visible, visible.State);

        var hiddenAgain = Assert.Single(tracker.Observe(
            idleSnapshots,
            startedAt.AddSeconds(105)).Values);
        Assert.Equal(BackgroundActivityState.Working, hiddenAgain.State);
        Assert.Equal(TimeSpan.FromSeconds(15), hiddenAgain.IdleFor);
    }

    private static ProcessSnapshot[] CreateSnapshots(
        double cpuUsagePercent = 0d,
        bool hasVisibleWindow = false)
    {
        return
        [
            new ProcessSnapshot(
                10,
                "ExampleApp",
                30L * 1024 * 1024,
                false,
                CpuUsagePercent: cpuUsagePercent,
                HasVisibleWindow: hasVisibleWindow,
                ExecutablePath: @"C:\Apps\Example\ExampleApp.exe"),
            new ProcessSnapshot(
                11,
                "ExampleHelper",
                20L * 1024 * 1024,
                false,
                ExecutablePath: @"C:\Apps\Example\ExampleHelper.exe")
        ];
    }
}
