using System.Diagnostics;

namespace FluxRAM.App.Configuration;

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

        if (process.ExitCode != 0 && throwOnFailure)
        {
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"{output} {error}".Trim());
        }

        return process.ExitCode;
    }
}
