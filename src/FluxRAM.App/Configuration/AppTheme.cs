namespace FluxRAM.App.Configuration;

public enum AppTheme
{
    Dark = 0,
    Light = 1
}

public static class AppThemeCatalog
{
    public static AppTheme FromCode(string? code)
    {
        return code?.Trim().ToLowerInvariant() switch
        {
            "light" => AppTheme.Light,
            _ => AppTheme.Dark
        };
    }

    public static string ToCode(AppTheme theme)
    {
        return theme == AppTheme.Light ? "light" : "dark";
    }
}
