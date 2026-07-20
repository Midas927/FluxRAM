using FluxRAM.App.ViewModels;
using FluxRAM.Core.Models;

namespace FluxRAM.App.Configuration;

public sealed record OptionalServiceDisplay(string Line, string ToolTip);

public static class OptionalServiceDisplayFormatter
{
    public static OptionalServiceDisplay Format(
        OptionalServiceCandidate candidate,
        UiLanguage language)
    {
        var kind = candidate.Kind switch
        {
            OptionalServiceKind.Application => Localize(language, "Application service", "应用服务"),
            _ => Localize(language, "System service", "系统服务")
        };
        var guidance = candidate.StopGuidance switch
        {
            OptionalServiceStopGuidance.WithApplication => Localize(language, "Close with app", "可随应用关闭"),
            OptionalServiceStopGuidance.WhenFeatureUnused => Localize(language, "Optional when unused", "不用该功能时可关"),
            _ => Localize(language, "Keep running", "建议保留")
        };
        var explanation = candidate.StopGuidance switch
        {
            OptionalServiceStopGuidance.WithApplication => Localize(
                language,
                "This service belongs to an application. Stop it only when you no longer need that application.",
                "该服务属于应用程序，仅在暂时不用对应应用时停止。"),
            OptionalServiceStopGuidance.WhenFeatureUnused => Localize(
                language,
                "This Windows feature service can be stopped temporarily when you do not use the related feature.",
                "该服务属于 Windows 可选功能，不使用相关功能时可以临时停止。"),
            _ => Localize(
                language,
                "This Windows service is best kept running because stopping it can affect system features.",
                "该服务与 Windows 系统功能相关，停止后可能影响使用，建议保留。")
        };

        return new OptionalServiceDisplay(
            $"[{kind}] [{guidance}] {candidate.DisplayName}{Environment.NewLine}{candidate.ServiceName}",
            $"{explanation} {Localize(language, "This item is not selected by default.", "该项默认不会勾选。")}");
    }

    private static string Localize(UiLanguage language, string english, string chinese)
    {
        return UiLanguageLocalizer.Localize(language, english, chinese);
    }
}
