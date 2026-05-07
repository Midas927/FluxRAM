using System.Security.Cryptography;
using FluxRAM.App.Configuration;
using FluxRAM.App.Licensing;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class LicenseManagerTests
{
    [Fact]
    public void GetStatus_DefaultsToFreeWhenNoLicenseIsStored()
    {
        using var rsa = RSA.Create(2048);
        var store = new LicenseActivationStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "license.key"));
        var manager = new LicenseManager(
            new StaticHardwareIdentifierProvider("FLX-LOCAL-MACHINE"),
            new LicenseKeyVerifier(ExportPublicKeyPem(rsa)),
            store,
            AppEdition.Free);

        var status = manager.GetStatus();

        Assert.Equal(AppEdition.Free, status.Features.Edition);
        Assert.False(status.Features.SupportsExtremeProfile);
        Assert.True(status.Features.SupportsProtectList);
        Assert.False(status.Features.SupportsAdvancedProtection);
    }

    [Fact]
    public void Activate_StoresValidProLicenseAndUnlocksFeatures()
    {
        using var rsa = RSA.Create(2048);
        var store = new LicenseActivationStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "license.key"));
        var manager = new LicenseManager(
            new StaticHardwareIdentifierProvider("FLX-LOCAL-MACHINE"),
            new LicenseKeyVerifier(ExportPublicKeyPem(rsa)),
            store,
            AppEdition.Free);
        var licenseKey = LicenseKeyVerifier.CreateSignedLicenseKey(
            new LicensePayload(1, "FluxRAM", "Pro", "FLX-LOCAL-MACHINE", DateTimeOffset.UtcNow),
            rsa);

        var status = manager.Activate(licenseKey);
        var reloadedStatus = manager.GetStatus();

        Assert.Equal(AppEdition.Pro, status.Features.Edition);
        Assert.Equal(AppEdition.Pro, reloadedStatus.Features.Edition);
        Assert.True(reloadedStatus.Features.SupportsExtremeProfile);
        Assert.True(reloadedStatus.Features.SupportsProtectList);
        Assert.True(reloadedStatus.Features.SupportsAdvancedProtection);
    }

    [Fact]
    public void Activate_DoesNotStoreInvalidLicense()
    {
        using var rsa = RSA.Create(2048);
        var store = new LicenseActivationStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "license.key"));
        var manager = new LicenseManager(
            new StaticHardwareIdentifierProvider("FLX-LOCAL-MACHINE"),
            new LicenseKeyVerifier(ExportPublicKeyPem(rsa)),
            store,
            AppEdition.Free);

        var status = manager.Activate("not-a-real-key");

        Assert.Equal(AppEdition.Free, status.Features.Edition);
        Assert.Null(store.Load());
    }

    private static string ExportPublicKeyPem(RSA rsa)
    {
        var base64 = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        return $"-----BEGIN PUBLIC KEY-----\n{base64}\n-----END PUBLIC KEY-----";
    }

    private sealed class StaticHardwareIdentifierProvider : IHardwareIdentifierProvider
    {
        public StaticHardwareIdentifierProvider(string machineId)
        {
            MachineId = machineId;
        }

        public string MachineId { get; }

        public string GetCurrentMachineId() => MachineId;
    }
}
