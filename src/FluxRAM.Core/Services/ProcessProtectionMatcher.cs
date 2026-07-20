using FluxRAM.Core.Models;

namespace FluxRAM.Core.Services;

public enum ProcessProtectionMatch
{
    None,
    ProcessName,
    ExactPath,
    ChildProcess,
    RelatedWindow
}

public sealed class ProcessProtectionContext
{
    internal ProcessProtectionContext(
        IReadOnlySet<string> processNames,
        IReadOnlySet<string> paths,
        IReadOnlySet<string> titleTokens,
        IReadOnlySet<int> rootProcessIds,
        IReadOnlyDictionary<int, int?> parentProcessIds)
    {
        ProcessNames = processNames;
        Paths = paths;
        TitleTokens = titleTokens;
        RootProcessIds = rootProcessIds;
        ParentProcessIds = parentProcessIds;
    }

    internal IReadOnlySet<string> ProcessNames { get; }

    internal IReadOnlySet<string> Paths { get; }

    internal IReadOnlySet<string> TitleTokens { get; }

    internal IReadOnlySet<int> RootProcessIds { get; }

    internal IReadOnlyDictionary<int, int?> ParentProcessIds { get; }
}

public static class ProcessProtectionMatcher
{
    private const int MinimumTitleTokenLength = 6;

