using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FluxRAM.Keygen;

public static class LicenseKeyGenerator
{
    private const string LicensePrefix = "FLX1-";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string GenerateProKey(string machineId, string privateKeyXml)
    {
        if (string.IsNullOrWhiteSpace(machineId))
        {
            throw new ArgumentException("Machine ID is required.", nameof(machineId));
        }

        if (string.IsNullOrWhiteSpace(privateKeyXml))
        {
            throw new ArgumentException("Private key is required.", nameof(privateKeyXml));
        }

        using var rsa = RSA.Create();
        rsa.FromXmlString(privateKeyXml.Trim());

        var payload = new LicensePayload(
            Version: 1,
            Product: "FluxRAM",
            Edition: "Pro",
            MachineId: machineId.Trim().ToUpperInvariant(),
            IssuedAt: DateTimeOffset.UtcNow);
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var signatureBytes = rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return LicensePrefix + EncodeBase64Url(payloadBytes) + "." + EncodeBase64Url(signatureBytes);
    }

    private static string EncodeBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed record LicensePayload(
        int Version,
        string Product,
        string Edition,
        string MachineId,
        DateTimeOffset IssuedAt);
}
