using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FluxRAM.App.Configuration;

public enum UpdateCheckState
{
    UpdateAvailable,
    UpToDate,
    CurrentBuildIsNewer,
    ReleaseVersionUnavailable,
    Failed
}

public enum UpdateVersionComparison
{
    Unknown,
    LatestIsNewer,
    Same,
    CurrentIsNewer
}

public sealed record UpdateCheckResult(
    UpdateCheckState State,
    string CurrentVersion,
    string? LatestVersion,
    string? ReleaseUrl,
    string? ErrorMessage,
    IReadOnlyList<AppUpdateAsset> Assets);

public sealed record AppUpdateAsset(
    string Name,
    Uri DownloadUri,
    string? Sha256);

public sealed class AppUpdateChecker : IDisposable
{
    private static readonly Uri LatestStableReleaseUri = new("https://api.github.com/repos/Midas927/FluxRAM/releases/latest");
    private static readonly Uri BetaReleaseChannelUri = new("https://api.github.com/repos/Midas927/FluxRAM/releases?per_page=20");
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private readonly HttpClient _httpClient;
    private readonly bool _disposeClient;
    private readonly string _currentVersion;

    public AppUpdateChecker()
        : this(
            new HttpClient { Timeout = DefaultTimeout },
            disposeClient: true,
            currentVersion: AppVersionInfo.CurrentVersion)
    {
    }

    public AppUpdateChecker(
        HttpClient httpClient,
        bool disposeClient = false,
        string? currentVersion = null)
    {
        _httpClient = httpClient;
        _disposeClient = disposeClient;
        _currentVersion = string.IsNullOrWhiteSpace(currentVersion)
            ? AppVersionInfo.CurrentVersion
            : currentVersion;
    }

    public async Task<UpdateCheckResult> CheckLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = FormatDisplayVersion(_currentVersion);
        try
        {
            var releaseUri = IsPrereleaseVersion(_currentVersion)
                ? BetaReleaseChannelUri
                : LatestStableReleaseUri;
            using var request = new HttpRequestMessage(HttpMethod.Get, releaseUri);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("FluxRAM", _currentVersion.Split('+')[0]));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(
                    UpdateCheckState.Failed,
                    currentVersion,
                    null,
                    null,
                    $"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase}.",
                    Array.Empty<AppUpdateAsset>());
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var release = SelectRelease(document.RootElement);
            if (!release.HasValue)
            {
                return new UpdateCheckResult(
                    UpdateCheckState.ReleaseVersionUnavailable,
                    currentVersion,
                    null,
                    null,
                    null,
                    Array.Empty<AppUpdateAsset>());
            }

            var root = release.Value;
            var latestVersion = TryGetString(root, "tag_name");
            var releaseUrl = TryGetString(root, "html_url");
            var assets = ReadAssets(root);

            if (string.IsNullOrWhiteSpace(latestVersion))
            {
                return new UpdateCheckResult(
                    UpdateCheckState.ReleaseVersionUnavailable,
                    currentVersion,
                    null,
                    releaseUrl,
                    null,
                    assets);
            }

            var comparison = CompareReleaseVersions(_currentVersion, latestVersion);
            var state = comparison switch
            {
                UpdateVersionComparison.LatestIsNewer => UpdateCheckState.UpdateAvailable,
                UpdateVersionComparison.Same => UpdateCheckState.UpToDate,
                UpdateVersionComparison.CurrentIsNewer => UpdateCheckState.CurrentBuildIsNewer,
                _ => UpdateCheckState.ReleaseVersionUnavailable
            };

