using System.IO;

namespace CodexTaskMonitor.App;

public static class AppLog
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _directory;

    public static void Initialize(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        foreach (var file in Directory.EnumerateFiles(directory, "monitor-*.log"))
        {
            if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7))
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // A previous process can briefly retain yesterday's file.
                }
            }
        }
    }

    public static async Task WriteAsync(string level, string message, Exception? exception = null)
    {
        if (_directory is null)
        {
            return;
        }

        await Gate.WaitAsync();
        try
        {
            var path = Path.Combine(_directory, $"monitor-{DateTime.UtcNow:yyyyMMdd}.log");
            var line = $"{DateTimeOffset.Now:O} [{level}] {message}";
            if (exception is not null)
            {
                line += $"{Environment.NewLine}{exception}";
            }

            await File.AppendAllTextAsync(path, line + Environment.NewLine);
        }
        catch
        {
            // Logging must never terminate the monitor.
        }
        finally
        {
            Gate.Release();
        }
    }
}
