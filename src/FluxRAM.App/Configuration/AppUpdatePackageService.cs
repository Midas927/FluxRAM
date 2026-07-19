using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using FluxRAM.App.Licensing;

namespace FluxRAM.App.Configuration;

public sealed record StagedAppUpdate(
    string Version,
    string CurrentExecutablePath,
    string StagedExecutablePath,
    string BackupExecutablePath,
    string CacheDirectory,
    string ScriptPath);

public sealed class AppUpdatePackageService : IDisposable
{
    private const long MaximumPackageBytes = 256L * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly bool _disposeClient;
    private readonly string _updatesRootDirectory;
    private readonly string _currentExecutablePath;

    public AppUpdatePackageService()
        : this(
            new HttpClient { Timeout = TimeSpan.FromMinutes(5) },
            AppDataPaths.GetUpdatesDirectory(),
            Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty,
            disposeClient: true)
    {
    }

    public AppUpdatePackageService(
        HttpClient httpClient,
        string updatesRootDirectory,
        string currentExecutablePath,
        bool disposeClient = false)
    {
        _httpClient = httpClient;
        _updatesRootDirectory = Path.GetFullPath(updatesRootDirectory);
        _currentExecutablePath = Path.GetFullPath(currentExecutablePath);
        _disposeClient = disposeClient;
    }

    public async Task<StagedAppUpdate> DownloadAndStageAsync(
        UpdateCheckResult update,
        AppDistributionMode distributionMode,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (update.State != UpdateCheckState.UpdateAvailable || string.IsNullOrWhiteSpace(update.LatestVersion))
        {
            throw new InvalidOperationException("No newer release is ready to install.");
        }

        var asset = AppDistributionInfo.SelectAsset(update.Assets, distributionMode)
            ?? throw new InvalidOperationException("The matching FluxRAM package was not found in this release.");
        if (!IsTrustedReleaseAsset(asset.DownloadUri))
        {
            throw new InvalidOperationException("The update package source could not be verified.");
        }

        var versionDirectoryName = SanitizeVersion(update.LatestVersion);
        CleanStaleUpdateDirectories();
        var cacheDirectory = Path.Combine(_updatesRootDirectory, versionDirectoryName);
        if (Directory.Exists(cacheDirectory))
        {
            Directory.Delete(cacheDirectory, recursive: true);
        }

        Directory.CreateDirectory(cacheDirectory);
        progress?.Report(5);

        var packageBytes = await DownloadBytesAsync(asset.DownloadUri, progress, cancellationToken)
            .ConfigureAwait(false);
        var expectedSha256 = asset.Sha256;
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            var checksumUri = new Uri(asset.DownloadUri.AbsoluteUri + ".sha256");
            var checksumText = await _httpClient.GetStringAsync(checksumUri, cancellationToken).ConfigureAwait(false);
            expectedSha256 = NormalizeSha256(checksumText);
        }

        if (string.IsNullOrWhiteSpace(expectedSha256) || !VerifySha256(packageBytes, expectedSha256))
        {
            throw new InvalidDataException("The downloaded update did not pass SHA256 verification.");
        }

        progress?.Report(85);
        var packagePath = Path.Combine(cacheDirectory, asset.Name);
        await File.WriteAllBytesAsync(packagePath, packageBytes, cancellationToken).ConfigureAwait(false);
        var stageDirectory = Path.Combine(cacheDirectory, "stage");
        var stagedExecutablePath = ExtractFluxRamExecutable(packagePath, stageDirectory);
        var scriptPath = Path.Combine(cacheDirectory, "replace-update.ps1");
        await File.WriteAllTextAsync(
            scriptPath,
            BuildReplacementScript(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken).ConfigureAwait(false);
        progress?.Report(100);

        return new StagedAppUpdate(
            update.LatestVersion,
            _currentExecutablePath,
            stagedExecutablePath,
            _currentExecutablePath + ".old",
            cacheDirectory,
            scriptPath);
    }

    public void LaunchReplacement(StagedAppUpdate update, int currentProcessId)
    {
        var powershellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(powershellPath))
        {
            powershellPath = "powershell.exe";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = powershellPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(update.ScriptPath);
        startInfo.ArgumentList.Add(currentProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(update.CurrentExecutablePath);
        startInfo.ArgumentList.Add(update.StagedExecutablePath);
        startInfo.ArgumentList.Add(update.BackupExecutablePath);
        startInfo.ArgumentList.Add(update.CacheDirectory);

        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The FluxRAM update helper could not be started.");
    }

    public static string? NormalizeSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[7..];
        }

