using FluxRAM.App.Configuration;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class StartupUpdatePolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-version")]
    [InlineData("v0.4.1")]
    public void ShouldPrompt_WhenNewVersionIsNotSkipped_ReturnsTrue(string? skippedVersion)
    {
        Assert.True(StartupUpdatePolicy.ShouldPrompt(Available("v0.4.2"), skippedVersion));
    }

    [Theory]
    [InlineData("v0.4.2")]
    [InlineData("0.4.2")]
    [InlineData(" V0.4.2.0 ")]
    [InlineData("0.4.2+local")]
    public void ShouldPrompt_WhenEquivalentVersionIsSkipped_ReturnsFalse(string skippedVersion)
    {
        Assert.False(StartupUpdatePolicy.ShouldPrompt(Available("v0.4.2"), skippedVersion));
    }

    [Fact]
    public void ShouldPrompt_SkippingOneVersionDoesNotSuppressANewerVersion()
    {
        Assert.False(StartupUpdatePolicy.ShouldPrompt(Available("v0.4.2"), "v0.4.2"));
        Assert.True(StartupUpdatePolicy.ShouldPrompt(Available("v0.4.3"), "v0.4.2"));
    }

    [Fact]
    public void ShouldPrompt_SkippingPrereleaseDoesNotSuppressNextPrereleaseOrStable()
    {
        const string skippedVersion = "v0.4.2-beta.1";

        Assert.False(StartupUpdatePolicy.ShouldPrompt(Available(skippedVersion), skippedVersion));
        Assert.True(StartupUpdatePolicy.ShouldPrompt(Available("v0.4.2-beta.2"), skippedVersion));
        Assert.True(StartupUpdatePolicy.ShouldPrompt(Available("v0.4.2"), skippedVersion));
    }

    [Fact]
    public void ShouldPrompt_LaterDoesNotSuppressTheNextLaunch()
    {
        var result = Available("v0.4.2");

        Assert.Equal(UpdatePromptChoice.Later, default(UpdatePromptChoice));
        Assert.True(StartupUpdatePolicy.ShouldPrompt(result, null));
        Assert.True(StartupUpdatePolicy.ShouldPrompt(result, null));
    }

    [Theory]
    [InlineData(UpdateCheckState.UpToDate)]
    [InlineData(UpdateCheckState.CurrentBuildIsNewer)]
    [InlineData(UpdateCheckState.ReleaseVersionUnavailable)]
    [InlineData(UpdateCheckState.Failed)]
    public void ShouldPrompt_WhenCheckDidNotFindAnUpdate_ReturnsFalse(UpdateCheckState state)
    {
        Assert.False(StartupUpdatePolicy.ShouldPrompt(Available("v0.4.2") with { State = state }, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-version")]
    [InlineData("v0.4.1")]
    [InlineData("v0.4.0")]
    public void ShouldPrompt_WhenLatestVersionIsInvalidOrNotNewer_ReturnsFalse(string? latestVersion)
    {
        Assert.False(StartupUpdatePolicy.ShouldPrompt(Available(latestVersion), null));
    }

    [Fact]
    public void ShouldPrompt_WhenOfflineOrResultIsMalformed_ReturnsFalse()
    {
        var offline = new UpdateCheckResult(UpdateCheckState.Failed, "v0.4.1", null, null, "Offline", []);

        Assert.False(StartupUpdatePolicy.ShouldPrompt(offline, null));
        Assert.False(StartupUpdatePolicy.ShouldPrompt(null, null));
        Assert.False(StartupUpdatePolicy.ShouldPrompt(Available("v0.4.2") with { CurrentVersion = "invalid" }, null));
    }

    private static UpdateCheckResult Available(string? latestVersion) =>
        new(UpdateCheckState.UpdateAvailable, "v0.4.1", latestVersion, null, null, []);
}
