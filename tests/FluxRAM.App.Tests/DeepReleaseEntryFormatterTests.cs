using FluxRAM.App.Configuration;
using FluxRAM.App.ViewModels;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class DeepReleaseEntryFormatterTests
{
    [Fact]
    public void Format_FreeEditionShowsProMarkerAndComparisonHint()
    {
        var entry = DeepReleaseEntryFormatter.Format(false, UiLanguage.ChineseSimplified);

        Assert.Equal("深度释放 · PRO", entry.Label);
        Assert.Contains("版本区别", entry.ToolTip);
    }

    [Fact]
    public void Format_ProEditionShowsDirectActionWithoutProMarker()
    {
        var entry = DeepReleaseEntryFormatter.Format(true, UiLanguage.ChineseSimplified);

        Assert.Equal("深度释放", entry.Label);
        Assert.DoesNotContain("PRO", entry.Label, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("闲置后台", entry.ToolTip);
    }
}
