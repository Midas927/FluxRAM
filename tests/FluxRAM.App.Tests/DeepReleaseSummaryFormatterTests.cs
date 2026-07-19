using FluxRAM.App.Configuration;
using FluxRAM.App.ViewModels;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class DeepReleaseSummaryFormatterTests
{
    [Fact]
    public void FormatSelection_ShowsSelectedAppsAndEstimatedMemory()
    {
        var candidates = new[]
        {
            new ExtremeCloseCandidate("chrome", new[] { 1, 2 }, 800L * 1024 * 1024, 0, 0, false, true, true),
            new ExtremeCloseCandidate("discord", new[] { 3 }, 300L * 1024 * 1024, 0, 0, false, true, true)
        };

        Assert.Equal(
            "Selected 2 apps | Estimated memory 1.1 GB",
            DeepReleaseSummaryFormatter.FormatSelection(candidates, UiLanguage.English));
        Assert.Equal(
            "已选择 2 个应用 | 预计释放 1.1 GB",
            DeepReleaseSummaryFormatter.FormatSelection(candidates, UiLanguage.ChineseSimplified));
    }

    [Fact]
    public void FormatSelection_HandlesEmptySelection()
    {
        Assert.Equal(
            "No app selected",
            DeepReleaseSummaryFormatter.FormatSelection(Array.Empty<ExtremeCloseCandidate>(), UiLanguage.English));
    }
}
