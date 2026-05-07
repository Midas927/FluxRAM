using FluxRAM.App.Licensing;
using FluxRAM.Core.Models;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class ProtectedAppCandidateFactoryTests
{
    [Fact]
    public void FromSnapshots_ReturnsDistinctNonSystemExecutablePaths()
    {
        var snapshots = new[]
        {
            new ProcessSnapshot(10, "obs64", 500, false, ExecutablePath: @"C:\Tools\OBS\obs64.exe"),
            new ProcessSnapshot(11, "obs64", 500, false, ExecutablePath: @"C:\Tools\OBS\obs64.exe"),
            new ProcessSnapshot(12, "explorer", 500, false, ExecutablePath: @"C:\Windows\explorer.exe"),
            new ProcessSnapshot(13, "chat", 500, false, ExecutablePath: null),
            new ProcessSnapshot(14, "game", 500, true, ExecutablePath: @"D:\Games\Game\game.exe")
        };

        var candidates = ProtectedAppCandidateFactory.FromSnapshots(
            snapshots,
            existingProtectedPaths: [@"C:\Already\added.exe"]);

        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, candidate => candidate.ExecutablePath.Equals(@"D:\Games\Game\game.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(candidates, candidate => candidate.ExecutablePath.Equals(@"C:\Tools\OBS\obs64.exe", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(candidates, candidate => candidate.ExecutablePath.Contains("explorer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FromSnapshots_ExcludesAlreadyProtectedPaths()
    {
        var snapshots = new[]
        {
            new ProcessSnapshot(20, "obs64", 500, false, ExecutablePath: @"C:\Tools\OBS\obs64.exe"),
            new ProcessSnapshot(21, "game", 500, true, ExecutablePath: @"D:\Games\Game\game.exe")
        };

        var candidates = ProtectedAppCandidateFactory.FromSnapshots(
            snapshots,
            existingProtectedPaths: [@"c:\tools\obs\obs64.exe"]);

        Assert.Single(candidates);
        Assert.Equal(@"D:\Games\Game\game.exe", candidates[0].ExecutablePath);
    }
}
