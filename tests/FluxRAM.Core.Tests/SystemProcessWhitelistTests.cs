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
        Assert.True(SystemProcessWhitelist.Contains("TextInputHost"));
        Assert.True(SystemProcessWhitelist.Contains("backgroundTaskHost"));
        Assert.True(SystemProcessWhitelist.Contains("msedgewebview2"));
        Assert.True(SystemProcessWhitelist.Contains("HipsDaemon"));
        Assert.True(SystemProcessWhitelist.Contains("MsMpEng"));
    }

    [Fact]
    public void ServiceTargets_ContainsTelemetryCandidates()
    {
        Assert.Contains("DiagTrack", ServiceTargets.WindowsBackgroundServices);
        Assert.Contains("WSearch", ServiceTargets.WindowsBackgroundServices);
    }

    [Fact]
    public void ServiceTargets_ResolvesDynamicUserServiceNames()
    {
        var candidates = ServiceTargets.ResolveCandidates(new[]
        {
            "CDPUserSvc_4f92a",
            "PimIndexMaintenanceSvc_4f92a",
            "UnrelatedService"
        });

        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, candidate => candidate.ServiceName == "CDPUserSvc_4f92a");
        Assert.Contains(candidates, candidate => candidate.ServiceName == "PimIndexMaintenanceSvc_4f92a");
        Assert.DoesNotContain(candidates, candidate => candidate.ServiceName == "UnrelatedService");
    }

    [Fact]
    public void ServiceTargets_RecognizesDedicatedServiceForCandidateApplication()
    {
        Assert.True(ServiceTargets.IsRelatedApplicationService("MarvisSvr", new[] { "Marvis" }));
        Assert.False(ServiceTargets.IsRelatedApplicationService("UnrelatedService", new[] { "Marvis" }));
        Assert.False(ServiceTargets.IsRelatedApplicationService("AppService", new[] { "App" }));
    }
}
