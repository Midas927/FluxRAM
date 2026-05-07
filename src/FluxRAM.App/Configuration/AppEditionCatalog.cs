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
        string featureSummaryEnglish,
        string featureSummaryChinese,
        string proIntroductionEnglish,
        string proIntroductionChinese,
        bool supportsProtectList,
        bool supportsAdvancedProtection,
        bool supportsExtremeProfile)
    {
        Edition = edition;
        ProductTitle = productTitle;
        EditionLabelEnglish = editionLabelEnglish;
        EditionLabelChinese = editionLabelChinese;
        FeatureSummaryEnglish = featureSummaryEnglish;
        FeatureSummaryChinese = featureSummaryChinese;
        ProIntroductionEnglish = proIntroductionEnglish;
        ProIntroductionChinese = proIntroductionChinese;
        SupportsProtectList = supportsProtectList;
        SupportsAdvancedProtection = supportsAdvancedProtection;
        SupportsExtremeProfile = supportsExtremeProfile;
    }

    public AppEdition Edition { get; }

    public string ProductTitle { get; }

    public string EditionLabelEnglish { get; }

    public string EditionLabelChinese { get; }

    public string FeatureSummaryEnglish { get; }

    public string FeatureSummaryChinese { get; }

    public string ProIntroductionEnglish { get; }

    public string ProIntroductionChinese { get; }

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
                featureSummaryEnglish: "Light, Standard, Auto Boost, tray Boost and basic protected apps included.",
                featureSummaryChinese: "已包含轻量、标准、自动 Boost、托盘 Boost 与基础应用保护。",
                proIntroductionEnglish: "FluxRAM Pro upgrades protected apps with exact-path protection, child-process association, window recognition and Extreme Performance.",
                proIntroductionChinese: "FluxRAM Pro 将受保护应用升级为精确路径保护、子进程关联、窗口识别与极致性能。",
                supportsProtectList: true,
                supportsAdvancedProtection: false,
                supportsExtremeProfile: false),
            AppEdition.Pro => new AppEditionFeatures(
                edition: AppEdition.Pro,
                productTitle: "FluxRAM Pro",
                editionLabelEnglish: "FluxRAM Pro",
                editionLabelChinese: "专业版",
                featureSummaryEnglish: "Extreme Performance and advanced app protection are unlocked.",
                featureSummaryChinese: "已解锁极致性能与高级应用保护。",
                proIntroductionEnglish: "Add protected apps from EXE files or running processes; Pro skips exact paths, related child processes and matching visible app windows.",
                proIntroductionChinese: "可从 EXE 或运行中进程添加受保护应用；Pro 会跳过精确路径、关联子进程和匹配的可见应用窗口。",
                supportsProtectList: true,
                supportsAdvancedProtection: true,
                supportsExtremeProfile: true),
            _ => For(AppEdition.Pro)
        };
    }
}
