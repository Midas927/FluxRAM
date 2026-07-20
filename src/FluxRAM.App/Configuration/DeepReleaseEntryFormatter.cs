using FluxRAM.App.ViewModels;

namespace FluxRAM.App.Configuration;

public sealed record DeepReleaseEntryPresentation(string Label, string ToolTip);

public static class DeepReleaseEntryFormatter
{
    public static DeepReleaseEntryPresentation Format(bool isAvailable, UiLanguage language)
    {
        if (isAvailable)
        {
            return new DeepReleaseEntryPresentation(
                Localize(language, "Deep Release", "深度释放"),
                Localize(
                    language,
                    "Review and close idle background applications.",
                    "检查并关闭闲置后台应用。"));
        }

        return new DeepReleaseEntryPresentation(
            Localize(language, "Deep Release · PRO", "深度释放 · PRO"),
            Localize(
                language,
                "Deep Release is included in Pro. Open the edition comparison.",
                "深度释放包含在 Pro 中，点击查看版本区别。"));
    }

    private static string Localize(UiLanguage language, string english, string chinese)
    {
        return UiLanguageLocalizer.Localize(language, english, chinese);
    }
}
