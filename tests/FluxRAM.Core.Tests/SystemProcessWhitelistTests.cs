using FluxRAM.Core.Services;
using Xunit;

namespace FluxRAM.Core.Tests;

public sealed class SystemProcessWhitelistTests
{
    [Fact]
    public void Contains_KernelProcesses_ReturnsTrue()
    {
        Assert.True(SystemProcessWhitelist.Contains("System"));
        Assert.True(SystemProcessWhitelist.Contains("Idle"));
        Assert.True(SystemProcessWhitelist.Contains("dwm"));
        Assert.True(SystemProcessWhitelist.Contains("explorer"));
        Assert.True(SystemProcessWhitelist.Contains("svchost"));
        Assert.True(SystemProcessWhitelist.Contains("ShellExperienceHost"));
    }

    [Fact]
    public void ServiceTargets_ContainsTelemetryCandidates()
    {
        Assert.Contains("DiagTrack", ServiceTargets.WindowsBackgroundServices);
        Assert.Contains("WSearch", ServiceTargets.WindowsBackgroundServices);
    }
}