        normalized = normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        return normalized.Length == 64 && normalized.All(Uri.IsHexDigit)
            ? normalized.ToLowerInvariant()
            : null;
    }

    public static bool VerifySha256(ReadOnlySpan<byte> content, string expectedSha256)
    {
        var normalizedExpected = NormalizeSha256(expectedSha256);
        if (normalizedExpected is null)
        {
            return false;
        }

        var actual = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(actual),
            Encoding.ASCII.GetBytes(normalizedExpected));
    }

    public static string ExtractFluxRamExecutable(string packagePath, string stageDirectory)
    {
        Directory.CreateDirectory(stageDirectory);
        using var archive = ZipFile.OpenRead(packagePath);
        var executableEntry = archive.Entries.SingleOrDefault(entry =>
            string.Equals(entry.FullName.Replace('\\', '/'), "FluxRAM.exe", StringComparison.OrdinalIgnoreCase));
        if (executableEntry is null || executableEntry.Length <= 0 || executableEntry.Length > MaximumPackageBytes)
        {
            throw new InvalidDataException("The update package does not contain a valid FluxRAM.exe.");
        }

        var stagedExecutablePath = Path.Combine(stageDirectory, "FluxRAM.exe");
        executableEntry.ExtractToFile(stagedExecutablePath, overwrite: true);
        return stagedExecutablePath;
    }

    public static string BuildReplacementScript()
    {
        return """
            param(
                [int]$OldPid,
                [string]$CurrentExe,
                [string]$StagedExe,
                [string]$BackupExe,
                [string]$CacheDirectory
            )

            $ErrorActionPreference = 'Stop'
            Wait-Process -Id $OldPid -ErrorAction SilentlyContinue

            if (Test-Path -LiteralPath $BackupExe) {
                Remove-Item -LiteralPath $BackupExe -Force
            }

            Move-Item -LiteralPath $CurrentExe -Destination $BackupExe -Force
            try {
                Copy-Item -LiteralPath $StagedExe -Destination $CurrentExe -Force
                $backupEncoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($BackupExe))
                $cacheEncoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($CacheDirectory))
                Start-Process -FilePath $CurrentExe -ArgumentList @('--complete-update', $backupEncoded, $cacheEncoded)
            }
            catch {
                if (Test-Path -LiteralPath $CurrentExe) {
                    Remove-Item -LiteralPath $CurrentExe -Force
                }
                if (Test-Path -LiteralPath $BackupExe) {
                    Move-Item -LiteralPath $BackupExe -Destination $CurrentExe -Force
                }
                throw
            }
            """;
    }

    public void Dispose()
    {
        if (_disposeClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<byte[]> DownloadBytesAsync(
        Uri uri,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumPackageBytes)
        {
            throw new InvalidDataException("The update package is larger than expected.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[128 * 1024];
        long totalRead = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
            if (totalRead > MaximumPackageBytes)
            {
                throw new InvalidDataException("The update package is larger than expected.");
            }

            output.Write(buffer, 0, read);
            if (response.Content.Headers.ContentLength is > 0)
            {
                progress?.Report(5 + (int)Math.Min(75, totalRead * 75 / response.Content.Headers.ContentLength.Value));
            }
        }

        return output.ToArray();
    }

    private static bool IsTrustedReleaseAsset(Uri uri)
    {
        return uri.Scheme == Uri.UriSchemeHttps &&
            string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath.StartsWith(
                "/Midas927/FluxRAM/releases/download/",
                StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeVersion(string version)
    {
        var value = version.Trim().TrimStart('v', 'V');
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalidCharacter, '-');
        }

        return string.IsNullOrWhiteSpace(value) ? "update" : value;
    }

    private void CleanStaleUpdateDirectories()
    {
        Directory.CreateDirectory(_updatesRootDirectory);
        foreach (var directory in Directory.EnumerateDirectories(_updatesRootDirectory))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A previous helper may still be finishing; the next update retries cleanup.
            }
        }
    }
}
