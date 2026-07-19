using FluxRAM.Core.Models;
using FluxRAM.Core.Services;
using Xunit;

namespace FluxRAM.Core.Tests;

public sealed class ProcessProtectionMatcherTests
{
    [Fact]
    public void Analyze_DistinguishesNameChildAndRelatedWindowMatches()
    {
        var snapshots = new[]
        {
            new ProcessSnapshot(10, "FluxQuest", 500, false, ExecutablePath: @"D:\Games\FluxQuest\FluxQuest.exe"),
            new ProcessSnapshot(11, "helper", 400, false, ParentProcessId: 10),
            new ProcessSnapshot(12, "launcher", 300, false, HasVisibleWindow: true, MainWindowTitle: "Flux Quest Settings"),
            new ProcessSnapshot(13, "discord", 200, false)
        };
        var context = ProcessProtectionMatcher.CreateContext(
            snapshots,
            new[] { "FluxQuest" },
            new[] { @"D:\Games\FluxQuest\FluxQuest.exe" });

        var summary = ProcessProtectionMatcher.Summarize(snapshots, context, enableAdvancedProtection: true);

        Assert.Equal(1, summary.ProcessNameCount);
        Assert.Equal(1, summary.ChildProcessCount);
        Assert.Equal(1, summary.RelatedWindowCount);
        Assert.Equal(3, summary.TotalCount);
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("discord")]
    public void Match_DoesNotAssociateUnrelatedAppByItsWindowTitle(string processName)
    {
        var snapshots = new[]
        {
            new ProcessSnapshot(20, processName, 500, false, HasVisibleWindow: true, MainWindowTitle: "Flux Quest Community")
        };
        var context = ProcessProtectionMatcher.CreateContext(
            snapshots,
            new[] { "FluxQuest" },
            new[] { @"D:\Games\FluxQuest\FluxQuest.exe" });

        var match = ProcessProtectionMatcher.Match(snapshots[0], context, enableAdvancedProtection: true);

        Assert.Equal(ProcessProtectionMatch.None, match);
    }

    [Fact]
    public void Match_DoesNotUseShortOrGenericWindowTokens()
    {
        var snapshots = new[]
        {
            new ProcessSnapshot(30, "launcher", 500, false, HasVisibleWindow: true, MainWindowTitle: "Game Setup")
        };
        var context = ProcessProtectionMatcher.CreateContext(
            snapshots,
            new[] { "game" },
            new[] { @"D:\Games\Game\game.exe" });

        var match = ProcessProtectionMatcher.Match(snapshots[0], context, enableAdvancedProtection: true);

        Assert.Equal(ProcessProtectionMatch.None, match);
    }
}
