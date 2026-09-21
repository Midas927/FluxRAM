using System.IO;
using System.Threading;

namespace FluxRAM.App.Licensing;

public static class AppDataPaths
{
    private static readonly AsyncLocal<DataRoots?> ScopedRoots = new();

    public static string GetLicenseKeyPath()
    {
        return Path.Combine(GetCommonDataRoot(), "FluxRAM", "license.key");
    }

    public static string GetMachineIdentityPath()
    {
        return Path.Combine(GetCommonDataRoot(), "FluxRAM", "machine.id");
    }

    public static string GetProtectedAppsPath()
    {
        return Path.Combine(GetCommonDataRoot(), "FluxRAM", "protected-apps.txt");
    }

    public static string GetUserSettingsPath()
    {
        return Path.Combine(GetLocalDataRoot(), "FluxRAM", "settings.json");
    }

    public static string GetDiagnosticLogPath()
    {
        return Path.Combine(GetLocalDataRoot(), "FluxRAM", "fluxram.log");
    }

    public static string GetUpdatesDirectory()
    {
        return Path.Combine(GetLocalDataRoot(), "FluxRAM", "Updates");
    }

    internal static IDisposable UseTestRoots(string commonDataRoot, string localDataRoot)
    {
        var previous = ScopedRoots.Value;
        ScopedRoots.Value = new DataRoots(commonDataRoot, localDataRoot);
        return new Scope(() => ScopedRoots.Value = previous);
    }

    private static string GetCommonDataRoot()
    {
        return ScopedRoots.Value?.CommonDataRoot ??
               Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    }

    private static string GetLocalDataRoot()
    {
        return ScopedRoots.Value?.LocalDataRoot ??
               Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    private sealed record DataRoots(string CommonDataRoot, string LocalDataRoot);

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose()
        {
            Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }
}
