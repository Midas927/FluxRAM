using System.Reflection;

namespace FluxRAM.App.Configuration;

public static class AppVersionInfo
{
    public static string CurrentVersion
    {
        get
        {
            var assembly = typeof(AppVersionInfo).Assembly;
            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                return informationalVersion;
            }

            return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }
    }

    public static string CurrentDisplayVersion => "v" + CurrentVersion.Split('+')[0];
}
