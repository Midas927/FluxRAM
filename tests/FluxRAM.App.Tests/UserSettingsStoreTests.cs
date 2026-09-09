using FluxRAM.App.Configuration;
using FluxRAM.App.ViewModels;
using FluxRAM.Core.Models;
using System.Text.Json;
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

    [Fact]
    public void LoadSkippedUpdateVersion_WhenFileIsMissing_ReturnsNull()
    {
        var store = new UserSettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json"));

        Assert.Null(store.LoadSkippedUpdateVersion());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ not-json")]
    public void LoadSkippedUpdateVersion_WhenLegacyOrMalformed_ReturnsNull(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);

        Assert.Null(new UserSettingsStore(path).LoadSkippedUpdateVersion());
    }

    [Fact]
    public void SaveSkippedUpdateVersion_PersistsAcrossInstancesAndPreservesExistingFields()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            {"LanguageCode":"ja","ThemeCode":"light","StartupAutoBoost":true,"AutoBoost":true,
             "ProfileCode":"Balanced","FutureSetting":{"enabled":true,"value":7}}
            """);
        var store = new UserSettingsStore(path);

        store.SaveSkippedUpdateVersion("v0.4.2");

        var reloaded = new UserSettingsStore(path);
        Assert.Equal("v0.4.2", reloaded.LoadSkippedUpdateVersion());
        Assert.Equal(UiLanguage.Japanese, reloaded.LoadLanguage());
        Assert.Equal(AppTheme.Light, reloaded.LoadTheme());
        Assert.True(reloaded.LoadStartupAutoBoost());
        Assert.True(reloaded.LoadAutoBoost());
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("Balanced", document.RootElement.GetProperty("ProfileCode").GetString());
        Assert.True(document.RootElement.GetProperty("FutureSetting").GetProperty("enabled").GetBoolean());
        Assert.Equal(7, document.RootElement.GetProperty("FutureSetting").GetProperty("value").GetInt32());
    }

    [Fact]
    public void SaveOtherSettings_PreservesSkippedVersion()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        new UserSettingsStore(path).SaveSkippedUpdateVersion("v0.4.2");
        var store = new UserSettingsStore(path);

        store.SaveLanguage(UiLanguage.Korean);
        store.SaveTheme(AppTheme.Light);
        store.SaveStartupAutoBoost(true);
        store.SaveAutoBoost(true);
        store.SaveProfile(OptimizerProfile.Aggressive);

        Assert.Equal("v0.4.2", new UserSettingsStore(path).LoadSkippedUpdateVersion());
    }

    [Fact]
    public void SaveSkippedUpdateVersion_CanReplaceAndClearSkipWithoutChangingPreferences()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        var store = new UserSettingsStore(path);
        store.SaveLanguage(UiLanguage.Korean);
        store.SaveSkippedUpdateVersion("v0.4.2");

        store.SaveSkippedUpdateVersion("v0.4.3");
        Assert.Equal("v0.4.3", new UserSettingsStore(path).LoadSkippedUpdateVersion());
        store.SaveSkippedUpdateVersion(null);

        Assert.Null(new UserSettingsStore(path).LoadSkippedUpdateVersion());
        Assert.Equal(UiLanguage.Korean, store.LoadLanguage());
    }
}
