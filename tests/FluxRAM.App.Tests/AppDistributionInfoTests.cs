using FluxRAM.App.Configuration;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class AppDistributionInfoTests
{
    [Theory]
    [InlineData("Portable", AppDistributionMode.Portable)]
    [InlineData("portable", AppDistributionMode.Portable)]
    [InlineData("Lite", AppDistributionMode.Lite)]
    [InlineData("", AppDistributionMode.Lite)]
    [InlineData(null, AppDistributionMode.Lite)]
    public void ParseMode_UsesLiteAsTheConservativeFallback(string? value, AppDistributionMode expected)
    {
        Assert.Equal(expected, AppDistributionInfo.ParseMode(value));
    }

    [Theory]
    [InlineData(AppDistributionMode.Lite, "FluxRAM-Lite-Windows-x64.zip")]
    [InlineData(AppDistributionMode.Portable, "FluxRAM-Portable-Windows-x64.zip")]
    public void SelectAsset_ReturnsTheMatchingPackage(AppDistributionMode mode, string expectedName)
    {
        var assets = new[]
        {
            new AppUpdateAsset("FluxRAM-Portable-Windows-x64.zip", new Uri("https://example.test/portable"), new string('b', 64)),
            new AppUpdateAsset("FluxRAM-Lite-Windows-x64.zip", new Uri("https://example.test/lite"), new string('a', 64))
        };

        var selected = AppDistributionInfo.SelectAsset(assets, mode);

        Assert.NotNull(selected);
        Assert.Equal(expectedName, selected.Name);
    }

    [Theory]
    [InlineData(AppDistributionMode.Lite, "FluxRAM-0.3.8-beta.2-Lite-Windows-x64.zip")]
    [InlineData(AppDistributionMode.Portable, "FluxRAM-0.3.8-beta.2-Portable-Windows-x64.zip")]
    public void SelectAsset_AcceptsVersionedBetaPackageNames(AppDistributionMode mode, string expectedName)
    {
        var assets = new[]
        {
            new AppUpdateAsset(
                "FluxRAM-0.3.8-beta.2-Portable-Windows-x64.zip",
                new Uri("https://example.test/portable"),
                new string('b', 64)),
            new AppUpdateAsset(
                "FluxRAM-0.3.8-beta.2-Lite-Windows-x64.zip",
                new Uri("https://example.test/lite"),
                new string('a', 64))
        };

        var selected = AppDistributionInfo.SelectAsset(assets, mode);

        Assert.NotNull(selected);
        Assert.Equal(expectedName, selected.Name);
    }
}
