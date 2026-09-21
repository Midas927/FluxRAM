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
    public void Verify_AcceptsLicenseSignedByTrustedPreviousKey()
    {
        using var currentRsa = RSA.Create(2048);
        using var previousRsa = RSA.Create(2048);
        var verifier = new LicenseKeyVerifier(
            ExportPublicKeyPem(currentRsa),
            ExportPublicKeyPem(previousRsa));
        var licenseKey = LicenseKeyVerifier.CreateSignedLicenseKey(
            new LicensePayload(1, "FluxRAM", "Pro", "FLX-ABCD-1234", DateTimeOffset.UtcNow),
            previousRsa);

        var result = verifier.Verify(licenseKey, "FLX-ABCD-1234");

        Assert.True(result.IsValid);
    }

    [Fact]
    public void VerifyClaims_AcceptsValidPayloadWithoutTreatingItAsMachineAuthorization()
    {
        using var rsa = RSA.Create(2048);
        var verifier = new LicenseKeyVerifier(ExportPublicKeyPem(rsa));
        var licenseKey = LicenseKeyVerifier.CreateSignedLicenseKey(
            new LicensePayload(1, "FluxRAM", "Pro", "FLX-OTHER-MACHINE", DateTimeOffset.UtcNow),
            rsa);

        var result = verifier.VerifyClaims(licenseKey);

        Assert.True(result.IsValid);
        Assert.Equal("FLX-OTHER-MACHINE", result.Payload?.MachineId);
        Assert.False(verifier.Verify(licenseKey, "FLX-LOCAL-MACHINE").IsValid);
    }

    [Fact]
    public void Verify_RejectsUnsupportedPayloadVersionEvenWhenSignatureIsValid()
    {
        using var rsa = RSA.Create(2048);
        var verifier = new LicenseKeyVerifier(ExportPublicKeyPem(rsa));
        var licenseKey = LicenseKeyVerifier.CreateSignedLicenseKey(
            new LicensePayload(2, "FluxRAM", "Pro", "FLX-ABCD-1234", DateTimeOffset.UtcNow),
            rsa);

        var result = verifier.VerifyClaims(licenseKey);

        Assert.False(result.IsValid);
        Assert.Equal(LicenseVerificationFailure.UnsupportedVersion, result.Failure);
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
    public void VerifyClaims_RejectsWrongProductAndEdition()
    {
        using var rsa = RSA.Create(2048);
        var verifier = new LicenseKeyVerifier(ExportPublicKeyPem(rsa));
        var wrongProduct = LicenseKeyVerifier.CreateSignedLicenseKey(
            new LicensePayload(1, "Other", "Pro", "FLX-ABCD-1234", DateTimeOffset.UtcNow), rsa);
        var wrongEdition = LicenseKeyVerifier.CreateSignedLicenseKey(
            new LicensePayload(1, "FluxRAM", "Free", "FLX-ABCD-1234", DateTimeOffset.UtcNow), rsa);

        Assert.Equal(LicenseVerificationFailure.WrongProduct, verifier.VerifyClaims(wrongProduct).Failure);
        Assert.Equal(LicenseVerificationFailure.WrongEdition, verifier.VerifyClaims(wrongEdition).Failure);
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
