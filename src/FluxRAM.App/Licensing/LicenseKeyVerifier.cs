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
    UnsupportedVersion,
    WrongProduct,
    WrongEdition,
    MachineMismatch,
    LegacyKeyRequiresReplacement,
    LegacyIdentityUnavailable,
    StorageError,
    HardwareUnavailable
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

    private readonly IReadOnlyList<string> _trustedPublicKeys;

    public LicenseKeyVerifier()
        : this(DefaultPublicKey, PreviousPublicKey)
    {
    }

    public LicenseKeyVerifier(string publicKey, params string[] additionalPublicKeys)
    {
        _trustedPublicKeys = new[] { publicKey }.Concat(additionalPublicKeys).ToArray();
    }

    public LicenseVerificationResult Verify(string licenseKey, string currentMachineId)
    {
        var claims = VerifyClaims(licenseKey);
        if (!claims.IsValid || claims.Payload is null)
        {
            return claims;
        }

        return string.Equals(
            NormalizeMachineId(claims.Payload.MachineId),
            NormalizeMachineId(currentMachineId),
            StringComparison.Ordinal)
            ? claims
            : LicenseVerificationResult.Invalid(LicenseVerificationFailure.MachineMismatch);
    }

    public LicenseVerificationResult VerifyClaims(string licenseKey)
    {
        try
        {
            var normalized = NormalizeLicenseKey(licenseKey);
            if (!normalized.StartsWith(LicensePrefix, StringComparison.Ordinal))
            {
                return LicenseVerificationResult.Invalid(LicenseVerificationFailure.Malformed);
            }

            var parts = normalized[LicensePrefix.Length..].Split('.');
            if (parts.Length != 2 || parts.Any(string.IsNullOrEmpty))
            {
                return LicenseVerificationResult.Invalid(LicenseVerificationFailure.Malformed);
            }

            var payloadBytes = DecodeBase64Url(parts[0]);
            var signatureBytes = DecodeBase64Url(parts[1]);
            if (!_trustedPublicKeys.Any(publicKey => VerifySignature(payloadBytes, signatureBytes, publicKey)))
            {
                return LicenseVerificationResult.Invalid(LicenseVerificationFailure.InvalidSignature);
            }

            var payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes, JsonOptions);
            if (payload is null)
            {
                return LicenseVerificationResult.Invalid(LicenseVerificationFailure.Malformed);
            }

            if (payload.Version != 1)
            {
                return LicenseVerificationResult.Invalid(LicenseVerificationFailure.UnsupportedVersion);
            }

            if (!string.Equals(payload.Product, ProductId, StringComparison.Ordinal))
            {
                return LicenseVerificationResult.Invalid(LicenseVerificationFailure.WrongProduct);
            }

            if (!string.Equals(payload.Edition, "Pro", StringComparison.OrdinalIgnoreCase))
            {
                return LicenseVerificationResult.Invalid(LicenseVerificationFailure.WrongEdition);
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

    private static bool VerifySignature(byte[] payloadBytes, byte[] signatureBytes, string publicKey)
    {
        using var rsa = RSA.Create();
        ImportPublicKey(rsa, publicKey);
        return rsa.VerifyData(
            payloadBytes,
            signatureBytes,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
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
        "<RSAKeyValue><Modulus>xMI0UDL//A536V6Kbi8PfLsBXUE/QChb7839aquXFGGrU+iG/mwAQf4P/DUFI8qDZiof6kvEBd529oap8LaCvo7KwmMOv8zKUCOQthQFsdbk6a8DjRbvPhzm8aB6NM3hYWcRI588KJ0YKcPMtSXYOq5HZwCTJ02EUgwrg3ftMh+7TeBPlFNaqrLBdLy39hL13A/svxSt8iTFgM8ifFgufhTOVcKxV78cayTuziF7GP94pKxAqg23ptrczNXRN/csm5rzztHnSpUicym3r0Gfy5TWZEOlgzT3NUyi1G3kbncsrHnHydxY9JhYlaExXtiOdMEAgNZQvA0/xSq8uxFAGQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

    // Keep the previous signing key so already activated copies survive key rotation.
    private const string PreviousPublicKey =
        "<RSAKeyValue><Modulus>nCt2RYUHG08d617d+KqHReIiJ3avzke8tz8/zumJDvi9bw688A0G1MYa7xE0/OUDpKG+6MpfC9+zJ/KKNtYe4XS8GF050tYI4L8aJ8dAEfN/k/0oAo0BjWuKxXBJS0uxb3vIjLeDLcvGo8LAEGlg1dv1lSxTdqgf2ohx3ptjEp19cCC/wVwPMtpLpTb+14khnSMgNKfnWWyvLXx9ZLECSFh19co5BC6u1JhdNT9VxcRGSi7iOY2LkQtXjg2NBqGT4Y0qEFC8Pemza58ktkygnzoXTbbaEngW5H/yCsjbjtDvbetPDjhMU1z4FvxLDH9Ai8LSM5B6NoFeK9b1MOOBKQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
}
