using System.Text;
using CodexTaskMonitor.Core;

namespace CodexTaskMonitor.Tests;

public sealed class IncrementalJsonlReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "CodexTaskMonitor.Tests",
        Guid.NewGuid().ToString("N"));

    public IncrementalJsonlReaderTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task LeavesPartialLineForNextRead()
    {
        var path = Path.Combine(_directory, "session.jsonl");
        await File.WriteAllTextAsync(path, "{\"n\":1}\n{\"n\":");

        var first = await IncrementalJsonlReader.ReadAvailableAsync(path, 0);
        await File.AppendAllTextAsync(path, "2}\n", Encoding.UTF8);
        var second = await IncrementalJsonlReader.ReadAvailableAsync(path, first.Position);

        Assert.Equal(new[] { """{"n":1}""" }, first.Lines);
        Assert.Equal(new[] { """{"n":2}""" }, second.Lines);
        Assert.Equal(new FileInfo(path).Length, second.Position);
    }

    [Fact]
    public async Task RestartsAtZeroAfterFileTruncation()
    {
        var path = Path.Combine(_directory, "session.jsonl");
        await File.WriteAllTextAsync(path, "{\"old\":true}\n");
        var oldLength = new FileInfo(path).Length;
        await File.WriteAllTextAsync(path, "{}\n");

        var result = await IncrementalJsonlReader.ReadAvailableAsync(path, oldLength);

        Assert.True(result.WasTruncated);
        Assert.Equal(new[] { "{}" }, result.Lines);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }
}
