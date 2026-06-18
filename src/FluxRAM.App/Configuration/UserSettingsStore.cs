using System.IO;
using System.Text.Json;
using FluxRAM.App.Diagnostics;
using FluxRAM.App.Licensing;
using FluxRAM.App.ViewModels;
using FluxRAM.Core.Models;

namespace FluxRAM.App.Configuration;

public sealed class UserSettingsStore
{
    private readonly string _path;

    public UserSettingsStore()
        : this(AppDataPaths.GetUserSettingsPath())
    {
    }

    public UserSettingsStore(string path)
    {
        _path = path;
    }

    public UiLanguage LoadLanguage()
    {
        return UiLanguageCatalog.FromCode(LoadSettings().LanguageCode);
    }

    public AppTheme LoadTheme()
    {
        return AppThemeCatalog.FromCode(LoadSettings().ThemeCode);
    }

    public bool LoadStartupAutoBoost()
    {
        return LoadSettings().StartupAutoBoost;
    }

    public bool LoadAutoBoost()
    {
        return LoadSettings().AutoBoost;
    }

    public OptimizerProfile LoadProfile()
    {
        return Enum.TryParse<OptimizerProfile>(LoadSettings().ProfileCode, true, out var profile)
            ? profile
            : OptimizerProfile.Conservative;
    }

    public void SaveLanguage(UiLanguage language)
    {
        var settings = LoadSettings();
        settings.LanguageCode = UiLanguageCatalog.ToCode(language);
        SaveSettings(settings);
    }

    public void SaveTheme(AppTheme theme)
    {
        var settings = LoadSettings();
        settings.ThemeCode = AppThemeCatalog.ToCode(theme);
        SaveSettings(settings);
    }

    public void SaveStartupAutoBoost(bool isEnabled)
    {
        var settings = LoadSettings();
        settings.StartupAutoBoost = isEnabled;
        SaveSettings(settings);
    }

    public void SaveAutoBoost(bool isEnabled)
    {
        var settings = LoadSettings();
        settings.AutoBoost = isEnabled;
        SaveSettings(settings);
    }

    public void SaveProfile(OptimizerProfile profile)
    {
        var settings = LoadSettings();
        settings.ProfileCode = profile.ToString();
        SaveSettings(settings);
    }

    private UserSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new UserSettings();
            }

            return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(_path)) ?? new UserSettings();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warning("Unable to load user settings.", ex);
            return new UserSettings();
        }
    }

    private void SaveSettings(UserSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warning("Unable to save user settings.", ex);
        }
    }

    private sealed class UserSettings
    {
        public string? LanguageCode { get; set; }

        public string? ThemeCode { get; set; }

        public bool StartupAutoBoost { get; set; }

        public bool AutoBoost { get; set; }

        public string? ProfileCode { get; set; }
    }
}
