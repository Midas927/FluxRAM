using System.IO;
using System.Text;

namespace FluxRAM.App.Licensing;

public enum LicenseActivationReadState
{
    Missing,
    Success,
    Error
}

public sealed record LicenseActivationReadResult(
    LicenseActivationReadState State,
    string? LicenseKey);

public sealed class LicenseActivationStore
{
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);
    private readonly string _licenseKeyPath;

    public LicenseActivationStore()
        : this(AppDataPaths.GetLicenseKeyPath())
    {
    }

    public LicenseActivationStore(string licenseKeyPath)
    {
        _licenseKeyPath = licenseKeyPath;
    }

    public LicenseActivationReadResult Read()
    {
        try
        {
            if (Directory.Exists(_licenseKeyPath))
            {
                return new LicenseActivationReadResult(LicenseActivationReadState.Error, null);
            }

            if (!File.Exists(_licenseKeyPath))
            {
                return new LicenseActivationReadResult(LicenseActivationReadState.Missing, null);
            }

            var licenseKey = File.ReadAllText(_licenseKeyPath, Encoding.UTF8).Trim();
            return licenseKey.Length == 0
                ? new LicenseActivationReadResult(LicenseActivationReadState.Error, null)
                : new LicenseActivationReadResult(LicenseActivationReadState.Success, licenseKey);
        }
        catch
        {
            return new LicenseActivationReadResult(LicenseActivationReadState.Error, null);
        }
    }

    public string? Load()
    {
        var result = Read();
        return result.State == LicenseActivationReadState.Success ? result.LicenseKey : null;
    }

    public bool Save(string licenseKey)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            return false;
        }

        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(_licenseKeyPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            temporaryPath = _licenseKeyPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporaryPath, licenseKey.Trim(), Utf8WithoutBom);
            File.Move(temporaryPath, _licenseKeyPath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                }
            }
        }
    }
}
