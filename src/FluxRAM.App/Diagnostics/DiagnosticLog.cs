using System.IO;
using System.Text;
using FluxRAM.App.Licensing;

namespace FluxRAM.App.Diagnostics;

public static class DiagnosticLog
{
    private const long MaxLogBytes = 1024 * 1024;
    private static readonly object Gate = new();

    public static string LogFilePath => AppDataPaths.GetDiagnosticLogPath();

    public static void Info(string message)
    {
        Write("INFO", message, null);
    }

    public static void Warning(string message, Exception? exception = null)
    {
        Write("WARN", message, exception);
    }

    public static void Error(string message, Exception? exception = null)
    {
        Write("ERROR", message, exception);
    }

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (Gate)
            {
                var path = LogFilePath;
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                RotateIfNeeded(path);
                var line = $"{DateTimeOffset.Now:O} [{level}] {message}";
                if (exception is not null)
                {
                    line += Environment.NewLine + exception;
                }

                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
        }
    }

    private static void RotateIfNeeded(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var info = new FileInfo(path);
        if (info.Length <= MaxLogBytes)
        {
            return;
        }

        var archivePath = Path.ChangeExtension(path, ".1.log");
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        File.Move(path, archivePath);
    }
}
