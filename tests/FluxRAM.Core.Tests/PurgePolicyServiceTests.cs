using FluxRAM.Core.Models;
using FluxRAM.Core.Services;
using Xunit;

namespace FluxRAM.Core.Tests;

public sealed class PurgePolicyServiceTests
{
    [Fact]
    public void CreatePlan_WhenMemoryPressureIsLow_ReturnsSkipPlan()
    {
        var service = new PurgePolicyService();
        var settings = OptimizerSettings.SafeDefaults();
        var snapshots = new[]
        {
            new ProcessSnapshot(1, "chrome", 900L * 1024 * 1024, false, ColdnessScore: 80)
        };
        var memorySnapshot = new MemorySnapshot(
            AvailablePhysicalMemoryBytes: settings.PurgeWhenAvailableMemoryBelowBytes + 1,
            TotalPhysicalMemoryBytes: 32UL * 1024 * 1024 * 1024,
            MemoryLoadPercent: 35);

        var plan = service.CreatePlan(
            snapshots,
            memorySnapshot,
            settings,
            DateTimeOffset.UtcNow,
            new Dictionary<int, DateTimeOffset>());

        Assert.False(plan.ShouldPurge);
        Assert.Empty(plan.Candidates);
    }

    [Fact]
    public void CreatePlan_SelectsColdProcesses_AndAppliesLimit()
    {
        var service = new PurgePolicyService();
        var settings = OptimizerSettings.SafeDefaults() with
        {
            MaxPurgeTargetsPerPass = 2,
            MinimumCandidateWorkingSetBytes = 150L * 1024 * 1024,
            PurgeWhenAvailableMemoryBelowBytes = 8UL * 1024 * 1024 * 1024,
            PurgeWhenAvailableMemoryBelowPercentOfTotal = 80,
            MinimumColdnessScore = 50
        };

        var snapshots = new[]
        {
            new ProcessSnapshot(101, "browser", 700L * 1024 * 1024, false, ColdnessScore: 82),
            new ProcessSnapshot(102, "editor", 300L * 1024 * 1024, false, ColdnessScore: 75),
            new ProcessSnapshot(103, "music", 500L * 1024 * 1024, false, ColdnessScore: 40),
            new ProcessSnapshot(104, "game", 1400L * 1024 * 1024, true, ColdnessScore: 90)
        };
        var memorySnapshot = new MemorySnapshot(
            AvailablePhysicalMemoryBytes: 2UL * 1024 * 1024 * 1024,
            TotalPhysicalMemoryBytes: 16UL * 1024 * 1024 * 1024,
            MemoryLoadPercent: 79);

        var plan = service.CreatePlan(
            snapshots,
            memorySnapshot,
            settings,
            DateTimeOffset.UtcNow,
            new Dictionary<int, DateTimeOffset>());

        Assert.True(plan.ShouldPurge);
        Assert.Equal(2, plan.Candidates.Count);
        Assert.Equal(101, plan.Candidates[0].ProcessId);
        Assert.Equal(102, plan.Candidates[1].ProcessId);
    }

    [Fact]
    public void CreatePlan_RespectsProtectList()
    {
        var service = new PurgePolicyService();
        var settings = OptimizerSettings.SafeDefaults() with
        {
            IgnoreMemoryPressureThreshold = true,
            MinimumCandidateWorkingSetBytes = 100L * 1024 * 1024,
            MinimumColdnessScore = 30
        };
        var snapshots = new[]
        {
            new ProcessSnapshot(201, "chrome", 500L * 1024 * 1024, false, ColdnessScore: 90),
            new ProcessSnapshot(202, "discord", 420L * 1024 * 1024, false, ColdnessScore: 88)
        };

        var plan = service.CreatePlan(
            snapshots,
            new MemorySnapshot(2UL * 1024 * 1024 * 1024, 16UL * 1024 * 1024 * 1024, 80),
            settings,
            DateTimeOffset.UtcNow,
            new Dictionary<int, DateTimeOffset>(),
            protectedProcessNames: new[] { "chrome.exe" });

        Assert.True(plan.ShouldPurge);
        Assert.Single(plan.Candidates);
        Assert.Equal(202, plan.Candidates[0].ProcessId);
    }

    [Fact]
    public void CreatePlan_RespectsProtectedPathList()
    {
        var service = new PurgePolicyService();
        var settings = OptimizerSettings.SafeDefaults() with
        {
            IgnoreMemoryPressureThreshold = true,
            MinimumColdnessScore = 20
        };
        var snapshots = new[]
        {
            new ProcessSnapshot(210, "obs64", 520L * 1024 * 1024, false, ColdnessScore: 90, ExecutablePath: @"C:\Tools\OBS\obs64.exe"),
            new ProcessSnapshot(211, "discord", 410L * 1024 * 1024, false, ColdnessScore: 88, ExecutablePath: @"C:\Users\me\AppData\Local\Discord\discord.exe")
        };

        var plan = service.CreatePlan(
            snapshots,
            new MemorySnapshot(2UL * 1024 * 1024 * 1024, 16UL * 1024 * 1024 * 1024, 80),
            settings,
            DateTimeOffset.UtcNow,
            new Dictionary<int, DateTimeOffset>(),
            protectedProcessPaths: new[] { @"C:\Tools\OBS\obs64.exe" });

        Assert.True(plan.ShouldPurge);
        Assert.Single(plan.Candidates);
        Assert.Equal(211, plan.Candidates[0].ProcessId);
    }

