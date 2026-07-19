using System.IO;

namespace FluxRAM.App.Licensing;

public static class AppDataPaths
{
    public static string GetLicenseKeyPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "FluxRAM",
            "license.key");
    }

    public static string GetProtectedAppsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "FluxRAM",
            "protected-apps.txt");
    }

    public static string GetUserSettingsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FluxRAM",
            "settings.json");
    }

    public static string GetDiagnosticLogPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FluxRAM",
            "fluxram.log");
    }

    public static string GetUpdatesDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FluxRAM",
            "Updates");
    }
}
