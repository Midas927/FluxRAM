using System.Collections.Generic;

namespace FluxRAM.App.Configuration;

public sealed class EditionDetailsSection
{
    public EditionDetailsSection(
        string titleEnglish,
        string titleChinese,
        string bodyEnglish,
        string bodyChinese)
    {
        TitleEnglish = titleEnglish;
        TitleChinese = titleChinese;
        BodyEnglish = bodyEnglish;
        BodyChinese = bodyChinese;
    }

    public string TitleEnglish { get; }

    public string TitleChinese { get; }

    public string BodyEnglish { get; }

    public string BodyChinese { get; }
}

public static class EditionDetailsCatalog
{
    public static string DialogTitleEnglish => "FluxRAM editions";

    public static string DialogTitleChinese => "FluxRAM 版本功能";

    public static string DialogSubtitleEnglish =>
        "FluxRAM keeps the everyday workflow generous. Pro adds stronger controls for heavier local workloads.";

    public static string DialogSubtitleChinese =>
        "FluxRAM 保留日常够用的核心体验，Pro 提供更强的重负载控制能力。";

    public static IReadOnlyList<EditionDetailsSection> Sections { get; } = new[]
    {
        new EditionDetailsSection(
            titleEnglish: "FluxRAM",
            titleChinese: "FluxRAM 普通版",
            bodyEnglish:
                "- Daily and Gaming profiles\n" +
                "- Boost Now and pressure-based Auto Boost\n" +
                "- Tray Boost and minimize-to-tray workflow\n" +
                "- Add / remove protected apps\n" +
                "- Choose protected apps from running processes\n" +
                "- Basic process-name protection and clear memory metrics",
            bodyChinese:
                "- Daily / Gaming 两种模式\n" +
                "- 立即 Boost 与按内存压力触发的 Auto Boost\n" +
                "- 托盘 Boost 与最小化托盘工作流\n" +
                "- 添加 / 删除受保护应用\n" +
                "- 从正在运行的进程中选择保护应用\n" +
                "- 基础进程名保护与清晰内存指标"),
        new EditionDetailsSection(
            titleEnglish: "FluxRAM Pro",
            titleChinese: "FluxRAM Pro 专业版",
            bodyEnglish:
                "- Everything in FluxRAM\n" +
                "- Extreme profile\n" +
                "- Deep Release with app selection and confirmation\n" +
                "- Exact EXE path protection\n" +
                "- Child-process association protection\n" +
                "- Smart association protection for related apps\n" +
                "- Permanent activation on the current machine",
            bodyChinese:
                "- 包含 FluxRAM 普通版全部功能\n" +
                "- Extreme 模式\n" +
                "- 选择应用并确认执行的深度释放\n" +
                "- 精确 EXE 路径保护\n" +
                "- 子进程与关联应用的智能关联保护\n" +
                "- 当前电脑永久激活")
    };
}
