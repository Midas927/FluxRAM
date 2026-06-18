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
    string? ErrorMessage);

public sealed class AppUpdateChecker : IDisposable
{
    private static readonly Uri LatestReleaseUri = new("https://api.github.com/repos/Midas927/FluxRAM/releases/latest");
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private readonly HttpClient _httpClient;
    private readonly bool _disposeClient;

    public AppUpdateChecker()
        : this(new HttpClient { Timeout = DefaultTimeout }, disposeClient: true)
    {
    }

    public AppUpdateChecker(HttpClient httpClient, bool disposeClient = false)
    {
        _httpClient = httpClient;
        _disposeClient = disposeClient;
    }

    public async Task<UpdateCheckResult> CheckLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = AppVersionInfo.CurrentDisplayVersion;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUri);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("FluxRAM", AppVersionInfo.CurrentVersion));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(
                    UpdateCheckState.Failed,
                    currentVersion,
                    null,
                    null,
                    $"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            var latestVersion = TryGetString(root, "tag_name");
            var releaseUrl = TryGetString(root, "html_url");

            if (string.IsNullOrWhiteSpace(latestVersion))
            {
                return new UpdateCheckResult(
                    UpdateCheckState.ReleaseVersionUnavailable,
                    currentVersion,
                    null,
                    releaseUrl,
                    null);
            }

            var comparison = CompareReleaseVersions(AppVersionInfo.CurrentVersion, latestVersion);
            var state = comparison switch
            {
                UpdateVersionComparison.LatestIsNewer => UpdateCheckState.UpdateAvailable,
                UpdateVersionComparison.Same => UpdateCheckState.UpToDate,
                UpdateVersionComparison.CurrentIsNewer => UpdateCheckState.CurrentBuildIsNewer,
                _ => UpdateCheckState.ReleaseVersionUnavailable
            };

            return new UpdateCheckResult(state, currentVersion, latestVersion, releaseUrl, null);
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(
                UpdateCheckState.Failed,
                currentVersion,
                null,
                null,
                ex.Message);
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

        var comparison = latest.CompareTo(current);
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

    private static Version? TryParseVersion(string? value)
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

        var metadataIndex = normalized.IndexOfAny(['-', '+']);
        if (metadataIndex >= 0)
        {
            normalized = normalized[..metadataIndex];
        }

        if (!Version.TryParse(normalized, out var version))
        {
            return null;
        }

        return new Version(
            version.Major,
            version.Minor,
            Math.Max(version.Build, 0),
            Math.Max(version.Revision, 0));
    }
}
