using System.Globalization;
using FluxRAM.App.ViewModels;
using FluxRAM.Core.Models;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class ReadableMetricsTests
{
    [Fact]
    public void MemorySample_SeparatesPhysicalMemoryFromWorkingSetTrimming()
    {
        var vm = new MainWindowViewModel();
        Assert.Equal("--", vm.AvailableRamValue);
        vm.UpdateMemorySnapshot(new MemorySnapshot(4UL << 30, 16UL << 30, 75));
        vm.UpdateBoostMetrics(100L << 20, 200L << 20, -20L << 20);
        Assert.Equal("4.0 GB", vm.AvailableRamValue);
        Assert.Equal("75%", vm.MemoryLoadValue);
        Assert.Equal("Used 12.0 GB / Total 16.0 GB", vm.PhysicalMemoryDisplay);
        Assert.Equal("+100.0 MB", vm.LastBoostTrimmedValue);
        Assert.Equal("-20.0 MB", vm.BoostNetGainValue);
    }

    [Theory]
    [InlineData("app.exe | Path protection | C:\\Program Files\\App\\app.exe", "app.exe", "Path protection | C:\\Program Files\\App\\app.exe")]
    [InlineData("12:00:00  Scan finished", "12:00:00", "Scan finished")]
    [InlineData("No eligible candidates", "No eligible candidates", "")]
    public void DetailPresentation_KeepsCompleteText(string input, string title, string body)
    {
        var converter = new DetailTextConverter();
        Assert.Equal(title, converter.Convert(input, typeof(string), "Title", CultureInfo.InvariantCulture));
        Assert.Equal(body, converter.Convert(input, typeof(string), "Body", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ScanWithoutAPlan_DoesNotReportZeroEligibleApps()
    {
        var vm = new MainWindowViewModel();
        vm.UpdateProcessMetrics(100, null, "app.exe");
        Assert.Equal("Processes: scanned 100", vm.ProcessSummaryDisplay);
        vm.UpdateProcessMetrics(100, 0, "app.exe");
        Assert.Contains("candidate apps 0", vm.ProcessSummaryDisplay);
    }
}
