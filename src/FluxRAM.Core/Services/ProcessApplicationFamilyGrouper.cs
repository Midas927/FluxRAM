using System.IO;
using FluxRAM.Core.Models;

namespace FluxRAM.Core.Services;

public static class ProcessApplicationFamilyGrouper
{
    private static readonly string[] GenericChildNames =
    {
        "agent",
        "cefsharp.browsersubprocess",
        "cefrendererprocess",
        "crashpad_handler",
        "helper",
        "host",
        "qtwebengineprocess",
        "renderer",
        "updater",
        "webviewhost",
        "worker"
    };

    public static IReadOnlyList<ProcessApplicationFamily> Group(IReadOnlyList<ProcessSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return Array.Empty<ProcessApplicationFamily>();
        }

        var snapshotsById = snapshots.ToDictionary(snapshot => snapshot.ProcessId);
        return snapshots
            .GroupBy(
                snapshot => ResolveFamilyIdentity(snapshot, snapshotsById),
                FamilyIdentityComparer.Instance)
            .Select(group => CreateFamily(group.Key, group.ToArray()))
            .ToArray();
    }

    private static ProcessApplicationFamily CreateFamily(
        FamilyIdentity identity,
        IReadOnlyList<ProcessSnapshot> snapshots)
    {
        var displayProcess = snapshots
            .OrderByDescending(snapshot => snapshot.IsForeground)
            .ThenByDescending(snapshot => snapshot.HasVisibleWindow)
            .ThenBy(snapshot => NormalizeProcessName(snapshot.ProcessName).Length)
            .ThenByDescending(snapshot => snapshot.WorkingSetBytes)
            .First();

        return new ProcessApplicationFamily(
            identity.Key,
            displayProcess.ProcessName,
            identity.ExecutableDirectory,
            snapshots);
    }

    private static FamilyIdentity ResolveFamilyIdentity(
        ProcessSnapshot snapshot,
        IReadOnlyDictionary<int, ProcessSnapshot> snapshotsById)
    {
        var visited = new HashSet<int>();
        var current = snapshot;

        while (visited.Add(current.ProcessId))
        {
            var pathIdentity = CreatePathIdentity(current.ExecutablePath);
            if (pathIdentity is not null)
            {
                return pathIdentity.Value;
            }

            if (!current.ParentProcessId.HasValue ||
                !snapshotsById.TryGetValue(current.ParentProcessId.Value, out var parent) ||
                !CanInheritParentIdentity(current.ProcessName, parent.ProcessName))
            {
                break;
            }

            current = parent;
        }

        var processName = NormalizeProcessName(current.ProcessName);
        if (IsGenericChildName(processName))
        {
            return new FamilyIdentity($"process:{current.ProcessId}", null);
        }

        return new FamilyIdentity($"name:{processName}", null);
    }

    private static bool CanInheritParentIdentity(string childProcessName, string parentProcessName)
    {
        var childName = NormalizeProcessName(childProcessName);
        var parentName = NormalizeProcessName(parentProcessName);
        if (childName.Length == 0 || parentName.Length == 0)
        {
            return false;
        }

        if (childName.Equals(parentName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsGenericChildName(childName))
        {
            return true;
        }

        var shorterLength = Math.Min(childName.Length, parentName.Length);
        return shorterLength >= 4 &&
            (childName.StartsWith(parentName, StringComparison.OrdinalIgnoreCase) ||
             parentName.StartsWith(childName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsGenericChildName(string processName)
    {
        return GenericChildNames.Any(name =>
            processName.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static FamilyIdentity? CreatePathIdentity(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(executablePath.Trim());
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory) || IsSharedSystemDirectory(directory))
            {
                return new FamilyIdentity($"path:{NormalizePath(fullPath)}", null);
            }

            return new FamilyIdentity($"directory:{NormalizePath(directory)}", directory);
        }
        catch
        {
            return new FamilyIdentity($"path:{NormalizePath(executablePath)}", null);
        }
    }

    private static bool IsSharedSystemDirectory(string directory)
    {
        var normalizedDirectory = NormalizePath(directory).TrimEnd('\\');
        var windowsDirectory = NormalizePath(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows)).TrimEnd('\\');

        if (windowsDirectory.Length > 0 &&
            (normalizedDirectory.Equals(windowsDirectory, StringComparison.OrdinalIgnoreCase) ||
             normalizedDirectory.StartsWith(windowsDirectory + "\\", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return StandardSharedRoots()
            .Any(root => normalizedDirectory.Equals(root, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> StandardSharedRoots()
    {
        yield return NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)).TrimEnd('\\');
        yield return NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)).TrimEnd('\\');
        yield return NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)).TrimEnd('\\');
        yield return NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)).TrimEnd('\\');
    }

    private static string NormalizePath(string path)
    {
        return path.Trim().Replace('/', '\\').ToLowerInvariant();
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

    private readonly record struct FamilyIdentity(string Key, string? ExecutableDirectory);

    private sealed class FamilyIdentityComparer : IEqualityComparer<FamilyIdentity>
    {
        public static FamilyIdentityComparer Instance { get; } = new();

        public bool Equals(FamilyIdentity x, FamilyIdentity y) =>
            string.Equals(x.Key, y.Key, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(FamilyIdentity obj) =>
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Key);
    }
}
