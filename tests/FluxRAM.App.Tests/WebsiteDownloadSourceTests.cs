using Xunit;

namespace FluxRAM.App.Tests;

public sealed class WebsiteDownloadSourceTests
{
    private const string GitCodeReleaseBase =
        "https://gitcode.com/Midas927/FluxRAM/releases/download/v0.4/";
    private const string GitHubReleaseBase =
        "https://github.com/Midas927/FluxRAM/releases/latest/download/";

    [Fact]
    public void Website_UsesGitCodeForPrimaryDownloadsAndKeepsGitHubFallbacks()
    {
        var html = File.ReadAllText(FindWebsiteIndex());

        Assert.Contains(GitCodeReleaseBase + "FluxRAM-Portable-Windows-x64.zip", html);
        Assert.Contains(GitCodeReleaseBase + "FluxRAM-Lite-Windows-x64.zip", html);
        Assert.Contains(GitCodeReleaseBase + "FluxRAM-Portable-Windows-x64.zip.sha256", html);
        Assert.Contains(GitCodeReleaseBase + "FluxRAM-Lite-Windows-x64.zip.sha256", html);
        Assert.Contains(GitHubReleaseBase + "FluxRAM-Portable-Windows-x64.zip", html);
        Assert.Contains(GitHubReleaseBase + "FluxRAM-Lite-Windows-x64.zip", html);
        Assert.Contains("国内下载", html);
        Assert.Contains("GitHub 备用", html);
    }

    [Fact]
    public void Website_UsesThePerformanceManualHeroAndKeepsItsCorePromise()
    {
        var html = File.ReadAllText(FindWebsiteIndex());

        Assert.Contains("assets/memory-field-manual-v2.png", html);
        Assert.Contains("FluxRAM 0.4", html);
        Assert.Contains("先判断，再释放。", html);
        Assert.Contains("普通 Boost 不替你关闭应用。", html);
        Assert.Contains("深度释放", html);
    }

    private static string FindWebsiteIndex()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "site", "index.html");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate site/index.html from the test output directory.");
    }
}
