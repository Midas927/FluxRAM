using System.Diagnostics;
using System.IO;
using System.Xml.Linq;

namespace FluxRAM.App.Configuration;

public enum StartupAutoBoostRegistrationKind
{
    NotRegistered,
    Registered,
    PathMismatch,
    ArgumentMissing,
    Unknown
}

public sealed record StartupAutoBoostRegistrationStatus(
    StartupAutoBoostRegistrationKind Kind,
    string? RegisteredExecutablePath,
    string? ExpectedExecutablePath,
    string? Detail);

public sealed class StartupAutoBoostService
{
    private const string TaskName = "FluxRAM Auto Boost";
    private const string AutoBoostArgument = "--auto-boost";

    public void SetEnabled(bool isEnabled)
    {
        if (isEnabled)
        {
            Enable();
            return;
        }

        Disable();
    }

    public static bool WasLaunchedForAutoBoost(string[] args)
    {
        return args.Any(arg => string.Equals(arg, AutoBoostArgument, StringComparison.OrdinalIgnoreCase));
    }

    public StartupAutoBoostRegistrationStatus GetRegistrationStatus()
    {
        var expectedExecutablePath = ResolveExecutablePath();
        var result = RunSchtasksWithOutput([
            "/Query",
            "/TN",
            TaskName,
            "/XML"
        ], throwOnFailure: false);

        if (result.ExitCode != 0)
        {
            return new StartupAutoBoostRegistrationStatus(
                StartupAutoBoostRegistrationKind.NotRegistered,
                null,
                expectedExecutablePath,
                result.Error);
        }

        if (!TryReadTaskAction(result.Output, out var command, out var arguments))
        {
            return new StartupAutoBoostRegistrationStatus(
                StartupAutoBoostRegistrationKind.Unknown,
                null,
                expectedExecutablePath,
                "Task action could not be read.");
        }

        var registeredExecutablePath = ExtractExecutablePath(command);
        var hasAutoBoostArgument =
            command.Contains(AutoBoostArgument, StringComparison.OrdinalIgnoreCase) ||
            arguments.Contains(AutoBoostArgument, StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(registeredExecutablePath))
        {
            return new StartupAutoBoostRegistrationStatus(
                StartupAutoBoostRegistrationKind.Unknown,
                null,
                expectedExecutablePath,
                "Task executable path could not be read.");
        }

        if (!PathsEqual(registeredExecutablePath, expectedExecutablePath) || !File.Exists(registeredExecutablePath))
        {
            return new StartupAutoBoostRegistrationStatus(
                StartupAutoBoostRegistrationKind.PathMismatch,
                registeredExecutablePath,
                expectedExecutablePath,
                "Task executable path differs from the current app path.");
        }

        if (!hasAutoBoostArgument)
        {
            return new StartupAutoBoostRegistrationStatus(
                StartupAutoBoostRegistrationKind.ArgumentMissing,
                registeredExecutablePath,
                expectedExecutablePath,
                "Task is missing the auto-boost argument.");
        }

        return new StartupAutoBoostRegistrationStatus(
            StartupAutoBoostRegistrationKind.Registered,
            registeredExecutablePath,
            expectedExecutablePath,
            null);
    }

    private static void Enable()
    {
        RunSchtasks([
            "/Create",
            "/TN",
            TaskName,
            "/SC",
            "ONLOGON",
            "/RL",
            "HIGHEST",
            "/F",
            "/TR",
            CreateRunCommand(ResolveExecutablePath())
        ]);
    }

    private static void Disable()
    {
        if (!TaskExists())
        {
            return;
        }

        RunSchtasks([
            "/Delete",
            "/TN",
            TaskName,
            "/F"
        ]);
    }

    private static bool TaskExists()
    {
        return RunSchtasks([
            "/Query",
            "/TN",
            TaskName
        ], throwOnFailure: false) == 0;
    }

    private static string CreateRunCommand(string executablePath)
    {
        return $"\"{executablePath}\" {AutoBoostArgument}";
    }

    private static string ResolveExecutablePath()
    {
        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            return executablePath;
        }

        executablePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            return executablePath;
        }

        throw new InvalidOperationException("FluxRAM executable path could not be resolved.");
    }

    private static int RunSchtasks(IReadOnlyList<string> arguments, bool throwOnFailure = true)
    {
        return RunSchtasksWithOutput(arguments, throwOnFailure).ExitCode;
    }

    private static SchtasksResult RunSchtasksWithOutput(IReadOnlyList<string> arguments, bool throwOnFailure = true)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException("Windows Task Scheduler command could not be started.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(10000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new TimeoutException("Windows Task Scheduler command timed out.");
        }

        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();

        if (process.ExitCode != 0 && throwOnFailure)
        {
            throw new InvalidOperationException($"{output} {error}".Trim());
        }

        return new SchtasksResult(process.ExitCode, output, error);
    }

    private static bool TryReadTaskAction(string xml, out string command, out string arguments)
    {
        command = string.Empty;
        arguments = string.Empty;
        try
        {
            var document = XDocument.Parse(xml);
            var exec = document
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName.Equals("Exec", StringComparison.OrdinalIgnoreCase));
            if (exec is null)
            {
                return false;
            }

            command = exec
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName.Equals("Command", StringComparison.OrdinalIgnoreCase))
                ?.Value
                ?.Trim() ?? string.Empty;
            arguments = exec
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName.Equals("Arguments", StringComparison.OrdinalIgnoreCase))
                ?.Value
                ?.Trim() ?? string.Empty;

            return command.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string ExtractExecutablePath(string command)
    {
        var trimmed = command.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (trimmed.StartsWith('"'))
        {
            var endQuote = trimmed.IndexOf('"', 1);
            return endQuote > 1 ? trimmed[1..endQuote] : string.Empty;
        }

        var exeIndex = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex >= 0)
        {
            return trimmed[..(exeIndex + 4)].Trim('"');
        }

        return trimmed;
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            var normalizedLeft = Path.GetFullPath(left).TrimEnd('\\').Replace('/', '\\');
            var normalizedRight = Path.GetFullPath(right).TrimEnd('\\').Replace('/', '\\');
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private readonly record struct SchtasksResult(int ExitCode, string Output, string Error);
}
