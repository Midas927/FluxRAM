using System.Collections.Frozen;

namespace FluxRAM.Core.Services;

public static class SystemProcessWhitelist
{
    private static readonly FrozenSet<string> Names = new[]
    {
        "Idle",
        "System",
        "Registry",
        "Memory Compression",
        "svchost",
        "ntoskrnl",
        "smss",
        "csrss",
        "wininit",
        "winlogon",
        "services",
        "lsass",
        "fontdrvhost",
        "dwm",
        "sihost",
        "spoolsv",
        "explorer",
        "ShellExperienceHost",
        "StartMenuExperienceHost",
        "RuntimeBroker",
        "SearchHost",
        "taskhostw",
        "ctfmon",
        "audiodg",
        "WmiPrvSE",
        "ApplicationFrameHost",
        "SystemSettings",
        "LockApp",
        "TextInputHost",
        "backgroundTaskHost",
        "UserOOBEBroker",
        "SecurityHealthService",
        "SecurityHealthSystray",
        "conhost",
        "dllhost",
        "WUDFHost",
        "dasHost",
        "unsecapp",
        "msedgewebview2",
        "HipsDaemon",
        "HipsTray",
        "MsMpEng",
        "NisSrv",
        "MpDefenderCoreService",
        "Sense",
        "avp",
        "AVGSvc",
        "AvastSvc",
        "360tray",
        "360safe",
        "ZhuDongFangYu",
        "QQPCRTP"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> ProcessNames => Names;

    public static bool Contains(string processName)
    {
        return Names.Contains(processName);
    }
}
