using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace FluxRAM.App.Licensing;

public interface IHardwareIdentifierProvider
{
    string GetCurrentMachineId();
}

public sealed class HardwareIdentifierService : IHardwareIdentifierProvider
{
    public string GetCurrentMachineId()
    {
        var parts = new List<string>();
        var machineGuid = TryReadMachineGuid();
        if (!string.IsNullOrWhiteSpace(machineGuid))
        {
            parts.Add($"machine:{machineGuid}");
        }

        parts.AddRange(GetPhysicalMacAddresses().Select(address => $"mac:{address}"));

        if (parts.Count == 0)
        {
            parts.Add($"fallback:{Environment.MachineName}:{Environment.OSVersion.VersionString}");
        }

        return BuildMachineId(parts);
    }

    public static string BuildMachineId(IEnumerable<string> stableParts)
    {
        var normalized = string.Join(
            "|",
            stableParts
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part.Trim().ToUpperInvariant())
                .OrderBy(part => part, StringComparer.Ordinal));

        if (normalized.Length == 0)
        {
            normalized = "FLUXRAM-UNKNOWN-MACHINE";
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var compact = Convert.ToHexString(hash)[..32];
        return "FLX-" + string.Join("-", Enumerable.Range(0, 8).Select(index => compact.Substring(index * 4, 4)));
    }

    private static string? TryReadMachineGuid()
    {
        return TryReadMachineGuid(RegistryView.Registry64) ?? TryReadMachineGuid(RegistryView.Registry32);
    }

    private static string? TryReadMachineGuid(RegistryView registryView)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView);
            using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography", false);
            return key?.GetValue("MachineGuid") as string;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> GetPhysicalMacAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(networkInterface => networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Where(networkInterface => networkInterface.OperationalStatus == OperationalStatus.Up)
                .Select(networkInterface => networkInterface.GetPhysicalAddress().ToString())
                .Where(address => !string.IsNullOrWhiteSpace(address))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(address => address, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
