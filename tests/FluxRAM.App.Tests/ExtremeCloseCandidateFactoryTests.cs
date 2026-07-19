using FluxRAM.App.Configuration;
using FluxRAM.Core.Models;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class ExtremeCloseCandidateFactoryTests
{
    [Fact]
    public void FromSnapshots_GroupsHighMemoryAppsByProcessName()
    {
        var snapshots = new[]
        {
            new ProcessSnapshot(10, "chrome", 180L * 1024 * 1024, false),
            new ProcessSnapshot(11, "chrome", 160L * 1024 * 1024, false),
            new ProcessSnapshot(12, "discord", 300L * 1024 * 1024, false)
        };

        var candidates = ExtremeCloseCandidateFactory.FromSnapshots(snapshots);

        var chrome = Assert.Single(candidates, candidate => candidate.ProcessName == "chrome");
        Assert.Equal(2, chrome.ProcessIds.Count);
        Assert.True(chrome.IsDefaultSelected);
    }

    [Fact]
    public void FromSnapshots_IncludesForegroundAppsButDoesNotSelectThemByDefault()
    {
        var snapshots = new[]
        {
            new ProcessSnapshot(20, "chrome", 800L * 1024 * 1024, true, HasVisibleWindow: true)
        };

        var candidate = Assert.Single(ExtremeCloseCandidateFactory.FromSnapshots(snapshots));

        Assert.True(candidate.HasForegroundProcess);
        Assert.False(candidate.IsDefaultSelected);
    }

    [Fact]
    public void FromSnapshots_ExcludesGamingAndProtectedProcesses()
    {
        var snapshots = new[]
        {
            new ProcessSnapshot(30, "steam", 900L * 1024 * 1024, false),
            new ProcessSnapshot(31, "obs64", 700L * 1024 * 1024, false, ExecutablePath: @"C:\Tools\OBS\obs64.exe"),
            new ProcessSnapshot(32, "kook", 500L * 1024 * 1024, false)
        };

        var candidates = ExtremeCloseCandidateFactory.FromSnapshots(
            snapshots,
            protectedProcessPaths: new[] { @"C:\Tools\OBS\obs64.exe" });

        var candidate = Assert.Single(candidates);
        Assert.Equal("kook", candidate.ProcessName);
    }
}
