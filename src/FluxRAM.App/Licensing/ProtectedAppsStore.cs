using System.IO;
using System.Text;

namespace FluxRAM.App.Licensing;

public sealed class ProtectedAppsStore
{
    private readonly string _path;

    public ProtectedAppsStore()
        : this(AppDataPaths.GetProtectedAppsPath())
    {
    }

    public ProtectedAppsStore(string path)
    {
        _path = path;
    }

    public IReadOnlyList<string> Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return Array.Empty<string>();
            }

            return File.ReadAllLines(_path, Encoding.UTF8)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public void Save(IReadOnlyCollection<string> protectedPaths)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var lines = protectedPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        File.WriteAllLines(_path, lines, Encoding.UTF8);
    }
}
