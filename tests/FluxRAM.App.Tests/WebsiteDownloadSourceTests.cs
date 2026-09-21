using Xunit;

namespace FluxRAM.App.Tests;

public sealed class WebsiteDownloadSourceTests
{
    private const string GitCodeReleaseBase =
        "https://gitcode.com/Midas927/FluxRAM/releases/download/v0.4.3/";
    private const string GitHubReleaseBase =
        "https://github.com/Midas927/FluxRAM/releases/download/v0.4.3/";

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
        Assert.Contains("https://gitcode.com/Midas927/FluxRAM/releases/v0.4.3", html);
        Assert.DoesNotContain("https://gitcode.com/Midas927/FluxRAM/releases/tag/", html);
    }

    [Fact]
    public void Website_UsesThePerformanceManualWithTheMemoryGridBackground()
    {
        var html = File.ReadAllText(FindWebsiteIndex());

        Assert.Contains("id=\"hero-memory-canvas\"", html);
        Assert.Contains("data-visual=\"memory-grid\"", html);
        Assert.Contains("后台状态", html);
        Assert.DoesNotContain("BACKGROUND FRAGMENTS", html);
        Assert.DoesNotContain("memory-field-manual", html);
        Assert.Contains("FluxRAM 0.4.3", html);
        Assert.Contains("先判断，再释放。", html);
        Assert.Contains("普通 Boost 不替你关闭应用。", html);
        Assert.Contains("深度释放", html);
        Assert.DoesNotContain("未知发布者", html);
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

    [Fact]
    public void PublicReadmesAndReleaseNotesUseGitCodeRoutes()
    {
        var root = Directory.GetParent(Path.GetDirectoryName(FindWebsiteIndex())!)!.FullName;
        foreach (var relative in new[] { "README.md", "README.en.md", "docs/releases/v0.4.3.md" })
        {
            var text = File.ReadAllText(Path.Combine(root, relative));
            Assert.Contains("https://gitcode.com/Midas927/FluxRAM/releases/v0.4.3", text);
            Assert.DoesNotContain("https://gitcode.com/Midas927/FluxRAM/releases/tag/", text);
            if (relative.StartsWith("README", StringComparison.Ordinal))
                Assert.Contains("[" + (relative == "README.md" ? "国内下载（GitCode）" : "China mirror (GitCode)") +
                    "](https://gitcode.com/Midas927/FluxRAM/releases/download/v0.4.3/FluxRAM-Portable-Windows-x64.zip)", text);
        }
    }
}
