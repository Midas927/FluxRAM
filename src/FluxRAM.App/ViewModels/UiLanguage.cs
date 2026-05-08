namespace FluxRAM.App.ViewModels;

public enum UiLanguage
{
    English = 0,
    ChineseSimplified = 1,
    ChineseTraditional = 2,
    Japanese = 3,
    Korean = 4
}

public sealed record UiLanguageOption(UiLanguage Language, string Code, string DisplayName);

public static class UiLanguageCatalog
{
    public static IReadOnlyList<UiLanguageOption> Options { get; } =
    [
        new(UiLanguage.English, "en", "English"),
        new(UiLanguage.ChineseSimplified, "zh-CN", "简体中文"),
        new(UiLanguage.ChineseTraditional, "zh-TW", "繁體中文"),
        new(UiLanguage.Japanese, "ja", "日本語"),
        new(UiLanguage.Korean, "ko", "한국어")
    ];

    public static UiLanguage FromCode(string? code)
    {
        return Options.FirstOrDefault(option =>
            option.Code.Equals(code, StringComparison.OrdinalIgnoreCase))?.Language ?? UiLanguage.English;
    }

    public static string ToCode(UiLanguage language)
    {
        return Options.FirstOrDefault(option => option.Language == language)?.Code ?? "en";
    }
}

