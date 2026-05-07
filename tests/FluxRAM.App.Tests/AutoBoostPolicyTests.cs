using FluxRAM.App.Automation;
using FluxRAM.Core.Models;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class AutoBoostPolicyTests
{
    [Fact]
    public void CanRun_ReturnsFalseWhenDisabled()
    {
        var now = DateTimeOffset.UtcNow;

        var canRun = AutoBoostPolicy.CanRun(
            isEnabled: false,
            settings: OptimizerSettings.SafeDefaults(),
            lastAutoBoostAt: null,
            now: now);

        Assert.False(canRun);
    }

    [Fact]
    public void CanRun_ReturnsTrueWhenEnabledAndNeverRun()
    {
        var now = DateTimeOffset.UtcNow;

        var canRun = AutoBoostPolicy.CanRun(
            isEnabled: true,
            settings: OptimizerSettings.SafeDefaults(),
            lastAutoBoostAt: null,
            now: now);

        Assert.True(canRun);
    }

    [Fact]
    public void CanRun_RespectsBoostCooldown()
    {
        var now = DateTimeOffset.UtcNow;
        var settings = OptimizerSettings.SafeDefaults() with
        {
            BoostCooldownSeconds = 120
        };

        Assert.False(AutoBoostPolicy.CanRun(true, settings, now.AddSeconds(-30), now));
        Assert.True(AutoBoostPolicy.CanRun(true, settings, now.AddSeconds(-120), now));
    }
}
