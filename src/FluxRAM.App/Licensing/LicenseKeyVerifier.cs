using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FluxRAM.App.Licensing;

public sealed record LicensePayload(
    int Version,
    string Product,
    string Edition,
    string MachineId,
    DateTimeOffset IssuedAt);

public enum LicenseVerificationFailure
{
    None,
    Malformed,
    InvalidSignature,
    WrongProduct,
    WrongEdition,
    MachineMismatch
}

public sealed record LicenseVerificationResult(
    bool IsValid,
    LicensePayload? Payload,
    LicenseVerificationFailure Failure)
{
    public static LicenseVerificationResult Valid(LicensePayload payload)
    {
        return new LicenseVerificationResult(true, payload, LicenseVerificationFailure.None);
    }

    public static LicenseVerificationResult Invalid(LicenseVerificationFailure failure)
    {
        return new LicenseVerificationResult(false, null, failure);
    }
}

public sealed class LicenseKeyVerifier
{
    public const string ProductId = "FluxRAM";

    private const string LicensePrefix = "FLX1-";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _publicKey;

    public LicenseKeyVerifier()
        : this(DefaultPublicKey)
    {
    }

    public LicenseKeyVerifier(string publicKey)
    {
        _publicKey = publicKey;
    }

    public LicenseVerificationResult Verify(string licenseKey, string currentMachineId)
    {
        try
        {
            var normalized = NormalizeLicenseKey(licenseKey);
            if (!normalized.StartsWith(LicensePrefix, StringComparison.Ordinal))
            {
                return LicenseVerificationResult.Invalid(LicenseVerificationFailure.Malformed);
            }

            var parts = normalized[LicensePrefix.Length..].Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                return LicenseVerificationResult.Invalid(LicenseVerificationFailure.Malformed);
            }

            var payloadBytes = DecodeBase64Url(parts[0]);
            var signatureBytes = DecodeBase64Url(parts[1]);
            using var rsa = RSA.Create();
            ImportPublicKey(rsa, _publicKey);

            var isSignatureValid = rsa.VerifyData(
                payloadBytes,
                signatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            if (!isSignatureValid)
            {
                return LicenseVerificationResult.Invalid(LicenseVerificationFailure.InvalidSignature);
            }

            var payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes, JsonOptions);
            if (payload is null)
            {
                return LicenseVerificationResult.Invalid(LicenseVerificationFailure.Malformed);
            }

            if (!string.Equals(payload.Product, ProductId, StringComparison.Ordinal))
            {
                return LicenseVerificationResult.Invalid(LicenseVerificationFailure.WrongProduct);
            }

            if (!string.Equals(payload.Edition, "Pro", StringComparison.OrdinalIgnoreCase))
            {
                return LicenseVerificationResult.Invalid(LicenseVerificationFailure.WrongEdition);
            }

            if (!string.Equals(
                    NormalizeMachineId(payload.MachineId),
                    NormalizeMachineId(currentMachineId),
                    StringComparison.Ordinal))
            {
                return LicenseVerificationResult.Invalid(LicenseVerificationFailure.MachineMismatch);
            }

            return LicenseVerificationResult.Valid(payload);
        }
        catch
        {
            return LicenseVerificationResult.Invalid(LicenseVerificationFailure.Malformed);
        }
    }

    public static string CreateSignedLicenseKey(LicensePayload payload, RSA privateKey)
    {
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var signatureBytes = privateKey.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return LicensePrefix + EncodeBase64Url(payloadBytes) + "." + EncodeBase64Url(signatureBytes);
    }

    private static string NormalizeLicenseKey(string licenseKey)
    {
        return new string((licenseKey ?? string.Empty).Where(character => !char.IsWhiteSpace(character)).ToArray());
    }

    private static string NormalizeMachineId(string machineId)
    {
        return (machineId ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static void ImportPublicKey(RSA rsa, string publicKey)
    {
        var trimmed = publicKey.Trim();
        if (trimmed.StartsWith("<RSAKeyValue>", StringComparison.Ordinal))
        {
            rsa.FromXmlString(trimmed);
            return;
        }

        rsa.ImportFromPem(trimmed);
    }

    private static string EncodeBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }

    private const string DefaultPublicKey =
        "<RSAKeyValue><Modulus>nCt2RYUHG08d617d+KqHReIiJ3avzke8tz8/zumJDvi9bw688A0G1MYa7xE0/OUDpKG+6MpfC9+zJ/KKNtYe4XS8GF050tYI4L8aJ8dAEfN/k/0oAo0BjWuKxXBJS0uxb3vIjLeDLcvGo8LAEGlg1dv1lSxTdqgf2ohx3ptjEp19cCC/wVwPMtpLpTb+14khnSMgNKfnWWyvLXx9ZLECSFh19co5BC6u1JhdNT9VxcRGSi7iOY2LkQtXjg2NBqGT4Y0qEFC8Pemza58ktkygnzoXTbbaEngW5H/yCsjbjtDvbetPDjhMU1z4FvxLDH9Ai8LSM5B6NoFeK9b1MOOBKQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
}
