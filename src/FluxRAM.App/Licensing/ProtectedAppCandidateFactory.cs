using FluxRAM.Core.Models;
using FluxRAM.Core.Services;

namespace FluxRAM.App.Licensing;

public sealed record ProtectedAppCandidate(
    string ProcessName,
    string ExecutablePath,
    long WorkingSetBytes,
    bool IsForeground)
{
    public string DisplayText
    {
        get
        {
            var focus = IsForeground ? " | foreground" : string.Empty;
            return $"{ProcessName}.exe | {FormatBytes(WorkingSetBytes)}{focus} | {ExecutablePath}";
        }
    }

    public override string ToString() => DisplayText;

    private static string FormatBytes(long bytes)
    {
        var absoluteBytes = Math.Abs((double)bytes);
        if (absoluteBytes >= 1024d * 1024d * 1024d)
        {
            return $"{bytes / (1024d * 1024d * 1024d):0.0} GB";
        }

        if (absoluteBytes >= 1024d * 1024d)
        {
            return $"{bytes / (1024d * 1024d):0.0} MB";
        }

        if (absoluteBytes >= 1024d)
        {
            return $"{bytes / 1024d:0.0} KB";
        }

        return $"{bytes} B";
    }
}

public static class ProtectedAppCandidateFactory
{
    public static IReadOnlyList<ProtectedAppCandidate> FromSnapshots(
        IReadOnlyList<ProcessSnapshot> snapshots,
        IReadOnlyCollection<string> existingProtectedPaths)
    {
        var existing = existingProtectedPaths
            .Select(NormalizePath)
            .Where(path => path.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return snapshots
            .Where(snapshot => !SystemProcessWhitelist.Contains(snapshot.ProcessName))
            .Where(snapshot => !string.IsNullOrWhiteSpace(snapshot.ExecutablePath))
            .Select(snapshot => new
            {
                Snapshot = snapshot,
                NormalizedPath = NormalizePath(snapshot.ExecutablePath!)
            })
            .Where(item => item.NormalizedPath.Length > 0)
            .Where(item => !existing.Contains(item.NormalizedPath))
            .GroupBy(item => item.NormalizedPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.Snapshot.IsForeground)
                .ThenByDescending(item => item.Snapshot.WorkingSetBytes)
                .First()
                .Snapshot)
            .OrderByDescending(snapshot => snapshot.IsForeground)
            .ThenBy(snapshot => snapshot.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(snapshot => snapshot.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .Select(snapshot => new ProtectedAppCandidate(
                snapshot.ProcessName,
                snapshot.ExecutablePath!,
                snapshot.WorkingSetBytes,
                snapshot.IsForeground))
            .ToArray();
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.Trim().Replace('/', '\\').ToLowerInvariant();
    }
}
