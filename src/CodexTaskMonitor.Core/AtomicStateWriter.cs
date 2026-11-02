using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexTaskMonitor.Core;

public sealed class AtomicStateWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AtomicStateWriter(string path)
    {
        _path = path;
    }

    public async Task WriteAsync(
        IReadOnlyList<CodexTaskSnapshot> tasks,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tempPath = $"{_path}.{Environment.ProcessId}.tmp";
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, new
                {
                    version = 1,
                    updatedAt = DateTimeOffset.UtcNow,
                    tasks,
                }, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, _path, true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
