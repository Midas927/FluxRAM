using System.IO;
using System.Text;

namespace FluxRAM.App.Licensing;

public sealed class LicenseActivationStore
{
    private readonly string _licenseKeyPath;

    public LicenseActivationStore()
        : this(AppDataPaths.GetLicenseKeyPath())
    {
    }

    public LicenseActivationStore(string licenseKeyPath)
    {
        _licenseKeyPath = licenseKeyPath;
    }

    public string? Load()
    {
        try
        {
            if (!File.Exists(_licenseKeyPath))
            {
                return null;
            }

            var licenseKey = File.ReadAllText(_licenseKeyPath, Encoding.UTF8).Trim();
            return licenseKey.Length == 0 ? null : licenseKey;
        }
        catch
        {
            return null;
        }
    }

    public void Save(string licenseKey)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            return;
        }

        var directory = Path.GetDirectoryName(_licenseKeyPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_licenseKeyPath, licenseKey.Trim(), Encoding.UTF8);
    }
}
