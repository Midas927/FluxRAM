using FluxRAM.App.Configuration;
using FluxRAM.App.ViewModels;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class UserSettingsStoreTests
{
    [Fact]
    public void LoadLanguage_WhenFileIsMissing_ReturnsEnglish()
    {
        var store = new UserSettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json"));

        Assert.Equal(UiLanguage.English, store.LoadLanguage());
    }

    [Fact]
    public void SaveLanguage_PersistsSelectedLanguage()
    {
        var store = new UserSettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json"));

        store.SaveLanguage(UiLanguage.Korean);

        Assert.Equal(UiLanguage.Korean, store.LoadLanguage());
    }

    [Fact]
    public void LoadLanguage_WhenFileIsMalformed_ReturnsEnglish()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not-json");
        var store = new UserSettingsStore(path);

        Assert.Equal(UiLanguage.English, store.LoadLanguage());
    }
}
