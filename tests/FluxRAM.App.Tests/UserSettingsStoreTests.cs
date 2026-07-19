using FluxRAM.App.Configuration;
using FluxRAM.App.ViewModels;
using FluxRAM.Core.Models;
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
    public void SaveTheme_PersistsSelectedTheme()
    {
        var store = new UserSettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json"));

        store.SaveTheme(AppTheme.Light);

        Assert.Equal(AppTheme.Light, store.LoadTheme());
    }

    [Fact]
    public void SaveTheme_DoesNotOverwriteLanguage()
    {
        var store = new UserSettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json"));

        store.SaveLanguage(UiLanguage.Japanese);
        store.SaveTheme(AppTheme.Light);

        Assert.Equal(UiLanguage.Japanese, store.LoadLanguage());
        Assert.Equal(AppTheme.Light, store.LoadTheme());
    }

    [Fact]
    public void SaveStartupAutoBoost_PersistsSelectedOption()
    {
        var store = new UserSettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json"));

        store.SaveStartupAutoBoost(true);

        Assert.True(store.LoadStartupAutoBoost());
    }

    [Fact]
    public void SaveStartupAutoBoost_DoesNotOverwriteLanguageOrTheme()
    {
        var store = new UserSettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json"));

        store.SaveLanguage(UiLanguage.Korean);
        store.SaveTheme(AppTheme.Light);
        store.SaveAutoBoost(true);
        store.SaveProfile(OptimizerProfile.Balanced);
        store.SaveStartupAutoBoost(true);

        Assert.Equal(UiLanguage.Korean, store.LoadLanguage());
        Assert.Equal(AppTheme.Light, store.LoadTheme());
        Assert.True(store.LoadAutoBoost());
        Assert.Equal(OptimizerProfile.GamingHandheld, store.LoadProfile());
        Assert.True(store.LoadStartupAutoBoost());
    }

    [Fact]
    public void SaveAutoBoost_PersistsSelectedOption()
    {
        var store = new UserSettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json"));

        store.SaveAutoBoost(true);

        Assert.True(store.LoadAutoBoost());
    }

    [Fact]
    public void SaveProfile_PersistsSelectedProfile()
    {
        var store = new UserSettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json"));

        store.SaveProfile(OptimizerProfile.Aggressive);

        Assert.Equal(OptimizerProfile.Aggressive, store.LoadProfile());
    }

    [Fact]
    public void SaveAutoBoostAndProfile_DoNotOverwriteOtherSettings()
    {
        var store = new UserSettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json"));

        store.SaveLanguage(UiLanguage.Japanese);
        store.SaveTheme(AppTheme.Light);
        store.SaveStartupAutoBoost(true);
        store.SaveAutoBoost(true);
        store.SaveProfile(OptimizerProfile.Balanced);

        Assert.Equal(UiLanguage.Japanese, store.LoadLanguage());
        Assert.Equal(AppTheme.Light, store.LoadTheme());
        Assert.True(store.LoadStartupAutoBoost());
        Assert.True(store.LoadAutoBoost());
        Assert.Equal(OptimizerProfile.GamingHandheld, store.LoadProfile());
    }

    [Fact]
    public void LoadLanguage_WhenFileIsMalformed_ReturnsEnglish()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not-json");
        var store = new UserSettingsStore(path);

        Assert.Equal(UiLanguage.English, store.LoadLanguage());
        Assert.Equal(AppTheme.Dark, store.LoadTheme());
        Assert.False(store.LoadStartupAutoBoost());
        Assert.False(store.LoadAutoBoost());
        Assert.Equal(OptimizerProfile.GamingHandheld, store.LoadProfile());
    }

    [Fact]
    public void LoadProfile_WhenFileIsMissing_ReturnsGaming()
    {
        var store = new UserSettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json"));

        Assert.Equal(OptimizerProfile.GamingHandheld, store.LoadProfile());
    }

    [Fact]
    public void LoadProfile_MigratesLegacyBalancedToGaming()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"ProfileCode":"Balanced"}""");
        var store = new UserSettingsStore(path);

        Assert.Equal(OptimizerProfile.GamingHandheld, store.LoadProfile());
    }
}
