using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using FluxRAM.App.Configuration;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class AppUpdatePackageServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "FluxRAM.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void NormalizeSha256_AcceptsApiDigestAndChecksumFileFormats()
    {
        var hash = new string('a', 64);

        Assert.Equal(hash, AppUpdatePackageService.NormalizeSha256($"sha256:{hash}"));
        Assert.Equal(hash, AppUpdatePackageService.NormalizeSha256($"{hash}  FluxRAM-Lite-Windows-x64.zip"));
        Assert.Null(AppUpdatePackageService.NormalizeSha256("not-a-hash"));
    }

    [Fact]
    public void VerifySha256_RejectsChangedContent()
    {
        var bytes = Encoding.UTF8.GetBytes("verified update");
        var expected = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        Assert.True(AppUpdatePackageService.VerifySha256(bytes, expected));
        Assert.False(AppUpdatePackageService.VerifySha256(Encoding.UTF8.GetBytes("changed"), expected));
    }

    [Fact]
    public void ExtractFluxRamExecutable_ExtractsOnlyTheExpectedEntry()
    {
        Directory.CreateDirectory(_tempDirectory);
        var archivePath = Path.Combine(_tempDirectory, "update.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var executable = archive.CreateEntry("FluxRAM.exe");
            using (var writer = new StreamWriter(executable.Open()))
            {
                writer.Write("beta executable");
            }

            archive.CreateEntry("../outside.txt");
        }

        var stagedPath = AppUpdatePackageService.ExtractFluxRamExecutable(
            archivePath,
            Path.Combine(_tempDirectory, "stage"));

        Assert.Equal("beta executable", File.ReadAllText(stagedPath));
        Assert.False(File.Exists(Path.Combine(_tempDirectory, "outside.txt")));
    }

    [Fact]
    public void BuildReplacementScript_WaitsBacksUpRestartsAndCanRestore()
    {
        var script = AppUpdatePackageService.BuildReplacementScript();

        Assert.Contains("Wait-Process", script, StringComparison.Ordinal);
        Assert.Contains("Move-Item", script, StringComparison.Ordinal);
        Assert.Contains("Copy-Item", script, StringComparison.Ordinal);
        Assert.Contains("Start-Process", script, StringComparison.Ordinal);
        Assert.Contains("$BackupExe", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadAndStageAsync_DownloadsVerifiesAndStagesMatchingPackage()
    {
        Directory.CreateDirectory(_tempDirectory);
        var packageBytes = CreatePackageBytes("staged beta");
        var hash = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
        var asset = new AppUpdateAsset(
            "FluxRAM-Lite-Windows-x64.zip",
            new Uri("https://github.com/Midas927/FluxRAM/releases/download/v0.3.8/FluxRAM-Lite-Windows-x64.zip"),
            hash);
        var update = new UpdateCheckResult(
            UpdateCheckState.UpdateAvailable,
            "v0.3.7",
            "v0.3.8",
            "https://github.com/Midas927/FluxRAM/releases/tag/v0.3.8",
            null,
            new[] { asset });
        var currentExecutablePath = Path.Combine(_tempDirectory, "FluxRAM.exe");
        File.WriteAllText(currentExecutablePath, "current");
        var staleUpdateDirectory = Path.Combine(_tempDirectory, "updates", "0.3.7");
        Directory.CreateDirectory(staleUpdateDirectory);
        File.WriteAllText(Path.Combine(staleUpdateDirectory, "old.zip"), "old update");
        using var client = new HttpClient(new StaticBytesHandler(packageBytes));
        using var service = new AppUpdatePackageService(
            client,
            Path.Combine(_tempDirectory, "updates"),
            currentExecutablePath);

        var staged = await service.DownloadAndStageAsync(update, AppDistributionMode.Lite);

        Assert.Equal("staged beta", File.ReadAllText(staged.StagedExecutablePath));
        Assert.True(File.Exists(staged.ScriptPath));
        Assert.Equal(currentExecutablePath + ".old", staged.BackupExecutablePath);
        Assert.False(Directory.Exists(staleUpdateDirectory));
    }

    [Fact]
    public void UpdateCompletionArguments_RejectMalformedOrUntrustedCleanupPaths()
    {
        var arbitraryPath = Convert.ToBase64String(Encoding.UTF8.GetBytes(@"C:\Windows\notepad.exe"));

        Assert.False(AppUpdateCompletionService.TryParseArguments(
            new[] { "--complete-update", arbitraryPath, arbitraryPath },
            out _));
    }

    [Fact]
    public void ReplacementScript_BacksUpAndReplacesExecutable()
    {
        Directory.CreateDirectory(_tempDirectory);
        var currentExecutablePath = Path.Combine(_tempDirectory, "FluxRAM.exe");
        var stagedExecutablePath = Path.Combine(_tempDirectory, "FluxRAM.staged.exe");
        var backupExecutablePath = currentExecutablePath + ".old";
        File.Copy(Path.Combine(Environment.SystemDirectory, "where.exe"), currentExecutablePath);
        File.Copy(Path.Combine(Environment.SystemDirectory, "whoami.exe"), stagedExecutablePath);
        var oldHash = GetFileSha256(currentExecutablePath);
        var stagedHash = GetFileSha256(stagedExecutablePath);
        var scriptPath = Path.Combine(_tempDirectory, "replace-update.ps1");
        File.WriteAllText(scriptPath, AppUpdatePackageService.BuildReplacementScript());
        var powershellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = powershellPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
        {
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            scriptPath,
            "999999",
            currentExecutablePath,
            stagedExecutablePath,
            backupExecutablePath,
            _tempDirectory
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        Assert.True(process.WaitForExit(15_000));
        var error = process.StandardError.ReadToEnd();

        Assert.True(process.ExitCode == 0, error);
        Assert.Equal(stagedHash, GetFileSha256(currentExecutablePath));
        Assert.Equal(oldHash, GetFileSha256(backupExecutablePath));
    }

    public void Dispose()
    {
        for (var attempt = 0; attempt < 10 && Directory.Exists(_tempDirectory); attempt += 1)
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 9)
            {
                Thread.Sleep(200);
            }
        }
    }

    private static byte[] CreatePackageBytes(string executableContent)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var executable = archive.CreateEntry("FluxRAM.exe");
            using var writer = new StreamWriter(executable.Open());
            writer.Write(executableContent);
        }

        return stream.ToArray();
    }

    private static string GetFileSha256(string path)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    }

    private sealed class StaticBytesHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }
}
