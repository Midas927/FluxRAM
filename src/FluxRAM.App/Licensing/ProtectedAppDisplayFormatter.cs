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
            ? UiLanguageLocalizer.Localize(language, "Pro advanced protection", "Pro 高级保护")
            : UiLanguageLocalizer.Localize(language, "Basic name protection", "基础进程名保护");
        var detail = enableAdvancedProtection
            ? UiLanguageLocalizer.Localize(language, "path + child + window", "路径 + 子进程 + 窗口")
            : UiLanguageLocalizer.Localize(language, "process name only", "仅进程名");

        return protectedPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new ProtectedAppDisplayEntry(path, $"{Path.GetFileName(path)} | {mode}: {detail} | {path}"))
            .ToArray();
    }
}
