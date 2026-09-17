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
        const string machineId = "FLX-1111-2222-3333-4444-5555-6666-7777-8888";
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, machineId);

        var service = new HardwareIdentifierService(path);

        Assert.Equal(machineId, service.GetCurrentMachineId());
    }

    [Fact]
    public void GetCurrentMachineId_PersistsGeneratedMachineId()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "machine.id");
        var service = new HardwareIdentifierService(path);

        var machineId = service.GetCurrentMachineId();

        Assert.Equal(machineId, File.ReadAllText(path));
    }
}