public static class UiLanguageLocalizer
{
    private static readonly IReadOnlyDictionary<string, string> Traditional = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Standby."] = "待命中。",
        ["Processes: waiting for first scan"] = "進程：等待首次掃描",
        ["Foreground: unknown"] = "前台：未知",
        ["Protected apps: 0"] = "受保護應用：0",
        ["FluxRAM Overhead: pending"] = "FluxRAM 開銷：等待資料",
        ["RAM Delta"] = "內存變化",
        ["Available RAM"] = "可用內存",
        ["Last Boost Trimmed"] = "最近 Boost 裁剪量",
        ["Total Trimmed"] = "累計裁剪量",
        ["Boost Net Gain"] = "Boost 淨收益",
        ["Rebound Rate"] = "回彈率",
        ["Protected apps"] = "受保護應用",
        ["Last update"] = "最後更新",
        ["Auto Boost: on, pressure-gated"] = "自動 Boost：開啟，按內存壓力觸發",
        ["Auto Boost: off"] = "自動 Boost：關閉",
        ["Protected apps: Pro only"] = "受保護應用：專業版專屬",
        ["Auto Boost"] = "自動 Boost",
        ["Boost Now"] = "立即 Boost",
        ["Tray Boost"] = "托盤 Boost",
        ["STATUS"] = "狀態",
        ["PROFILE"] = "檔位",
        ["LANGUAGE"] = "語言",
        ["EDITION"] = "版本",
        ["Light"] = "輕量",
        ["Standard"] = "標準",
        ["Extreme Performance"] = "極致性能",
        ["Details"] = "詳細設定",
        ["Hide Details"] = "收起詳情",
        ["Minimize"] = "最小化",
        ["MACHINE ID"] = "機器標識",
        ["Copy"] = "複製",
        ["PRO KEY"] = "專業版 Key",
        ["Activate"] = "啟用",
        ["Protected Apps"] = "受保護應用",
        ["Add EXE"] = "新增 EXE",
        ["Running App"] = "執行中應用",
        ["Remove Selected"] = "刪除所選",
        ["Memory Metrics"] = "內存指標",
        ["Runtime Summary"] = "執行摘要",
        ["Boost Details"] = "Boost 明細",
        ["Recent Activity"] = "最近活動",
        ["LICENSE STATUS"] = "授權狀態",
        ["Edition details"] = "版本功能明細",
        ["Close"] = "關閉",
        ["Open FluxRAM"] = "開啟 FluxRAM",
        ["Exit"] = "退出",
        ["FluxRAM editions"] = "FluxRAM 版本功能",
        ["FluxRAM keeps the everyday workflow generous. Pro adds precision controls for heavier local workloads."] = "FluxRAM 保留日常夠用的核心體驗，Pro 提供更精確的高負載保護能力。",
        ["FluxRAM 普通版"] = "FluxRAM 普通版",
        ["FluxRAM Pro 专业版"] = "FluxRAM Pro 專業版",
        ["- Light / Standard profiles\n- Boost Now and pressure-based Auto Boost\n- Tray Boost and minimize-to-tray workflow\n- Add / remove protected apps\n- Choose protected apps from running processes\n- Basic process-name protection and clear memory metrics"] =
            "- 輕量 / 標準檔位\n- 立即 Boost 與按內存壓力觸發的自動 Boost\n- 托盤 Boost 與最小化托盤工作流\n- 新增 / 刪除受保護應用\n- 從正在執行的進程中選擇保護應用\n- 基礎進程名保護與清晰內存指標",
        ["- Everything in FluxRAM\n- Extreme Performance profile\n- Exact EXE path protection\n- Child-process association protection\n- Visible-window recognition for target apps\n- Permanent activation on the current machine"] =
            "- 包含 FluxRAM 普通版全部功能\n- 極致性能檔位\n- 精確 EXE 路徑保護\n- 子進程關聯保護\n- 目標應用可見視窗識別\n- 當前電腦永久啟用",
        ["Simplified boost-first memory tool for local Windows workloads"] = "面向本機 Windows 負載的精簡 Boost 優先內存工具",
        ["Basic name protection"] = "基礎進程名保護",
        ["process name only"] = "僅進程名",
        ["Pro advanced protection"] = "Pro 高級保護",
        ["path + child + window"] = "路徑 + 子進程 + 視窗",
        ["Pro advanced protection: exact path, child process and window recognition are active."] = "Pro 高級保護：精確路徑、子進程與視窗識別已啟用。",
        ["Basic protection: process name only. Pro also protects exact paths, child processes and matching windows."] = "基礎保護：僅按進程名保護。Pro 另可保護精確路徑、子進程和匹配視窗。",
        ["Language switched."] = "語言已切換。"
    };

    private static readonly IReadOnlyDictionary<string, string> Japanese = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Standby."] = "待機中。",
        ["Processes: waiting for first scan"] = "プロセス：初回スキャン待ち",
        ["Foreground: unknown"] = "前面：不明",
        ["Protected apps: 0"] = "保護アプリ：0",
        ["FluxRAM Overhead: pending"] = "FluxRAM オーバーヘッド：待機中",
        ["RAM Delta"] = "メモリ変化",
        ["Available RAM"] = "利用可能メモリ",
        ["Last Boost Trimmed"] = "直近 Boost 削減量",
        ["Total Trimmed"] = "累計削減量",
        ["Boost Net Gain"] = "Boost 純増",
        ["Rebound Rate"] = "リバウンド率",
        ["Protected apps"] = "保護アプリ",
        ["Last update"] = "最終更新",
        ["Auto Boost: on, pressure-gated"] = "自動 Boost：オン、メモリ圧力で実行",
        ["Auto Boost: off"] = "自動 Boost：オフ",
        ["Protected apps: Pro only"] = "保護アプリ：Pro 専用",
        ["Auto Boost"] = "自動 Boost",
        ["Boost Now"] = "今すぐ Boost",
        ["Tray Boost"] = "トレイ Boost",
        ["STATUS"] = "状態",
        ["PROFILE"] = "モード",
        ["LANGUAGE"] = "言語",
        ["EDITION"] = "エディション",
        ["Light"] = "軽量",
        ["Standard"] = "標準",
        ["Extreme Performance"] = "極限性能",
        ["Details"] = "詳細設定",
        ["Hide Details"] = "詳細を閉じる",
        ["Minimize"] = "最小化",
        ["MACHINE ID"] = "マシン ID",
        ["Copy"] = "コピー",
        ["PRO KEY"] = "Pro Key",
        ["Activate"] = "有効化",
        ["Protected Apps"] = "保護アプリ",
        ["Add EXE"] = "EXE を追加",
        ["Running App"] = "実行中アプリ",
        ["Remove Selected"] = "選択を削除",
        ["Memory Metrics"] = "メモリ指標",
        ["Runtime Summary"] = "実行概要",
        ["Boost Details"] = "Boost 詳細",
        ["Recent Activity"] = "最近の活動",
        ["LICENSE STATUS"] = "ライセンス状態",
        ["Edition details"] = "エディション詳細",
        ["Close"] = "閉じる",
        ["Open FluxRAM"] = "FluxRAM を開く",
        ["Exit"] = "終了",
        ["FluxRAM editions"] = "FluxRAM エディション",
        ["FluxRAM keeps the everyday workflow generous. Pro adds precision controls for heavier local workloads."] = "FluxRAM は日常利用に十分な機能を提供し、Pro は重いローカル作業向けに精密な制御を追加します。",
        ["FluxRAM 普通版"] = "FluxRAM 通常版",
        ["FluxRAM Pro 专业版"] = "FluxRAM Pro",
        ["- Light / Standard profiles\n- Boost Now and pressure-based Auto Boost\n- Tray Boost and minimize-to-tray workflow\n- Add / remove protected apps\n- Choose protected apps from running processes\n- Basic process-name protection and clear memory metrics"] =
            "- 軽量 / 標準モード\n- 今すぐ Boost とメモリ圧力による自動 Boost\n- トレイ Boost と最小化トレイ運用\n- 保護アプリの追加 / 削除\n- 実行中プロセスから保護アプリを選択\n- 基本のプロセス名保護と明確なメモリ指標",
        ["- Everything in FluxRAM\n- Extreme Performance profile\n- Exact EXE path protection\n- Child-process association protection\n- Visible-window recognition for target apps\n- Permanent activation on the current machine"] =
            "- FluxRAM 通常版の全機能\n- 極限性能モード\n- 正確な EXE パス保護\n- 子プロセス関連保護\n- 対象アプリの可視ウィンドウ認識\n- 現在の PC で永続的に有効化",
        ["Simplified boost-first memory tool for local Windows workloads"] = "ローカル Windows ワークロード向けの Boost 優先メモリツール",
        ["Basic name protection"] = "基本のプロセス名保護",
        ["process name only"] = "プロセス名のみ",
        ["Pro advanced protection"] = "Pro 高度保護",
        ["path + child + window"] = "パス + 子プロセス + ウィンドウ",
        ["Pro advanced protection: exact path, child process and window recognition are active."] = "Pro 高度保護：正確なパス、子プロセス、ウィンドウ認識が有効です。",
        ["Basic protection: process name only. Pro also protects exact paths, child processes and matching windows."] = "基本保護：プロセス名のみ。Pro は正確なパス、子プロセス、一致するウィンドウも保護します。",
        ["Language switched."] = "言語を切り替えました。"
    };

    private static readonly IReadOnlyDictionary<string, string> Korean = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Standby."] = "대기 중.",
        ["Processes: waiting for first scan"] = "프로세스: 첫 스캔 대기 중",
        ["Foreground: unknown"] = "전면: 알 수 없음",
        ["Protected apps: 0"] = "보호 앱: 0",
        ["FluxRAM Overhead: pending"] = "FluxRAM 오버헤드: 대기 중",
        ["RAM Delta"] = "메모리 변화",
        ["Available RAM"] = "사용 가능 메모리",
        ["Last Boost Trimmed"] = "최근 Boost 정리량",
        ["Total Trimmed"] = "누적 정리량",
        ["Boost Net Gain"] = "Boost 순증가",
        ["Rebound Rate"] = "리바운드율",
        ["Protected apps"] = "보호 앱",
        ["Last update"] = "마지막 업데이트",
        ["Auto Boost: on, pressure-gated"] = "자동 Boost: 켜짐, 메모리 압력 기준",
        ["Auto Boost: off"] = "자동 Boost: 꺼짐",
        ["Protected apps: Pro only"] = "보호 앱: Pro 전용",
        ["Auto Boost"] = "자동 Boost",
        ["Boost Now"] = "지금 Boost",
        ["Tray Boost"] = "트레이 Boost",
        ["STATUS"] = "상태",
        ["PROFILE"] = "모드",
        ["LANGUAGE"] = "언어",
        ["EDITION"] = "에디션",
        ["Light"] = "가벼움",
        ["Standard"] = "표준",
        ["Extreme Performance"] = "극한 성능",
        ["Details"] = "세부 설정",
        ["Hide Details"] = "세부 정보 접기",
        ["Minimize"] = "최소화",
        ["MACHINE ID"] = "기기 식별자",
        ["Copy"] = "복사",
        ["PRO KEY"] = "Pro Key",
        ["Activate"] = "활성화",
        ["Protected Apps"] = "보호 앱",
        ["Add EXE"] = "EXE 추가",
        ["Running App"] = "실행 중 앱",
        ["Remove Selected"] = "선택 삭제",
        ["Memory Metrics"] = "메모리 지표",
        ["Runtime Summary"] = "실행 요약",
        ["Boost Details"] = "Boost 세부 정보",
        ["Recent Activity"] = "최근 활동",
        ["LICENSE STATUS"] = "라이선스 상태",
        ["Edition details"] = "에디션 세부 정보",
        ["Close"] = "닫기",
        ["Open FluxRAM"] = "FluxRAM 열기",
        ["Exit"] = "종료",
        ["FluxRAM editions"] = "FluxRAM 에디션",
        ["FluxRAM keeps the everyday workflow generous. Pro adds precision controls for heavier local workloads."] = "FluxRAM은 일상 사용에 충분한 기능을 제공하고, Pro는 무거운 로컬 작업을 위한 정밀 제어를 추가합니다.",
        ["FluxRAM 普通版"] = "FluxRAM 일반판",
        ["FluxRAM Pro 专业版"] = "FluxRAM Pro",
        ["- Light / Standard profiles\n- Boost Now and pressure-based Auto Boost\n- Tray Boost and minimize-to-tray workflow\n- Add / remove protected apps\n- Choose protected apps from running processes\n- Basic process-name protection and clear memory metrics"] =
            "- 가벼움 / 표준 모드\n- 즉시 Boost 및 메모리 압력 기반 자동 Boost\n- 트레이 Boost 및 최소화 트레이 흐름\n- 보호 앱 추가 / 삭제\n- 실행 중 프로세스에서 보호 앱 선택\n- 기본 프로세스 이름 보호와 명확한 메모리 지표",
        ["- Everything in FluxRAM\n- Extreme Performance profile\n- Exact EXE path protection\n- Child-process association protection\n- Visible-window recognition for target apps\n- Permanent activation on the current machine"] =
            "- FluxRAM 일반판의 모든 기능\n- 극한 성능 모드\n- 정확한 EXE 경로 보호\n- 자식 프로세스 연관 보호\n- 대상 앱의 보이는 창 인식\n- 현재 PC에서 영구 활성화",
        ["Simplified boost-first memory tool for local Windows workloads"] = "로컬 Windows 작업을 위한 Boost 우선 메모리 도구",
        ["Basic name protection"] = "기본 프로세스 이름 보호",
        ["process name only"] = "프로세스 이름만",
        ["Pro advanced protection"] = "Pro 고급 보호",
        ["path + child + window"] = "경로 + 자식 프로세스 + 창",
        ["Pro advanced protection: exact path, child process and window recognition are active."] = "Pro 고급 보호: 정확한 경로, 자식 프로세스, 창 인식이 활성화되었습니다.",
        ["Basic protection: process name only. Pro also protects exact paths, child processes and matching windows."] = "기본 보호: 프로세스 이름만 보호합니다. Pro는 정확한 경로, 자식 프로세스, 일치하는 창도 보호합니다.",
        ["Language switched."] = "언어가 변경되었습니다."
    };

    public static string Localize(UiLanguage language, string english, string chineseSimplified)
    {
        return language switch
        {
            UiLanguage.ChineseSimplified => chineseSimplified,
            UiLanguage.ChineseTraditional => Traditional.TryGetValue(english, out var value) ? value : chineseSimplified,
            UiLanguage.Japanese => Japanese.TryGetValue(english, out var value) ? value : english,
            UiLanguage.Korean => Korean.TryGetValue(english, out var value) ? value : english,
            _ => english
        };
    }

    public static string LocalizeLabel(UiLanguage language, string englishLabel, string chineseLabel)
    {
        return Localize(language, englishLabel, chineseLabel);
    }
}
