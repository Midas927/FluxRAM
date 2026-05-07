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
        "FluxRAM keeps the everyday workflow generous. Pro adds precision controls for heavier local workloads.";

    public static string DialogSubtitleChinese =>
        "FluxRAM 保留日常够用的核心体验，Pro 提供更精确的高负载保护能力。";

    public static IReadOnlyList<EditionDetailsSection> Sections { get; } = new[]
    {
        new EditionDetailsSection(
            titleEnglish: "FluxRAM",
            titleChinese: "FluxRAM 普通版",
            bodyEnglish:
                "- Light / Standard profiles\n" +
                "- Boost Now and pressure-based Auto Boost\n" +
                "- Tray Boost and minimize-to-tray workflow\n" +
                "- Add / remove protected apps\n" +
                "- Choose protected apps from running processes\n" +
                "- Basic process-name protection and clear memory metrics",
            bodyChinese:
                "- 轻量 / 标准档位\n" +
                "- 立即 Boost 与按压力触发的自动 Boost\n" +
                "- 托盘 Boost 与最小化托盘工作流\n" +
                "- 添加 / 删除受保护应用\n" +
                "- 从正在运行的进程里选择保护应用\n" +
                "- 基础进程名保护与清晰内存指标"),
        new EditionDetailsSection(
            titleEnglish: "FluxRAM Pro",
            titleChinese: "FluxRAM Pro 专业版",
            bodyEnglish:
                "- Everything in FluxRAM\n" +
                "- Extreme Performance profile\n" +
                "- Exact EXE path protection\n" +
                "- Child-process association protection\n" +
                "- Visible-window recognition for target apps\n" +
                "- Permanent activation on the current machine",
            bodyChinese:
                "- 包含 FluxRAM 普通版全部功能\n" +
                "- 极致性能档位\n" +
                "- 精确 EXE 路径保护\n" +
                "- 子进程关联保护\n" +
                "- 目标应用可见窗口识别\n" +
                "- 当前电脑永久激活")
    };
}
