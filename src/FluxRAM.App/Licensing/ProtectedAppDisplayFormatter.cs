using System.IO;
using FluxRAM.App.ViewModels;

namespace FluxRAM.App.Licensing;

public sealed record ProtectedAppDisplayEntry(string Path, string DisplayText);

public static class ProtectedAppDisplayFormatter
{
    public static IReadOnlyList<ProtectedAppDisplayEntry> Format(
        IReadOnlyCollection<string> protectedPaths,
        bool enableAdvancedProtection,
        UiLanguage language)
    {
        var mode = enableAdvancedProtection
            ? UiLanguageLocalizer.Localize(language, "Smart association protection", "智能关联保护")
            : UiLanguageLocalizer.Localize(language, "Basic name protection", "基础进程名保护");
        var detail = enableAdvancedProtection
            ? UiLanguageLocalizer.Localize(language, "exact path + child + related app", "精确路径 + 子进程 + 关联应用")
            : UiLanguageLocalizer.Localize(language, "process name only", "仅进程名");

        return protectedPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new ProtectedAppDisplayEntry(path, $"{Path.GetFileName(path)} | {mode}: {detail} | {path}"))
            .ToArray();
    }
}
