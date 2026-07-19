namespace FluxRAM.Core.Models;

public sealed record OptimizerSettings(
    int MaxPurgeTargetsPerPass,
    long MinimumCandidateWorkingSetBytes,
    ulong PurgeWhenAvailableMemoryBelowBytes,
    int PurgeWhenAvailableMemoryBelowPercentOfTotal,
    bool IgnoreMemoryPressureThreshold,
    bool AllowForegroundProcessPurge,
    int ProcessCooldownSeconds,
    int NormalIntervalSeconds,
    int BackoffIntervalSeconds,
    long LowYieldThresholdBytes,
    int LowYieldPassesBeforeBackoff,
    bool EnablePriorityAdjustment,
    bool EnableServiceKiller,
    bool EnableGamingProcessProtection = false,
    double MinimumColdnessScore = 55d,
    int BoostCooldownSeconds = 120)
{
    public static OptimizerSettings SafeDefaults()
    {
        return new OptimizerSettings(
            MaxPurgeTargetsPerPass: 3,
            MinimumCandidateWorkingSetBytes: 220L * 1024 * 1024,
            PurgeWhenAvailableMemoryBelowBytes: 7UL * 1024 * 1024 * 1024,
            PurgeWhenAvailableMemoryBelowPercentOfTotal: 32,
            IgnoreMemoryPressureThreshold: false,
            AllowForegroundProcessPurge: false,
            ProcessCooldownSeconds: 75,
            NormalIntervalSeconds: 8,
            BackoffIntervalSeconds: 20,
            LowYieldThresholdBytes: 64L * 1024 * 1024,
            LowYieldPassesBeforeBackoff: 3,
            EnablePriorityAdjustment: false,
            EnableServiceKiller: false,
            EnableGamingProcessProtection: false,
            MinimumColdnessScore: 55d,
            BoostCooldownSeconds: 120);
    }
}
