using FluxRAM.App.ViewModels;
using FluxRAM.Core.Models;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void UpdateBoostMetrics_FormatsTrimmedDisplays()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.UpdateBoostMetrics(
            lastBoostTrimmedBytes: 400 * 1024 * 1024,
            totalTrimmedBytes: 1200 * 1024 * 1024,
            boostNetGainBytes: 300 * 1024 * 1024);

        Assert.Equal("Last Boost Trimmed: +400.0 MB", viewModel.LastBoostTrimmedDisplay);
        Assert.Equal("Total Trimmed: +1.2 GB", viewModel.TotalTrimmedDisplay);
        Assert.Equal("Boost Net Gain: +300.0 MB", viewModel.BoostNetGainDisplay);
    }

    [Fact]
    public void UpdateReboundRate_FormatsDisplay()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.UpdateReboundRate(38.5);

        Assert.Equal("Rebound Rate: 38.5%", viewModel.ReboundRateDisplay);
    }

    [Fact]
    public void SetAutoBoost_UpdatesAutoBoostDisplay()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.SetAutoBoost(true);

        Assert.Equal("Auto Boost: on, pressure-gated", viewModel.AutoBoostDisplay);
    }

    [Fact]
    public void UpdateProtectionSummary_ReflectsEditionAvailability()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.UpdateProtectionSummary(3, supportsProtectList: true);
        Assert.Equal("Protected apps: 3", viewModel.ProtectionSummaryDisplay);

        viewModel.UpdateProtectionSummary(0, supportsProtectList: false);
        Assert.Equal("Protected apps: Pro only", viewModel.ProtectionSummaryDisplay);
    }

    [Fact]
    public void UpdateProProtectionSummary_ShowsTangibleAssociationResults()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.UpdateProProtectionSummary(
            new ProcessProtectionSummary(1, 0, 2, 1),
            isPro: true);

        Assert.Equal(
            "Pro Guard: protected 4 processes (name 1, child 2, related window 1).",
            viewModel.ProProtectionSummaryDisplay);

        viewModel.SetLanguage(UiLanguage.ChineseSimplified);
        Assert.Equal(
            "Pro 守护：已保护 4 个进程（名称 1、子进程 2、关联窗口 1）。",
            viewModel.ProProtectionSummaryDisplay);
    }

    [Fact]
    public void UpdateProtectedEntries_ReplacesEntries()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.UpdateProtectedEntries(["C:\\Apps\\Game\\game.exe", "C:\\Apps\\OBS\\obs64.exe"]);

        Assert.Equal(2, viewModel.ProtectedEntries.Count);
        Assert.Contains("game.exe", viewModel.ProtectedEntries[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetLanguage_SwitchesDisplayToChinese()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.UpdateReboundRate(20);
        viewModel.SetAutoBoost(true);
        viewModel.UpdateProtectionSummary(2, supportsProtectList: true);

        viewModel.SetLanguage(UiLanguage.ChineseSimplified);

        Assert.Equal("回弹率：20.0%", viewModel.ReboundRateDisplay);
        Assert.Equal("自动 Boost：开启，按内存压力触发", viewModel.AutoBoostDisplay);
        Assert.Equal("受保护应用：2", viewModel.ProtectionSummaryDisplay);
    }

    [Fact]
    public void SetLanguage_SwitchesDisplayToJapaneseAndKorean()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.SetAutoBoost(true);
        viewModel.UpdateProtectionSummary(2, supportsProtectList: true);

        viewModel.SetLanguage(UiLanguage.Japanese);
        Assert.Equal("自動 Boost：オン、メモリ圧力で実行", viewModel.AutoBoostDisplay);
        Assert.Equal("保護アプリ：2", viewModel.ProtectionSummaryDisplay);

        viewModel.SetLanguage(UiLanguage.Korean);
        Assert.Equal("자동 Boost: 켜짐, 메모리 압력 기준", viewModel.AutoBoostDisplay);
        Assert.Equal("보호 앱: 2", viewModel.ProtectionSummaryDisplay);
    }

    [Fact]
    public void UpdateSelfOverhead_FormatsOverheadSummary()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.UpdateSelfOverhead(new AppOverheadSnapshot(2.5, 140 * 1024 * 1024, 110 * 1024 * 1024, 450));

        Assert.Contains("CPU 2.5%", viewModel.SelfOverheadDisplay);
        Assert.Contains("WS 140.0 MB", viewModel.SelfOverheadDisplay);
        Assert.Contains("Private 110.0 MB", viewModel.SelfOverheadDisplay);
        Assert.Contains("Handles 450", viewModel.SelfOverheadDisplay);
    }

    [Fact]
    public void AddEvent_CapsHistoryToThirty()
    {
        var viewModel = new MainWindowViewModel();

        for (var index = 1; index <= 35; index += 1)
        {
            viewModel.AddEvent($"Event-{index}");
        }

        Assert.Equal(30, viewModel.RecentEvents.Count);
        Assert.Contains("Event-35", viewModel.RecentEvents[0]);
        Assert.Contains("Event-6", viewModel.RecentEvents[^1]);
    }
}
