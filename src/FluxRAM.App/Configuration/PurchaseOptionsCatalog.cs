using FluxRAM.App.ViewModels;

namespace FluxRAM.App.Configuration;

public static class PurchaseOptionsCatalog
{
    public const string DomesticPriceText = "FluxRAM Pro：人民币 10 元";

    public const string InternationalPriceText = "FluxRAM Pro: USD $3";

    public const string WhopPurchaseUrl = "https://whop.com/fluxram/";

    public const string AlipayQrImagePath = "Assets/PurchasePro/alipay-qr.jpg";

    public static IReadOnlyList<string> PaymentFlowImagePaths { get; } =
    [
        "Assets/PurchasePro/step-1.jpg",
        "Assets/PurchasePro/step-2.jpg",
        "Assets/PurchasePro/step-3.jpg"
    ];

    public static bool UsesAlipayFlow(UiLanguage language)
    {
        return language == UiLanguage.ChineseSimplified;
    }
}
