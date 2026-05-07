using System.Security.Cryptography;
using FluxRAM.App.Licensing;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class LicenseKeyVerifierTests
{
    [Fact]
    public void Verify_AcceptsSignedProLicenseForCurrentMachine()
    {
        using var rsa = RSA.Create(2048);
        var verifier = new LicenseKeyVerifier(ExportPublicKeyPem(rsa));
        var licenseKey = LicenseKeyVerifier.CreateSignedLicenseKey(
            new LicensePayload(1, "FluxRAM", "Pro", "FLX-ABCD-1234", DateTimeOffset.UtcNow),
            rsa);

        var result = verifier.Verify(licenseKey, "FLX-ABCD-1234");

        Assert.True(result.IsValid);
        Assert.Equal("Pro", result.Payload?.Edition);
    }

    [Fact]
    public void Verify_RejectsLicenseForAnotherMachine()
    {
        using var rsa = RSA.Create(2048);
        var verifier = new LicenseKeyVerifier(ExportPublicKeyPem(rsa));
        var licenseKey = LicenseKeyVerifier.CreateSignedLicenseKey(
            new LicensePayload(1, "FluxRAM", "Pro", "FLX-ABCD-1234", DateTimeOffset.UtcNow),
            rsa);

        var result = verifier.Verify(licenseKey, "FLX-WXYZ-9999");

        Assert.False(result.IsValid);
        Assert.Equal(LicenseVerificationFailure.MachineMismatch, result.Failure);
    }

    [Fact]
    public void Verify_RejectsTamperedPayload()
    {
        using var rsa = RSA.Create(2048);
        var verifier = new LicenseKeyVerifier(ExportPublicKeyPem(rsa));
        var licenseKey = LicenseKeyVerifier.CreateSignedLicenseKey(
            new LicensePayload(1, "FluxRAM", "Pro", "FLX-ABCD-1234", DateTimeOffset.UtcNow),
            rsa);
        var tampered = licenseKey.Replace("A", "B", StringComparison.Ordinal);

        var result = verifier.Verify(tampered, "FLX-ABCD-1234");

        Assert.False(result.IsValid);
    }

    private static string ExportPublicKeyPem(RSA rsa)
    {
        var base64 = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        return $"-----BEGIN PUBLIC KEY-----\n{base64}\n-----END PUBLIC KEY-----";
    }
}
