using System.IO;
using System.Text;
using FluxRAM.App.Licensing;

namespace FluxRAM.App.Configuration;

public sealed record AppUpdateCompletionRequest(string BackupExecutablePath, string CacheDirectory);

public static class AppUpdateCompletionService
{
    private const string CompleteUpdateArgument = "--complete-update";

    public static bool TryParseArguments(
        IReadOnlyList<string> args,
        out AppUpdateCompletionRequest? request)
    {
        request = null;
        var argumentIndex = -1;
        for (var index = 0; index < args.Count; index += 1)
        {
            if (string.Equals(args[index], CompleteUpdateArgument, StringComparison.OrdinalIgnoreCase))
            {
                argumentIndex = index;
                break;
            }
        }

        if (argumentIndex < 0 || argumentIndex + 2 >= args.Count)
        {
            return false;
        }

        try
        {
            var backupPath = DecodePath(args[argumentIndex + 1]);
            var cacheDirectory = DecodePath(args[argumentIndex + 2]);
            var currentExecutablePath = Path.GetFullPath(Environment.ProcessPath ?? string.Empty);
            var expectedBackupPath = Path.GetFullPath(currentExecutablePath + ".old");
            var updatesRoot = EnsureTrailingSeparator(Path.GetFullPath(AppDataPaths.GetUpdatesDirectory()));
            var normalizedCacheDirectory = Path.GetFullPath(cacheDirectory);
            if (!string.Equals(Path.GetFullPath(backupPath), expectedBackupPath, StringComparison.OrdinalIgnoreCase) ||
                !EnsureTrailingSeparator(normalizedCacheDirectory).StartsWith(updatesRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            request = new AppUpdateCompletionRequest(expectedBackupPath, normalizedCacheDirectory);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task CompleteAsync(
        AppUpdateCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        for (var attempt = 0; attempt < 5; attempt += 1)
        {
            try
            {
                if (File.Exists(request.BackupExecutablePath))
                {
                    File.Delete(request.BackupExecutablePath);
                }

                if (Directory.Exists(request.CacheDirectory))
                {
                    Directory.Delete(request.CacheDirectory, recursive: true);
                }

                return;
            }
            catch when (attempt < 4)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static string DecodePath(string value)
    {
        return Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
