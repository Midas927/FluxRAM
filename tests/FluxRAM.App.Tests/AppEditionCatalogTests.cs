using FluxRAM.App.Configuration;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class AppEditionCatalogTests
{
    [Fact]
    public void CurrentEdition_DefaultsToFreeForRuntimeActivationBuild()
    {
        Assert.Equal(AppEdition.Free, AppEditionCatalog.CurrentEdition);
    }

    [Fact]
    public void FreeEdition_DisablesPremiumFeatures()
    {
        var features = AppEditionCatalog.For(AppEdition.Free);

        Assert.Equal("FluxRAM", features.ProductTitle);
        Assert.False(features.SupportsExtremeProfile);
        Assert.True(features.SupportsProtectList);
        Assert.False(features.SupportsAdvancedProtection);
        Assert.Contains("Auto Boost", features.FeatureSummaryEnglish);
        Assert.Contains("FluxRAM Pro", features.ProIntroductionEnglish);
        Assert.Contains("protected apps", features.ProIntroductionEnglish);
    }

    [Fact]
    public void ProEdition_EnablesPremiumFeatures()
    {
        var features = AppEditionCatalog.For(AppEdition.Pro);

        Assert.Equal("FluxRAM Pro", features.ProductTitle);
        Assert.True(features.SupportsExtremeProfile);
        Assert.True(features.SupportsProtectList);
        Assert.True(features.SupportsAdvancedProtection);
        Assert.Contains("unlocked", features.FeatureSummaryEnglish);
        Assert.Contains("running processes", features.ProIntroductionEnglish);
    }
}