    private static readonly HashSet<string> BrowserProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "brave",
        "chrome",
        "firefox",
        "iexplore",
        "msedge",
        "opera",
        "vivaldi"
    };

    private static readonly HashSet<string> GenericTitleTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "app",
        "client",
        "game",
        "helper",
        "launcher",
        "setup",
        "update",
        "updater"
    };

    private static readonly HashSet<string> RelatedWindowHostNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "applicationframehost",
        "bootstrapper",
        "client",
        "helper",
        "host",
        "launcher",
        "setup",
        "update",
        "updater"
    };

    public static ProcessProtectionContext CreateContext(
        IReadOnlyList<ProcessSnapshot> snapshots,
        IReadOnlyCollection<string>? protectedProcessNames,
        IReadOnlyCollection<string>? protectedProcessPaths)
    {
        var processNames = NormalizeProcessNames(protectedProcessNames);
        var paths = NormalizePaths(protectedProcessPaths);
        var titleTokens = BuildTitleTokens(processNames, paths);
        var rootProcessIds = snapshots
            .Where(snapshot =>
                processNames.Contains(NormalizeProcessName(snapshot.ProcessName)) ||
                IsExactPathMatch(snapshot.ExecutablePath, paths))
            .Select(snapshot => snapshot.ProcessId)
            .ToHashSet();
        var parentProcessIds = snapshots
            .GroupBy(snapshot => snapshot.ProcessId)
            .ToDictionary(group => group.Key, group => group.First().ParentProcessId);

        return new ProcessProtectionContext(
            processNames,
            paths,
            titleTokens,
            rootProcessIds,
            parentProcessIds);
    }

    public static ProcessProtectionMatch Match(
        ProcessSnapshot snapshot,
        ProcessProtectionContext context,
        bool enableAdvancedProtection)
    {
        if (context.ProcessNames.Contains(NormalizeProcessName(snapshot.ProcessName)))
        {
            return ProcessProtectionMatch.ProcessName;
        }

        if (!enableAdvancedProtection)
        {
            return ProcessProtectionMatch.None;
        }

        if (IsExactPathMatch(snapshot.ExecutablePath, context.Paths))
        {
            return ProcessProtectionMatch.ExactPath;
        }

        if (IsDescendantOfProtectedRoot(snapshot, context.RootProcessIds, context.ParentProcessIds))
        {
            return ProcessProtectionMatch.ChildProcess;
        }

        return IsRelatedWindow(snapshot, context.TitleTokens)
            ? ProcessProtectionMatch.RelatedWindow
            : ProcessProtectionMatch.None;
    }

    public static ProcessProtectionSummary Summarize(
        IReadOnlyList<ProcessSnapshot> snapshots,
        ProcessProtectionContext context,
        bool enableAdvancedProtection)
    {
        var processNameCount = 0;
        var exactPathCount = 0;
        var childProcessCount = 0;
        var relatedWindowCount = 0;

        foreach (var snapshot in snapshots)
        {
            switch (Match(snapshot, context, enableAdvancedProtection))
            {
                case ProcessProtectionMatch.ProcessName:
                    processNameCount += 1;
                    break;
                case ProcessProtectionMatch.ExactPath:
                    exactPathCount += 1;
                    break;
                case ProcessProtectionMatch.ChildProcess:
                    childProcessCount += 1;
                    break;
                case ProcessProtectionMatch.RelatedWindow:
                    relatedWindowCount += 1;
                    break;
            }
        }

        return new ProcessProtectionSummary(
            processNameCount,
            exactPathCount,
            childProcessCount,
            relatedWindowCount);
    }

    private static bool IsDescendantOfProtectedRoot(
        ProcessSnapshot snapshot,
        IReadOnlySet<int> protectedRootProcessIds,
        IReadOnlyDictionary<int, int?> parentProcessIds)
    {
        var seenProcessIds = new HashSet<int> { snapshot.ProcessId };
        var parentProcessId = snapshot.ParentProcessId;
        for (var depth = 0; depth < 16 && parentProcessId.HasValue; depth += 1)
        {
            if (protectedRootProcessIds.Contains(parentProcessId.Value))
            {
                return true;
            }

            if (!seenProcessIds.Add(parentProcessId.Value))
            {
                return false;
            }

            parentProcessId = parentProcessIds.TryGetValue(parentProcessId.Value, out var nextParentProcessId)
                ? nextParentProcessId
                : null;
        }

        return false;
    }

    private static bool IsRelatedWindow(ProcessSnapshot snapshot, IReadOnlySet<string> titleTokens)
    {
        if (!snapshot.HasVisibleWindow ||
            string.IsNullOrWhiteSpace(snapshot.MainWindowTitle) ||
            titleTokens.Count == 0 ||
            BrowserProcessNames.Contains(NormalizeProcessName(snapshot.ProcessName)) ||
            !IsRelatedWindowHost(snapshot.ProcessName))
        {
            return false;
        }

        var normalizedTitle = NormalizeSearchToken(snapshot.MainWindowTitle);
        return titleTokens.Any(token => normalizedTitle.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRelatedWindowHost(string? processName)
    {
        var normalized = NormalizeProcessName(processName);
        return RelatedWindowHostNames.Contains(normalized) ||
            normalized.EndsWith("bootstrapper", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("helper", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("host", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("launcher", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("updater", StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> NormalizeProcessNames(IReadOnlyCollection<string>? values)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (values is null)
        {
            return result;
        }

        foreach (var value in values)
        {
            var normalized = NormalizeProcessName(value);
            if (normalized.Length > 0)
            {
                result.Add(normalized);
            }
        }

        return result;
    }

    private static HashSet<string> NormalizePaths(IReadOnlyCollection<string>? values)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (values is null)
        {
            return result;
        }

        foreach (var value in values)
        {
            var normalized = NormalizePath(value);
            if (normalized.Length > 0)
            {
                result.Add(normalized);
            }
        }

        return result;
    }

    private static HashSet<string> BuildTitleTokens(
        IReadOnlySet<string> processNames,
        IReadOnlySet<string> paths)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var processName in processNames)
        {
            AddTitleToken(result, processName);
        }

        foreach (var path in paths)
        {
            AddTitleToken(result, Path.GetFileNameWithoutExtension(path));
            AddTitleToken(result, Path.GetFileName(Path.GetDirectoryName(path)));
        }

        return result;
    }

    private static void AddTitleToken(ISet<string> tokens, string? value)
    {
        var token = NormalizeSearchToken(value);
        if (token.Length >= MinimumTitleTokenLength && !GenericTitleTokens.Contains(token))
        {
            tokens.Add(token);
        }
    }

    private static bool IsExactPathMatch(string? executablePath, IReadOnlySet<string> protectedPaths)
    {
        return !string.IsNullOrWhiteSpace(executablePath) &&
            protectedPaths.Contains(NormalizePath(executablePath));
    }

    private static string NormalizeProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return string.Empty;
        }

        var normalized = processName.Trim();
        return normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^4].ToLowerInvariant()
            : normalized.ToLowerInvariant();
    }

    private static string NormalizeSearchToken(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private static string NormalizePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().Replace('/', '\\').ToLowerInvariant();
    }
}