    [Fact]
    public void CreatePlan_BasicProtection_DoesNotProtectDifferentProcessWithSamePath()
    {
        var service = new PurgePolicyService();
        var settings = OptimizerSettings.SafeDefaults() with
        {
            IgnoreMemoryPressureThreshold = true,
            MinimumColdnessScore = 20
        };
        var snapshots = new[]
        {
            new ProcessSnapshot(220, "helper", 520L * 1024 * 1024, false, ColdnessScore: 90, ExecutablePath: @"C:\Tools\OBS\obs64.exe"),
            new ProcessSnapshot(221, "discord", 410L * 1024 * 1024, false, ColdnessScore: 88, ExecutablePath: @"C:\Users\me\AppData\Local\Discord\discord.exe")
        };

        var plan = service.CreatePlan(
            snapshots,
            new MemorySnapshot(2UL * 1024 * 1024 * 1024, 16UL * 1024 * 1024 * 1024, 80),
            settings,
            DateTimeOffset.UtcNow,
            new Dictionary<int, DateTimeOffset>(),
            protectedProcessNames: new[] { "obs64" },
            protectedProcessPaths: new[] { @"C:\Tools\OBS\obs64.exe" },
            enableAdvancedProtection: false);

        Assert.True(plan.ShouldPurge);
        Assert.Equal(2, plan.Candidates.Count);
    }

    [Fact]
    public void CreatePlan_AdvancedProtection_ProtectsChildProcessOfProtectedRoot()
    {
        var service = new PurgePolicyService();
        var settings = OptimizerSettings.SafeDefaults() with
        {
            IgnoreMemoryPressureThreshold = true,
            MinimumColdnessScore = 20
        };
        var snapshots = new[]
        {
            new ProcessSnapshot(230, "game", 900L * 1024 * 1024, false, ColdnessScore: 90, ExecutablePath: @"D:\Games\Game\game.exe"),
            new ProcessSnapshot(231, "helper", 450L * 1024 * 1024, false, ColdnessScore: 88, ExecutablePath: @"D:\Games\Game\helper.exe", ParentProcessId: 230),
            new ProcessSnapshot(232, "discord", 410L * 1024 * 1024, false, ColdnessScore: 86, ExecutablePath: @"C:\Users\me\AppData\Local\Discord\discord.exe")
        };

        var plan = service.CreatePlan(
            snapshots,
            new MemorySnapshot(2UL * 1024 * 1024 * 1024, 16UL * 1024 * 1024 * 1024, 80),
            settings,
            DateTimeOffset.UtcNow,
            new Dictionary<int, DateTimeOffset>(),
            protectedProcessNames: new[] { "game" },
            protectedProcessPaths: new[] { @"D:\Games\Game\game.exe" },
            enableAdvancedProtection: true);

        Assert.True(plan.ShouldPurge);
        Assert.Single(plan.Candidates);
        Assert.Equal(232, plan.Candidates[0].ProcessId);
    }

    [Fact]
    public void CreatePlan_AdvancedProtection_ProtectsVisibleWindowTitleMatch()
    {
        var service = new PurgePolicyService();
        var settings = OptimizerSettings.SafeDefaults() with
        {
            IgnoreMemoryPressureThreshold = true,
            MinimumColdnessScore = 20
        };
        var snapshots = new[]
        {
            new ProcessSnapshot(240, "launcher", 520L * 1024 * 1024, false, HasVisibleWindow: true, ColdnessScore: 90, ExecutablePath: @"D:\Launchers\launcher.exe", MainWindowTitle: "Flux Quest Settings"),
            new ProcessSnapshot(241, "discord", 410L * 1024 * 1024, false, ColdnessScore: 88, ExecutablePath: @"C:\Users\me\AppData\Local\Discord\discord.exe")
        };

        var plan = service.CreatePlan(
            snapshots,
            new MemorySnapshot(2UL * 1024 * 1024 * 1024, 16UL * 1024 * 1024 * 1024, 80),
            settings,
            DateTimeOffset.UtcNow,
            new Dictionary<int, DateTimeOffset>(),
            protectedProcessNames: new[] { "FluxQuest" },
            protectedProcessPaths: new[] { @"D:\Games\FluxQuest\FluxQuest.exe" },
            enableAdvancedProtection: true);

        Assert.True(plan.ShouldPurge);
        Assert.Single(plan.Candidates);
        Assert.Equal(241, plan.Candidates[0].ProcessId);
    }

