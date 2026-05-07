using FluxRAM.App.Licensing;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class ProtectedAppsStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsDistinctProtectedPaths()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "protected-apps.txt");
        var store = new ProtectedAppsStore(path);

        store.Save([
            "C:\\Apps\\Game\\game.exe",
            "C:\\Apps\\Game\\game.exe",
            "C:\\Tools\\OBS\\obs64.exe"
        ]);

        var loaded = store.Load();

        Assert.Equal(2, loaded.Count);
        Assert.Contains("game.exe", loaded[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("obs64.exe", loaded[1], StringComparison.OrdinalIgnoreCase);
    }
}