            return new UpdateCheckResult(state, currentVersion, latestVersion, releaseUrl, null, assets);
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(
                UpdateCheckState.Failed,
                currentVersion,
                null,
                null,
                ex.Message,
                Array.Empty<AppUpdateAsset>());
        }
    }

    public static UpdateVersionComparison CompareReleaseVersions(string currentVersion, string latestVersion)
    {
        var current = TryParseVersion(currentVersion);
        var latest = TryParseVersion(latestVersion);
        if (current is null || latest is null)
        {
            return UpdateVersionComparison.Unknown;
        }

        var comparison = latest.Number.CompareTo(current.Number);
        if (comparison == 0)
        {
            comparison = ComparePrerelease(latest.Prerelease, current.Prerelease);
        }
        if (comparison > 0)
        {
            return UpdateVersionComparison.LatestIsNewer;
        }

        if (comparison < 0)
        {
            return UpdateVersionComparison.CurrentIsNewer;
        }

        return UpdateVersionComparison.Same;
    }

    public void Dispose()
    {
        if (_disposeClient)
        {
            _httpClient.Dispose();
        }
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static IReadOnlyList<AppUpdateAsset> ReadAssets(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AppUpdateAsset>();
        }

        var assets = new List<AppUpdateAsset>();
        foreach (var assetElement in assetsElement.EnumerateArray())
        {
            var name = TryGetString(assetElement, "name");
            var downloadUrl = TryGetString(assetElement, "browser_download_url");
            if (string.IsNullOrWhiteSpace(name) ||
                !Uri.TryCreate(downloadUrl, UriKind.Absolute, out var downloadUri))
            {
                continue;
            }

            var digest = TryGetString(assetElement, "digest");
            assets.Add(new AppUpdateAsset(
                name,
                downloadUri,
                AppUpdatePackageService.NormalizeSha256(digest)));
        }

        return assets;
    }

    private static JsonElement? SelectRelease(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            return root;
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        JsonElement? bestRelease = null;
        string? bestVersion = null;
        foreach (var release in root.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object ||
                (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True))
            {
                continue;
            }

            var version = TryGetString(release, "tag_name");
            if (TryParseVersion(version) is null)
            {
                continue;
            }

            if (bestVersion is null || CompareReleaseVersions(bestVersion, version!) == UpdateVersionComparison.LatestIsNewer)
            {
                bestRelease = release;
                bestVersion = version;
            }
        }

        return bestRelease;
    }

    private static bool IsPrereleaseVersion(string version)
    {
        return version.Split('+')[0].Contains('-', StringComparison.Ordinal);
    }

    private static string FormatDisplayVersion(string version)
    {
        var value = version.Split('+')[0];
        return value.StartsWith('v') || value.StartsWith('V') ? value : "v" + value;
    }

    private static ParsedReleaseVersion? TryParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        var buildMetadataIndex = normalized.IndexOf('+');
        if (buildMetadataIndex >= 0)
        {
            normalized = normalized[..buildMetadataIndex];
        }

        string? prerelease = null;
        var prereleaseIndex = normalized.IndexOf('-');
        if (prereleaseIndex >= 0)
        {
            prerelease = normalized[(prereleaseIndex + 1)..];
            normalized = normalized[..prereleaseIndex];
        }

        if (!Version.TryParse(normalized, out var version) || string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return new ParsedReleaseVersion(
            new Version(
                version.Major,
                version.Minor,
                Math.Max(version.Build, 0),
                Math.Max(version.Revision, 0)),
            string.IsNullOrWhiteSpace(prerelease) ? null : prerelease);
    }

    private static int ComparePrerelease(string? left, string? right)
    {
        if (left is null)
        {
            return right is null ? 0 : 1;
        }

        if (right is null)
        {
            return -1;
        }

        var leftParts = left.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var rightParts = right.Split('.', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < Math.Min(leftParts.Length, rightParts.Length); index += 1)
        {
            var leftIsNumber = int.TryParse(leftParts[index], out var leftNumber);
            var rightIsNumber = int.TryParse(rightParts[index], out var rightNumber);
            var comparison = leftIsNumber && rightIsNumber
                ? leftNumber.CompareTo(rightNumber)
                : leftIsNumber
                    ? -1
                    : rightIsNumber
                        ? 1
                        : string.Compare(leftParts[index], rightParts[index], StringComparison.OrdinalIgnoreCase);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return leftParts.Length.CompareTo(rightParts.Length);
    }

    private sealed record ParsedReleaseVersion(Version Number, string? Prerelease);
}
