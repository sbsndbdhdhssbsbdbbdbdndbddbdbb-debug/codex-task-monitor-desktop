using System.Text;

namespace CodexTaskMonitor.Core;

public sealed record JsonlReadResult(
    IReadOnlyList<string> Lines,
    long Position,
    bool WasTruncated);

public static class IncrementalJsonlReader
{
    public static async Task<JsonlReadResult> ReadAvailableAsync(
        string path,
        long position,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var wasTruncated = position > stream.Length;
        if (wasTruncated)
        {
            position = 0;
        }

        if (position == stream.Length)
        {
            return new JsonlReadResult(Array.Empty<string>(), position, wasTruncated);
        }

        stream.Position = Math.Max(0, position);
        var remaining = checked((int)Math.Min(stream.Length - stream.Position, 16 * 1024 * 1024));
        var buffer = new byte[remaining];
        var bytesRead = 0;
        while (bytesRead < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(bytesRead, buffer.Length - bytesRead),
                cancellationToken);
            if (read == 0)
            {
                break;
            }

            bytesRead += read;
        }

        if (bytesRead == 0)
        {
            return new JsonlReadResult(Array.Empty<string>(), position, wasTruncated);
        }

        var lastNewline = Array.LastIndexOf(buffer, (byte)'\n', bytesRead - 1, bytesRead);
        if (lastNewline < 0)
        {
            return new JsonlReadResult(Array.Empty<string>(), position, wasTruncated);
        }

        var text = Encoding.UTF8.GetString(buffer, 0, lastNewline + 1);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0)
            .ToArray();
        return new JsonlReadResult(lines, position + lastNewline + 1, wasTruncated);
    }

    public static async Task<long> FindSeedPositionAsync(
        string path,
        int maxSeedBytes = 512 * 1024,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length <= maxSeedBytes)
        {
            return 0;
        }

        var start = stream.Length - maxSeedBytes;
        stream.Position = start;
        var buffer = new byte[Math.Min(4096, maxSeedBytes)];
        var read = await stream.ReadAsync(buffer, cancellationToken);
        var newline = Array.IndexOf(buffer, (byte)'\n', 0, read);
        return newline < 0 ? start : start + newline + 1;
    }
}
