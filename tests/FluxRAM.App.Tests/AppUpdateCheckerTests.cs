using FluxRAM.App.Configuration;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class AppUpdateCheckerTests
{
    [Theory]
    [InlineData("0.3.5", "v0.3.6", UpdateVersionComparison.LatestIsNewer)]
    [InlineData("0.3.5", "0.3.5", UpdateVersionComparison.Same)]
    [InlineData("0.3.5", "v0.3.4", UpdateVersionComparison.CurrentIsNewer)]
    [InlineData("0.3.5+local", "v0.3.5", UpdateVersionComparison.Same)]
    [InlineData("0.3.5", "not-a-version", UpdateVersionComparison.Unknown)]
    public void CompareReleaseVersions_HandlesReleaseTags(
        string currentVersion,
        string latestVersion,
        UpdateVersionComparison expected)
    {
        Assert.Equal(expected, AppUpdateChecker.CompareReleaseVersions(currentVersion, latestVersion));
    }
}
