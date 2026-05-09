using FluxRAM.App.Configuration;
using FluxRAM.App.ViewModels;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class PurchaseOptionsCatalogTests
{
    [Fact]
    public void UsesAlipayFlow_OnlyForSimplifiedChinese()
    {
        Assert.True(PurchaseOptionsCatalog.UsesAlipayFlow(UiLanguage.ChineseSimplified));
        Assert.False(PurchaseOptionsCatalog.UsesAlipayFlow(UiLanguage.English));
        Assert.False(PurchaseOptionsCatalog.UsesAlipayFlow(UiLanguage.ChineseTraditional));
        Assert.False(PurchaseOptionsCatalog.UsesAlipayFlow(UiLanguage.Japanese));
        Assert.False(PurchaseOptionsCatalog.UsesAlipayFlow(UiLanguage.Korean));
    }

    [Fact]
    public void PurchaseAssetsAndWhopUrl_AreConfigured()
    {
        Assert.Equal("FluxRAM Pro：人民币 10 元", PurchaseOptionsCatalog.DomesticPriceText);
        Assert.Equal("FluxRAM Pro: USD $3", PurchaseOptionsCatalog.InternationalPriceText);
        Assert.EndsWith("alipay-qr.jpg", PurchaseOptionsCatalog.AlipayQrImagePath, StringComparison.Ordinal);
        Assert.Equal(3, PurchaseOptionsCatalog.PaymentFlowImagePaths.Count);
        Assert.StartsWith("https://whop.com/", PurchaseOptionsCatalog.WhopPurchaseUrl, StringComparison.OrdinalIgnoreCase);
    }
}
