using FluxRAM.Core.Models;

namespace FluxRAM.Core.Services;

public static class OptimizerSettingsCatalog
{
    public static OptimizerSettings FromProfile(OptimizerProfile profile)
    {
        return profile switch
        {
            OptimizerProfile.Conservative => new OptimizerSettings(
                MaxPurgeTargetsPerPass: 2,
                MinimumCandidateWorkingSetBytes: 280L * 1024 * 1024,
                PurgeWhenAvailableMemoryBelowBytes: 5UL * 1024 * 1024 * 1024,
                PurgeWhenAvailableMemoryBelowPercentOfTotal: 26,
                IgnoreMemoryPressureThreshold: false,
                AllowForegroundProcessPurge: false,
                ProcessCooldownSeconds: 60,
                NormalIntervalSeconds: 8,
                BackoffIntervalSeconds: 18,
                LowYieldThresholdBytes: 96L * 1024 * 1024,
                LowYieldPassesBeforeBackoff: 2,
                EnablePriorityAdjustment: false,
                EnableServiceKiller: false,
                MinimumColdnessScore: 65d,
                BoostCooldownSeconds: 120),

            OptimizerProfile.Balanced => new OptimizerSettings(
                MaxPurgeTargetsPerPass: 5,
                MinimumCandidateWorkingSetBytes: 160L * 1024 * 1024,
                PurgeWhenAvailableMemoryBelowBytes: 9UL * 1024 * 1024 * 1024,
                PurgeWhenAvailableMemoryBelowPercentOfTotal: 40,
                IgnoreMemoryPressureThreshold: false,
                AllowForegroundProcessPurge: false,
                ProcessCooldownSeconds: 24,
                NormalIntervalSeconds: 5,
                BackoffIntervalSeconds: 12,
                LowYieldThresholdBytes: 40L * 1024 * 1024,
                LowYieldPassesBeforeBackoff: 4,
                EnablePriorityAdjustment: false,
                EnableServiceKiller: false,
                MinimumColdnessScore: 55d,
                BoostCooldownSeconds: 120),

            OptimizerProfile.Aggressive => new OptimizerSettings(
                MaxPurgeTargetsPerPass: 0,
                MinimumCandidateWorkingSetBytes: 64L * 1024 * 1024,
                PurgeWhenAvailableMemoryBelowBytes: 0,
                PurgeWhenAvailableMemoryBelowPercentOfTotal: 0,
                IgnoreMemoryPressureThreshold: true,
                AllowForegroundProcessPurge: true,
                ProcessCooldownSeconds: 0,
                NormalIntervalSeconds: 1,
                BackoffIntervalSeconds: 1,
                LowYieldThresholdBytes: 0,
                LowYieldPassesBeforeBackoff: int.MaxValue,
                EnablePriorityAdjustment: false,
                EnableServiceKiller: false,
                MinimumColdnessScore: 20d,
                BoostCooldownSeconds: 120),

            _ => FromProfile(OptimizerProfile.Conservative)
        };
    }

    public static string ToDisplayName(OptimizerProfile profile)
    {
        return profile switch
        {
            OptimizerProfile.Conservative => "Light",
            OptimizerProfile.Balanced => "Standard",
            OptimizerProfile.Aggressive => "Extreme Performance",
            _ => "Light"
        };
    }
}
