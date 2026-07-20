using FluxRAM.App.Configuration;
using System.Net;
using System.Text;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class AppUpdateCheckerTests
{
    [Theory]
    [InlineData("0.3.5", "v0.3.6", UpdateVersionComparison.LatestIsNewer)]
    [InlineData("0.3.6", "v0.3.7", UpdateVersionComparison.LatestIsNewer)]
    [InlineData("0.3.7", "v0.4", UpdateVersionComparison.LatestIsNewer)]
    [InlineData("0.4", "v0.4", UpdateVersionComparison.Same)]
    [InlineData("0.4", "v0.4.0", UpdateVersionComparison.Same)]
    [InlineData("0.3.8-beta.1", "v0.3.8", UpdateVersionComparison.LatestIsNewer)]
    [InlineData("0.3.8-beta.1", "v0.3.8-beta.2", UpdateVersionComparison.LatestIsNewer)]
    [InlineData("0.3.8", "v0.3.8-beta.2", UpdateVersionComparison.CurrentIsNewer)]
    [InlineData("0.3.5", "0.3.5", UpdateVersionComparison.Same)]
    [InlineData("0.3.5", "v0.3.4", UpdateVersionComparison.CurrentIsNewer)]
    [InlineData("0.3.5+local", "v0.3.5", UpdateVersionComparison.Same)]
    [InlineData("0.3.5", "not-a-version", UpdateVersionComparison.Unknown)]
    public void CompareReleaseVersions_HandlesReleaseTags(
        string currentVersion,
        string latestVersion,
        UpdateVersionComparison expected)
    {
        Assert.Equal(expected, AppUpdateChecker.CompareReleaseVersions(currentVersion, latestVersion));
    }

    [Fact]
    public async Task CheckLatestReleaseAsync_ReadsDownloadAssetsAndDigests()
    {
        const string json = """
            {
              "tag_name": "v9.9.9",
              "html_url": "https://github.com/Midas927/FluxRAM/releases/tag/v9.9.9",
              "assets": [
                {
                  "name": "FluxRAM-Lite-Windows-x64.zip",
                  "browser_download_url": "https://example.test/lite.zip",
                  "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                },
                {
                  "name": "FluxRAM-Portable-Windows-x64.zip",
                  "browser_download_url": "https://example.test/portable.zip",
                  "digest": "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
                }
              ]
            }
            """;
        var handler = new StaticResponseHandler(json);
        using var client = new HttpClient(handler);
        using var checker = new AppUpdateChecker(client, currentVersion: "0.3.7");

        var result = await checker.CheckLatestReleaseAsync();

        Assert.Equal(UpdateCheckState.UpdateAvailable, result.State);
        Assert.Equal(2, result.Assets.Count);
        var portable = Assert.Single(result.Assets, asset => asset.Name.Contains("Portable", StringComparison.Ordinal));
        Assert.Equal(new Uri("https://example.test/portable.zip"), portable.DownloadUri);
        Assert.Equal("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", portable.Sha256);
        Assert.EndsWith("/releases/latest", handler.LastRequestUri?.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckLatestReleaseAsync_BetaBuildUsesPrereleaseChannelAndSelectsNewestVersion()
    {
        const string json = """
            [
              {
                "tag_name": "v0.3.8-beta.2",
                "html_url": "https://github.com/Midas927/FluxRAM/releases/tag/v0.3.8-beta.2",
                "draft": false,
                "prerelease": true,
                "assets": []
              },
              {
                "tag_name": "v0.3.7",
                "html_url": "https://github.com/Midas927/FluxRAM/releases/tag/v0.3.7",
                "draft": false,
                "prerelease": false,
                "assets": []
              }
            ]
            """;
        var handler = new StaticResponseHandler(json);
        using var client = new HttpClient(handler);
        using var checker = new AppUpdateChecker(client, currentVersion: "0.3.8-beta.1");

        var result = await checker.CheckLatestReleaseAsync();

        Assert.Equal(UpdateCheckState.UpdateAvailable, result.State);
        Assert.Equal("v0.3.8-beta.2", result.LatestVersion);
        Assert.EndsWith("/releases?per_page=20", handler.LastRequestUri?.PathAndQuery, StringComparison.Ordinal);
    }

    private sealed class StaticResponseHandler(string content) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }
}
