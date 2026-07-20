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
                EnableGamingProcessProtection: false,
                MinimumColdnessScore: 65d,
                BoostCooldownSeconds: 120,
                MinimumGroupedProcessWorkingSetBytes: 24L * 1024 * 1024),

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
                EnableGamingProcessProtection: false,
                MinimumColdnessScore: 55d,
                BoostCooldownSeconds: 120,
                MinimumGroupedProcessWorkingSetBytes: 12L * 1024 * 1024),

            OptimizerProfile.GamingHandheld => new OptimizerSettings(
                MaxPurgeTargetsPerPass: 7,
                MinimumCandidateWorkingSetBytes: 96L * 1024 * 1024,
                PurgeWhenAvailableMemoryBelowBytes: 12UL * 1024 * 1024 * 1024,
                PurgeWhenAvailableMemoryBelowPercentOfTotal: 48,
                IgnoreMemoryPressureThreshold: false,
                AllowForegroundProcessPurge: false,
                ProcessCooldownSeconds: 18,
                NormalIntervalSeconds: 4,
                BackoffIntervalSeconds: 10,
                LowYieldThresholdBytes: 24L * 1024 * 1024,
                LowYieldPassesBeforeBackoff: 5,
                EnablePriorityAdjustment: false,
                EnableServiceKiller: false,
                EnableGamingProcessProtection: true,
                MinimumColdnessScore: 45d,
                BoostCooldownSeconds: 90,
                MinimumGroupedProcessWorkingSetBytes: 8L * 1024 * 1024),

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
                EnableGamingProcessProtection: false,
                MinimumColdnessScore: 20d,
                BoostCooldownSeconds: 120,
                MinimumGroupedProcessWorkingSetBytes: 4L * 1024 * 1024),

            _ => FromProfile(OptimizerProfile.Conservative)
        };
    }

    public static string ToDisplayName(OptimizerProfile profile)
    {
        return profile switch
        {
            OptimizerProfile.Conservative => "Daily",
            OptimizerProfile.Balanced => "Gaming",
            OptimizerProfile.GamingHandheld => "Gaming",
            OptimizerProfile.Aggressive => "Extreme",
            _ => "Gaming"
        };
    }
}
