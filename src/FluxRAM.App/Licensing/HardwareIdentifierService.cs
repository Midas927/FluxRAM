using System.Net.NetworkInformation;
using System.IO;
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
    private static readonly byte[] MachineIdentityEntropy =
        Encoding.UTF8.GetBytes("FluxRAM.MachineIdentity.v1");
    private readonly string _machineIdentityPath;

    public HardwareIdentifierService()
        : this(AppDataPaths.GetMachineIdentityPath())
    {
    }

    public HardwareIdentifierService(string machineIdentityPath)
    {
        _machineIdentityPath = machineIdentityPath;
    }

    public string GetCurrentMachineId()
    {
        var persistedMachineId = TryLoadPersistedMachineId();
        if (persistedMachineId is not null)
        {
            return persistedMachineId;
        }

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

        var machineId = BuildMachineId(parts);
        TryPersistMachineId(machineId);
        return TryLoadPersistedMachineId() ?? machineId;
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

    private string? TryLoadPersistedMachineId()
    {
        try
        {
            if (!File.Exists(_machineIdentityPath))
            {
                return null;
            }

            var protectedValue = File.ReadAllBytes(_machineIdentityPath);
            var value = Encoding.UTF8.GetString(
                    ProtectedData.Unprotect(
                        protectedValue,
                        MachineIdentityEntropy,
                        DataProtectionScope.LocalMachine))
                .Trim()
                .ToUpperInvariant();
            return IsMachineId(value) ? value : null;
        }
        catch
        {
            return null;
        }
    }

    private void TryPersistMachineId(string machineId)
    {
        try
        {
            var directory = Path.GetDirectoryName(_machineIdentityPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            var protectedValue = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(machineId),
                MachineIdentityEntropy,
                DataProtectionScope.LocalMachine);
            using var stream = new FileStream(
                _machineIdentityPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read);
            stream.Write(protectedValue);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool IsMachineId(string value)
    {
        var parts = value.Split('-');
        return parts.Length == 9 &&
            string.Equals(parts[0], "FLX", StringComparison.Ordinal) &&
            parts[1..].All(part => part.Length == 4 && part.All(Uri.IsHexDigit));
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
