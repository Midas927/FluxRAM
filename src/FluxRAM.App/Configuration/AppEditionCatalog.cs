namespace FluxRAM.App.Configuration;

public enum AppEdition
{
    Free,
    Pro
}

public sealed class AppEditionFeatures
{
    public AppEditionFeatures(
        AppEdition edition,
        string productTitle,
        string editionLabelEnglish,
        string editionLabelChinese,
        bool supportsProtectList,
        bool supportsAdvancedProtection,
        bool supportsExtremeProfile)
    {
        Edition = edition;
        ProductTitle = productTitle;
        EditionLabelEnglish = editionLabelEnglish;
        EditionLabelChinese = editionLabelChinese;
        SupportsProtectList = supportsProtectList;
        SupportsAdvancedProtection = supportsAdvancedProtection;
        SupportsExtremeProfile = supportsExtremeProfile;
    }

    public AppEdition Edition { get; }

    public string ProductTitle { get; }

    public string EditionLabelEnglish { get; }

    public string EditionLabelChinese { get; }

    public bool SupportsProtectList { get; }

    public bool SupportsAdvancedProtection { get; }

    public bool SupportsExtremeProfile { get; }
}

public static class AppEditionCatalog
{
    public static AppEdition CurrentEdition => AppEdition.Free;

    public static AppEditionFeatures Current => For(CurrentEdition);

    public static AppEditionFeatures For(AppEdition edition)
    {
        return edition switch
        {
            AppEdition.Free => new AppEditionFeatures(
                edition: AppEdition.Free,
                productTitle: "FluxRAM",
                editionLabelEnglish: "FluxRAM",
                editionLabelChinese: "普通版",
                supportsProtectList: true,
                supportsAdvancedProtection: false,
                supportsExtremeProfile: false),
            AppEdition.Pro => new AppEditionFeatures(
                edition: AppEdition.Pro,
                productTitle: "FluxRAM Pro",
                editionLabelEnglish: "FluxRAM Pro",
                editionLabelChinese: "专业版",
                supportsProtectList: true,
                supportsAdvancedProtection: true,
                supportsExtremeProfile: true),
            _ => For(AppEdition.Pro)
        };
    }
}