    [Fact]
    public void CreatePlan_RespectsCooldownAndSkipsRecentPurge()
    {
        var service = new PurgePolicyService();
        var settings = OptimizerSettings.SafeDefaults() with
        {
            ProcessCooldownSeconds = 60,
            PurgeWhenAvailableMemoryBelowBytes = 8UL * 1024 * 1024 * 1024,
            PurgeWhenAvailableMemoryBelowPercentOfTotal = 80,
            MinimumColdnessScore = 40
        };

        var now = DateTimeOffset.UtcNow;
        var snapshots = new[]
        {
            new ProcessSnapshot(301, "browser", 700L * 1024 * 1024, false, ColdnessScore: 91),
            new ProcessSnapshot(302, "editor", 600L * 1024 * 1024, false, ColdnessScore: 70)
        };
        var memorySnapshot = new MemorySnapshot(
            AvailablePhysicalMemoryBytes: 1UL * 1024 * 1024 * 1024,
            TotalPhysicalMemoryBytes: 16UL * 1024 * 1024 * 1024,
            MemoryLoadPercent: 88);
        var cooldownMap = new Dictionary<int, DateTimeOffset>
        {
            [301] = now.AddSeconds(-20)
        };

        var plan = service.CreatePlan(
            snapshots,
            memorySnapshot,
            settings,
            now,
            cooldownMap);

        Assert.True(plan.ShouldPurge);
        Assert.Single(plan.Candidates);
        Assert.Equal(302, plan.Candidates[0].ProcessId);
    }

    [Fact]
    public void CreatePlan_WhenForced_IgnoresMemoryThreshold()
    {
        var service = new PurgePolicyService();
        var settings = OptimizerSettings.SafeDefaults();
        var now = DateTimeOffset.UtcNow;
        var snapshots = new[]
        {
            new ProcessSnapshot(401, "browser", 800L * 1024 * 1024, false, ColdnessScore: 86)
        };
        var memorySnapshot = new MemorySnapshot(
            AvailablePhysicalMemoryBytes: settings.PurgeWhenAvailableMemoryBelowBytes + 4UL * 1024 * 1024 * 1024,
            TotalPhysicalMemoryBytes: 16UL * 1024 * 1024 * 1024,
            MemoryLoadPercent: 35);

        var plan = service.CreatePlan(
            snapshots,
            memorySnapshot,
            settings,
            now,
            new Dictionary<int, DateTimeOffset>(),
            forcePurge: true);

        Assert.True(plan.ShouldPurge);
        Assert.Single(plan.Candidates);
        Assert.Equal(401, plan.Candidates[0].ProcessId);
        Assert.Contains("Boost Now plan", plan.DecisionMessage);
    }

    [Fact]
    public void CreatePlan_WhenThresholdBypassed_AllowsPurgeWithHighAvailableMemory()
    {
        var service = new PurgePolicyService();
        var settings = OptimizerSettings.SafeDefaults() with
        {
            IgnoreMemoryPressureThreshold = true,
            MinimumCandidateWorkingSetBytes = 64L * 1024 * 1024,
            MinimumColdnessScore = 20
        };

        var snapshots = new[]
        {
            new ProcessSnapshot(501, "browser", 500L * 1024 * 1024, false, ColdnessScore: 45)
        };

        var memorySnapshot = new MemorySnapshot(
            AvailablePhysicalMemoryBytes: 20UL * 1024 * 1024 * 1024,
            TotalPhysicalMemoryBytes: 32UL * 1024 * 1024 * 1024,
            MemoryLoadPercent: 28);

        var plan = service.CreatePlan(
            snapshots,
            memorySnapshot,
            settings,
            DateTimeOffset.UtcNow,
            new Dictionary<int, DateTimeOffset>());

        Assert.True(plan.ShouldPurge);
        Assert.Single(plan.Candidates);
        Assert.Contains("bypassed threshold", plan.DecisionMessage);
    }

    [Fact]
    public void CreatePlan_WhenMaxTargetsIsZero_TakesAllEligibleCandidates()
    {
        var service = new PurgePolicyService();
        var settings = OptimizerSettings.SafeDefaults() with
        {
            IgnoreMemoryPressureThreshold = true,
            MaxPurgeTargetsPerPass = 0,
            MinimumCandidateWorkingSetBytes = 64L * 1024 * 1024,
            AllowForegroundProcessPurge = true,
            MinimumColdnessScore = 0
        };

        var snapshots = new[]
        {
            new ProcessSnapshot(601, "browser", 700L * 1024 * 1024, false, ColdnessScore: 82),
            new ProcessSnapshot(602, "editor", 400L * 1024 * 1024, false, ColdnessScore: 68),
            new ProcessSnapshot(603, "game", 300L * 1024 * 1024, true, ColdnessScore: 63)
        };

        var plan = service.CreatePlan(
            snapshots,
            new MemorySnapshot(12UL * 1024 * 1024 * 1024, 16UL * 1024 * 1024 * 1024, 40),
            settings,
            DateTimeOffset.UtcNow,
            new Dictionary<int, DateTimeOffset>());

        Assert.True(plan.ShouldPurge);
        Assert.Equal(3, plan.Candidates.Count);
    }
}
