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
        using var fixture = new LicenseFixture();

        var status = fixture.Manager.GetStatus();

        Assert.Equal(AppEdition.Free, status.Features.Edition);
        Assert.False(status.Features.SupportsExtremeProfile);
        Assert.True(status.Features.SupportsProtectList);
        Assert.False(status.Features.SupportsAdvancedProtection);
        Assert.Equal(LicenseVerificationFailure.None, status.Failure);
    }

    [Fact]
    public void Activate_StoresStableMachineLicense()
    {
        using var fixture = new LicenseFixture();
        var licenseKey = fixture.CreateKey(fixture.Hardware.StableMachineId);

        var status = fixture.Manager.Activate(licenseKey);
        var reloadedStatus = fixture.Manager.GetStatus();

        Assert.Equal(AppEdition.Pro, status.Features.Edition);
        Assert.True(status.IsActivated);
        Assert.Equal(AppEdition.Pro, reloadedStatus.Features.Edition);
        Assert.True(reloadedStatus.Features.SupportsExtremeProfile);
    }

    [Fact]
    public void Activate_DoesNotStoreInvalidLicense()
    {
        using var fixture = new LicenseFixture();

        var status = fixture.Manager.Activate("not-a-real-key");

        Assert.Equal(AppEdition.Free, status.Features.Edition);
        Assert.Null(fixture.ActivationStore.Load());
    }

    [Fact]
    public void LegacyLicense_IsSessionOnlyAndRequiresSignedReplacement()
    {
        using var fixture = new LicenseFixture();
        var licenseKey = fixture.CreateKey(fixture.Hardware.LegacyMachineId);

        var status = fixture.Manager.Activate(licenseKey);

        Assert.Equal(AppEdition.Pro, status.Features.Edition);
        Assert.False(status.IsActivated);
        Assert.Equal(LicenseVerificationFailure.LegacyKeyRequiresReplacement, status.Failure);
        Assert.Null(fixture.ActivationStore.Load());
    }

    [Fact]
    public void StoredLegacyLicense_RequiresExplicitReactivationAndStopsMatchingAfterNetworkChange()
    {
        using var fixture = new LicenseFixture();
        var licenseKey = fixture.CreateKey(fixture.Hardware.LegacyMachineId);
        Assert.True(fixture.ActivationStore.Save(licenseKey));

        var beforeNetworkChange = fixture.Manager.GetStatus();
        var explicitReactivation = fixture.Manager.Activate(licenseKey);
        fixture.Hardware.LegacyMachineId = "FLX-LEGACY-AFTER-NETWORK-CHANGE";
        var afterNetworkChange = fixture.Manager.GetStatus();

        Assert.Equal(AppEdition.Free, beforeNetworkChange.Features.Edition);
        Assert.False(beforeNetworkChange.IsActivated);
        Assert.Equal(LicenseVerificationFailure.LegacyKeyRequiresReplacement, beforeNetworkChange.Failure);
        Assert.Equal(AppEdition.Pro, explicitReactivation.Features.Edition);
        Assert.False(explicitReactivation.IsActivated);
        Assert.Equal(LicenseVerificationFailure.LegacyKeyRequiresReplacement, explicitReactivation.Failure);
        Assert.Equal(AppEdition.Free, afterNetworkChange.Features.Edition);
        Assert.Equal(LicenseVerificationFailure.MachineMismatch, afterNetworkChange.Failure);
    }

    [Fact]
    public void LegacyLicense_DoesNotRecoverAfterNetworkAlreadyChanged()
    {
        using var fixture = new LicenseFixture();
        var licenseKey = fixture.CreateKey("FLX-LEGACY-BEFORE-NETWORK-CHANGE");
        fixture.Hardware.LegacyMachineId = "FLX-LEGACY-AFTER-NETWORK-CHANGE";

        var status = fixture.Manager.Activate(licenseKey);

        Assert.Equal(AppEdition.Free, status.Features.Edition);
        Assert.Equal(LicenseVerificationFailure.MachineMismatch, status.Failure);
    }

    [Fact]
    public void LegacyLicense_RequiresReplacementWhenPreviousIdentityCannotBeRead()
    {
        using var fixture = new LicenseFixture();
        var licenseKey = fixture.CreateKey("FLX-LEGACY-UNKNOWN-MACHINE");
        fixture.Hardware.LegacyIdentityAvailable = false;

        var status = fixture.Manager.Activate(licenseKey);

        Assert.Equal(AppEdition.Free, status.Features.Edition);
        Assert.Equal(LicenseVerificationFailure.LegacyIdentityUnavailable, status.Failure);
    }

    [Fact]
    public void CopiedSignedLicense_DoesNotActivateOnDifferentHardware()
    {
        using var fixture = new LicenseFixture();
        var licenseKey = fixture.CreateKey(fixture.Hardware.StableMachineId);
        Assert.True(fixture.ActivationStore.Save(licenseKey));
        var copiedHardware = new MutableHardwareIdentifierProvider(
            stableMachineId: "FLX-DIFFERENT-STABLE-MACHINE",
            legacyMachineId: "FLX-DIFFERENT-LEGACY-MACHINE");

        var status = fixture.CreateManager(copiedHardware).GetStatus();

        Assert.Equal(AppEdition.Free, status.Features.Edition);
        Assert.Equal(LicenseVerificationFailure.MachineMismatch, status.Failure);
    }

    [Fact]
    public void Activate_DoesNotReportPersistentActivationWhenLicenseCannotBeSaved()
    {
        using var fixture = new LicenseFixture(useDirectoryAsLicensePath: true);
        var licenseKey = fixture.CreateKey(fixture.Hardware.StableMachineId);

        var status = fixture.Manager.Activate(licenseKey);

        Assert.Equal(AppEdition.Free, status.Features.Edition);
        Assert.False(status.IsActivated);
        Assert.Equal(LicenseVerificationFailure.StorageError, status.Failure);
    }

    [Fact]
    public void GetStatus_ReportsStorageErrorWhenLicenseCannotBeRead()
    {
        using var fixture = new LicenseFixture(useDirectoryAsLicensePath: true);

        var status = fixture.Manager.GetStatus();

        Assert.Equal(AppEdition.Free, status.Features.Edition);
        Assert.Equal(LicenseVerificationFailure.StorageError, status.Failure);
    }

    [Fact]
    public void GetStatus_ReportsStorageErrorWhenLicenseFileIsEmpty()
    {
        using var fixture = new LicenseFixture();
        File.WriteAllText(fixture.LicensePath, "   ");

        var status = fixture.Manager.GetStatus();

        Assert.Equal(AppEdition.Free, status.Features.Edition);
        Assert.Equal(LicenseVerificationFailure.StorageError, status.Failure);
    }

    private sealed class LicenseFixture : IDisposable
    {
        private readonly RSA _rsa = RSA.Create(2048);
        private readonly string _root = Path.Combine(Path.GetTempPath(), "FluxRAM.Tests", Guid.NewGuid().ToString("N"));

        public LicenseFixture(bool useDirectoryAsLicensePath = false)
        {
            Directory.CreateDirectory(_root);
            LicensePath = useDirectoryAsLicensePath ? _root : Path.Combine(_root, "license.key");
            Hardware = new MutableHardwareIdentifierProvider(
                stableMachineId: "FLX-STABLE-LOCAL-MACHINE",
                legacyMachineId: "FLX-LEGACY-LOCAL-MACHINE");
            ActivationStore = new LicenseActivationStore(LicensePath);
            Manager = CreateManager(Hardware);
        }

        public string LicensePath { get; }
        public MutableHardwareIdentifierProvider Hardware { get; }
        public LicenseActivationStore ActivationStore { get; }
        public LicenseManager Manager { get; }

        public string CreateKey(string machineId)
        {
            return LicenseKeyVerifier.CreateSignedLicenseKey(
                new LicensePayload(1, "FluxRAM", "Pro", machineId, DateTimeOffset.UtcNow),
                _rsa);
        }

        public LicenseManager CreateManager(IHardwareIdentifierProvider hardware)
        {
            return new LicenseManager(
                hardware,
                new LicenseKeyVerifier(ExportPublicKeyPem(_rsa)),
                ActivationStore);
        }

        public void Dispose()
        {
            _rsa.Dispose();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }

    private static string ExportPublicKeyPem(RSA rsa)
    {
        var base64 = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        return $"-----BEGIN PUBLIC KEY-----\n{base64}\n-----END PUBLIC KEY-----";
    }

    private sealed class MutableHardwareIdentifierProvider : IHardwareIdentifierProvider
    {
        public MutableHardwareIdentifierProvider(string stableMachineId, string legacyMachineId)
        {
            StableMachineId = stableMachineId;
            LegacyMachineId = legacyMachineId;
        }

        public string StableMachineId { get; set; }
        public string LegacyMachineId { get; set; }
        public bool LegacyIdentityAvailable { get; set; } = true;

        public string GetCurrentMachineId() => StableMachineId;
        public string GetLegacyMachineId() => LegacyIdentityAvailable
            ? LegacyMachineId
            : throw new InvalidOperationException("Legacy identity unavailable.");
    }
}
