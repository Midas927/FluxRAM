using FluxRAM.Core.Models;

namespace FluxRAM.App.Automation;

public static class AutoBoostPolicy
{
    public static bool CanRun(
        bool isEnabled,
        OptimizerSettings settings,
        DateTimeOffset? lastAutoBoostAt,
        DateTimeOffset now)
    {
        if (!isEnabled)
        {
            return false;
        }

        if (!lastAutoBoostAt.HasValue)
        {
            return true;
        }

        var cooldown = TimeSpan.FromSeconds(Math.Max(15, settings.BoostCooldownSeconds));
        return now - lastAutoBoostAt.Value >= cooldown;
    }
}
