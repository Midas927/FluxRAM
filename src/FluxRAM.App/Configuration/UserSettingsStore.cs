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
        try
        {
            if (!File.Exists(_path))
            {
                return UiLanguage.English;
            }

            var settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(_path));
            return UiLanguageCatalog.FromCode(settings?.LanguageCode);
        }
        catch
        {
            return UiLanguage.English;
        }
    }

    public void SaveLanguage(UiLanguage language)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var settings = new UserSettings(UiLanguageCatalog.ToCode(language));
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private sealed record UserSettings(string LanguageCode);
}
