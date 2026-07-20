using FluxRAM.Core.Models;
using FluxRAM.Core.Services;
using Xunit;

namespace FluxRAM.Core.Tests;

public sealed class BackgroundActivityTrackerTests
{
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
