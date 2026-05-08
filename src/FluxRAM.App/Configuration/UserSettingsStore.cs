using System.IO;
using System.Text.Json;
using FluxRAM.App.Licensing;
using FluxRAM.App.ViewModels;

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
        catch
        {
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
        catch
        {
        }
    }

    private sealed class UserSettings
    {
        public string? LanguageCode { get; set; }

        public string? ThemeCode { get; set; }
    }
}
