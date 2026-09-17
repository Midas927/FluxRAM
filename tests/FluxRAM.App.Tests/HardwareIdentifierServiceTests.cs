using FluxRAM.App.Licensing;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class HardwareIdentifierServiceTests
{
    [Fact]
    public void GetCurrentMachineId_ReusesPersistedMachineId()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "machine.id");
        var firstService = new HardwareIdentifierService(path);
        var machineId = firstService.GetCurrentMachineId();
        var secondService = new HardwareIdentifierService(path);

        Assert.Equal(machineId, secondService.GetCurrentMachineId());
    }

    [Fact]
    public void GetCurrentMachineId_PersistsGeneratedMachineId()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "machine.id");
        var service = new HardwareIdentifierService(path);

        var machineId = service.GetCurrentMachineId();

        Assert.NotEqual(machineId, File.ReadAllText(path));

        var reloadedMachineId = new HardwareIdentifierService(path).GetCurrentMachineId();
        Assert.Equal(machineId, reloadedMachineId);
    }
}
