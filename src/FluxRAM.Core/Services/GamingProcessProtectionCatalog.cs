using System.Collections.Frozen;

namespace FluxRAM.Core.Services;

public static class GamingProcessProtectionCatalog
{
    private static readonly FrozenSet<string> Names = new[]
    {
        "steam",
        "steamwebhelper",
        "epicgameslauncher",
        "eadesktop",
        "battle.net",
        "riotclientservices",
        "xboxapp",
        "gamingservices",
        "gamingservicesnet",
        "armourycrate",
        "armourycrateservice",
        "legionspace",
        "ayaspace",
        "gpdassistant",
        "onexconsole",
        "amdsoftware",
        "radeonsoftware",
        "nvidiaapp",
        "nvidia container",
        "nvcontainer",
        "intelgraphicscommandcenter"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> ProcessNames => Names;

    public static bool Contains(string processName)
    {
        return Names.Contains(NormalizeProcessName(processName));
    }

    private static string NormalizeProcessName(string processName)
    {
        var normalized = processName.Trim();
        return normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^4]
            : normalized;
    }
}
