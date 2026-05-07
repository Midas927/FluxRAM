using FluxRAM.Core.Models;
using FluxRAM.Core.Services;
using Xunit;

namespace FluxRAM.Core.Tests;

public sealed class OptimizerSettingsCatalogTests
{
    [Fact]
    public void FromProfile_ReturnsExpectedDefaults()
    {
        var conservative = OptimizerSettingsCatalog.FromProfile(OptimizerProfile.Conservative);
        var balanced = OptimizerSettingsCatalog.FromProfile(OptimizerProfile.Balanced);
        var aggressive = OptimizerSettingsCatalog.FromProfile(OptimizerProfile.Aggressive);

        Assert.True(conservative.MaxPurgeTargetsPerPass < balanced.MaxPurgeTargetsPerPass);
        Assert.True(conservative.NormalIntervalSeconds > aggressive.NormalIntervalSeconds);
        Assert.True(conservative.MinimumCandidateWorkingSetBytes > aggressive.MinimumCandidateWorkingSetBytes);
        Assert.True(aggressive.ProcessCooldownSeconds < balanced.ProcessCooldownSeconds);
        Assert.True(aggressive.IgnoreMemoryPressureThreshold);
        Assert.True(aggressive.AllowForegroundProcessPurge);
        Assert.Equal(0, aggressive.MaxPurgeTargetsPerPass);
        Assert.False(conservative.EnablePriorityAdjustment);
        Assert.False(balanced.EnablePriorityAdjustment);
        Assert.False(aggressive.EnablePriorityAdjustment);
        Assert.False(conservative.EnableServiceKiller);
        Assert.False(balanced.EnableServiceKiller);
        Assert.False(aggressive.EnableServiceKiller);
        Assert.True(conservative.MinimumColdnessScore > aggressive.MinimumColdnessScore);
        Assert.Equal(120, conservative.BoostCooldownSeconds);
        Assert.Equal(120, balanced.BoostCooldownSeconds);
        Assert.Equal(120, aggressive.BoostCooldownSeconds);
    }

    [Fact]
    public void ToDisplayName_MapsProfiles()
    {
        Assert.Equal("Light", OptimizerSettingsCatalog.ToDisplayName(OptimizerProfile.Conservative));
        Assert.Equal("Standard", OptimizerSettingsCatalog.ToDisplayName(OptimizerProfile.Balanced));
        Assert.Equal("Extreme Performance", OptimizerSettingsCatalog.ToDisplayName(OptimizerProfile.Aggressive));
    }
}
