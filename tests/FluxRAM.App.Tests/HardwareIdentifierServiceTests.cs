using FluxRAM.App.Licensing;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class HardwareIdentifierServiceTests
{
    [Fact]
    public void GetCurrentMachineId_UsesSystemUuidAndIgnoresNetworkChanges()
    {
        var first = CreateService(
            systemUuid: " 00112233-4455-6677-8899-aabbccddeeff ",
            machineGuid: "MACHINE-A",
            macAddresses: ["001122334455"]);
        var second = CreateService(
            systemUuid: "00112233-4455-6677-8899-AABBCCDDEEFF",
            machineGuid: "MACHINE-B",
            macAddresses: ["FFEEDDCCBBAA"]);

        var expected = HardwareIdentifierService.BuildMachineId(
            ["smbios:00112233-4455-6677-8899-AABBCCDDEEFF"]);

        Assert.Equal(expected, first.GetCurrentMachineId());
        Assert.Equal(expected, second.GetCurrentMachineId());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF")]
    [InlineData("Default string")]
    public void GetCurrentMachineId_RejectsFallbackWhenSystemUuidIsUnavailable(string? invalidUuid)
    {
        var service = CreateService(
            invalidUuid,
            "MACHINE-A",
            ["001122334455"]);

        Assert.Throws<InvalidOperationException>(() => service.GetCurrentMachineId());
    }

    [Fact]
    public void GetCurrentMachineId_ThrowsWhenNoStableIdentityExists()
    {
        var service = CreateService(null, null, []);

        Assert.Throws<InvalidOperationException>(() => service.GetCurrentMachineId());
    }

    [Fact]
    public void GetLegacyMachineId_PreservesPreviousMachineGuidAndActiveMacAlgorithm()
    {
        var service = CreateService(
            "00112233-4455-6677-8899-AABBCCDDEEFF",
            "machine-guid-a",
            ["ffeeddccbbaa", "001122334455", "001122334455"]);

        var expected = HardwareIdentifierService.BuildMachineId(
            ["machine:machine-guid-a", "mac:001122334455", "mac:FFEEDDCCBBAA"]);

        Assert.Equal(expected, service.GetLegacyMachineId());
    }

    [Fact]
    public void GetLegacyMachineId_ChangesWithNetworkButCurrentMachineIdDoesNot()
    {
        var facts = new HardwareIdentifierFacts(
            "00112233-4455-6677-8899-AABBCCDDEEFF",
            "MACHINE-A",
            ["001122334455"]);
        var service = new HardwareIdentifierService(() => facts);
        var current = service.GetCurrentMachineId();
        var legacy = service.GetLegacyMachineId();

        facts = facts with { MacAddresses = ["FFEEDDCCBBAA"] };

        Assert.Equal(current, service.GetCurrentMachineId());
        Assert.NotEqual(legacy, service.GetLegacyMachineId());
    }

    [Fact]
    public void GetLegacyMachineId_RejectsIncompleteLegacyProof()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CreateService(null, null, ["001122334455"]).GetLegacyMachineId());
        Assert.Throws<InvalidOperationException>(() =>
            CreateService(null, "MACHINE-A", []).GetLegacyMachineId());
        Assert.Throws<InvalidOperationException>(() =>
            CreateService(null, null, []).GetLegacyMachineId());
    }

    [Fact]
    public void GetCurrentMachineId_DoesNotHideHardwareQueryFailure()
    {
        var service = new HardwareIdentifierService(
            () => throw new InvalidOperationException("WMI unavailable"));

        Assert.Throws<InvalidOperationException>(() => service.GetCurrentMachineId());
    }

    private static HardwareIdentifierService CreateService(
        string? systemUuid,
        string? machineGuid,
        IReadOnlyList<string> macAddresses)
    {
        return new HardwareIdentifierService(() => new HardwareIdentifierFacts(
            systemUuid,
            machineGuid,
            macAddresses));
    }
}
