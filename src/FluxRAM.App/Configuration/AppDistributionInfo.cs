using System.Reflection;

namespace FluxRAM.App.Configuration;

public enum AppDistributionMode
{
    Lite,
    Portable
}

public static class AppDistributionInfo
{
    private const string DistributionModeMetadataKey = "FluxRAMDistributionMode";

    public static AppDistributionMode CurrentMode
    {
        get
        {
            var value = typeof(AppDistributionInfo).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute =>
                    string.Equals(attribute.Key, DistributionModeMetadataKey, StringComparison.Ordinal))
                ?.Value;
            return ParseMode(value);
        }
    }

    public static AppDistributionMode ParseMode(string? value)
    {
        return string.Equals(value, "Portable", StringComparison.OrdinalIgnoreCase)
            ? AppDistributionMode.Portable
            : AppDistributionMode.Lite;
    }

    public static AppUpdateAsset? SelectAsset(
        IReadOnlyList<AppUpdateAsset> assets,
        AppDistributionMode mode)
    {
        var expectedName = mode == AppDistributionMode.Portable
            ? "FluxRAM-Portable-Windows-x64.zip"
            : "FluxRAM-Lite-Windows-x64.zip";
        var exactMatch = assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, expectedName, StringComparison.OrdinalIgnoreCase));
        if (exactMatch is not null)
        {
            return exactMatch;
        }

        var packageSuffix = mode == AppDistributionMode.Portable
            ? "-Portable-Windows-x64.zip"
            : "-Lite-Windows-x64.zip";
        return assets.FirstOrDefault(asset =>
            asset.Name.StartsWith("FluxRAM-", StringComparison.OrdinalIgnoreCase) &&
            asset.Name.EndsWith(packageSuffix, StringComparison.OrdinalIgnoreCase));
    }
}
