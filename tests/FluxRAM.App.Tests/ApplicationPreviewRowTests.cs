using Xunit;

namespace FluxRAM.App.Tests;

public sealed class ApplicationPreviewRowTests
{
    [Fact]
    public void MissingMeasurementIsNotDisplayedAsZeroRelease()
    {
        var vm = new FluxRAM.App.ViewModels.MainWindowViewModel();
        vm.UpdateBoostMetrics(0, 1024, 0, hasTrimMeasurement: false, hasNetMeasurement: false);
        Assert.Equal("--", vm.LastBoostTrimmedValue);
        Assert.Equal("--", vm.BoostNetGainValue);
        Assert.Contains("--", vm.ReboundRateDisplay);
        Assert.Equal("+1.0 KB", vm.TotalTrimmedValue);
        vm.UpdateBoostMetrics(0, 1024, 0);
        Assert.Equal("+0 B", vm.LastBoostTrimmedValue);
        Assert.Equal("+0 B", vm.BoostNetGainValue);
    }

    [Theory]
    [InlineData(" APP ", false, true)]
    [InlineData("c:\\tools", false, true)]
    [InlineData("unknown", false, false)]
    [InlineData("", true, false)]
    public void SearchIncludesRejectedApplicationsWithoutMakingThemEligible(string query, bool onlyEligible, bool expected)
    {
        var row = new ApplicationPreviewRow(@"My App | Protected | C:\Tools\app.exe", false);
        Assert.Equal(expected, row.Matches(query, onlyEligible));
    }
}
