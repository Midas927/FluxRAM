using FluxRAM.App.Configuration;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class AppEditionCatalogTests
{
    [Fact]
    public void CurrentEdition_IsAlwaysRuntimeActivatedFreeShell()
    {
        Assert.Equal(AppEdition.Free, AppEditionCatalog.CurrentEdition);
    }

    [Fact]
    public void FreeEdition_DisablesPremiumFeatures()
    {
        var features = AppEditionCatalog.For(AppEdition.Free);

        Assert.Equal("FluxRAM", features.ProductTitle);
        Assert.False(features.SupportsExtremeProfile);
        Assert.False(features.SupportsExtremeClose);
        Assert.True(features.SupportsProtectList);
        Assert.False(features.SupportsAdvancedProtection);
        Assert.Equal("FluxRAM", features.EditionLabelEnglish);
        Assert.Equal("普通版", features.EditionLabelChinese);
    }

    [Fact]
    public void ProEdition_EnablesPremiumFeatures()
    {
        var features = AppEditionCatalog.For(AppEdition.Pro);

        Assert.Equal("FluxRAM Pro", features.ProductTitle);
        Assert.True(features.SupportsExtremeProfile);
        Assert.True(features.SupportsExtremeClose);
        Assert.True(features.SupportsProtectList);
        Assert.True(features.SupportsAdvancedProtection);
        Assert.Equal("FluxRAM Pro", features.EditionLabelEnglish);
        Assert.Equal("专业版", features.EditionLabelChinese);
    }
}
