using System.Management;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace FluxRAM.App.Licensing;

public interface IHardwareIdentifierProvider
{
    string GetCurrentMachineId();
    string GetLegacyMachineId();
}

public sealed record HardwareIdentifierFacts(
    string? SystemUuid,
    string? MachineGuid,
    IReadOnlyList<string> MacAddresses);

public sealed class HardwareIdentifierService : IHardwareIdentifierProvider
{
    private readonly Func<HardwareIdentifierFacts>? _factsProvider;

    public HardwareIdentifierService()
    {
    }

    public HardwareIdentifierService(Func<HardwareIdentifierFacts> factsProvider)
    {
        _factsProvider = factsProvider ?? throw new ArgumentNullException(nameof(factsProvider));
    }

    public string GetCurrentMachineId()
    {
        var facts = _factsProvider?.Invoke() ?? new HardwareIdentifierFacts(
            QueryWmiValue("Win32_ComputerSystemProduct", "UUID"),
            null,
            []);
        var systemUuid = NormalizeSystemUuid(facts.SystemUuid);
        if (systemUuid is not null)
        {
            return BuildMachineId([$"smbios:{systemUuid}"]);
        }

        throw new InvalidOperationException("A stable hardware identifier is unavailable.");
    }

    public string GetLegacyMachineId()
    {
        var facts = _factsProvider?.Invoke() ?? new HardwareIdentifierFacts(
            null,
            ReadMachineGuid(),
            GetActiveMacAddresses());
        var macAddresses = facts.MacAddresses
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(address => address, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (string.IsNullOrWhiteSpace(facts.MachineGuid) || macAddresses.Length == 0)
        {
            throw new InvalidOperationException("The previous machine identifier cannot be proven.");
        }

        return BuildMachineId(
        [
            $"machine:{facts.MachineGuid}",
            .. macAddresses.Select(address => $"mac:{address}")
        ]);
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

    private static string? QueryWmiValue(string className, string propertyName)
    {
        using var searcher = new ManagementObjectSearcher($"SELECT {propertyName} FROM {className}");
        searcher.Options.Timeout = TimeSpan.FromSeconds(2);
        searcher.Options.ReturnImmediately = true;
        using var results = searcher.Get();
        foreach (ManagementBaseObject item in results)
        {
            using (item)
            {
                var value = item[propertyName]?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static string? ReadMachineGuid()
    {
        Exception? registry64Exception = null;
        try
        {
            var value = ReadMachineGuid(RegistryView.Registry64);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        catch (Exception ex)
        {
            registry64Exception = ex;
        }

        try
        {
            return ReadMachineGuid(RegistryView.Registry32);
        }
        catch (Exception ex) when (registry64Exception is not null)
        {
            throw new AggregateException(
                "MachineGuid could not be read from either registry view.",
                registry64Exception,
                ex);
        }
    }

    private static string? ReadMachineGuid(RegistryView registryView)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView);
        using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography", false);
        return key?.GetValue("MachineGuid") as string;
    }

    private static IReadOnlyList<string> GetActiveMacAddresses()
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

    private static string? NormalizeSystemUuid(string? value)
    {
        if (!Guid.TryParse(value?.Trim().Trim('\0'), out var uuid) ||
            uuid == Guid.Empty ||
            uuid == new Guid("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF"))
        {
            return null;
        }

        return uuid.ToString("D").ToUpperInvariant();
    }

}
