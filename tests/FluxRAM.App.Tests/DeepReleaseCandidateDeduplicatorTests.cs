using FluxRAM.App.Configuration;
using FluxRAM.Core.Models;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class DeepReleaseCandidateDeduplicatorTests
{
    [Fact]
    public void RemoveServiceDuplicates_RemovesDedicatedServiceProcessGroup()
    {
        var applications = new[]
        {
            Candidate("Marvis", 10, 11),
            Candidate("MarvisSvr", 20, 21)
        };
        var services = new[]
        {
            new OptionalServiceCandidate("MarvisSvr", "MarvisSvr", 20)
        };

        var result = DeepReleaseCandidateDeduplicator.RemoveServiceDuplicates(applications, services);

        var candidate = Assert.Single(result);
        Assert.Equal("Marvis", candidate.ProcessName);
    }

    private static ExtremeCloseCandidate Candidate(string processName, params int[] processIds)
    {
        return new ExtremeCloseCandidate(
            processName,
            processIds,
            100L * 1024 * 1024,
            0,
            0,
            false,
            false,
            false);
    }
}
