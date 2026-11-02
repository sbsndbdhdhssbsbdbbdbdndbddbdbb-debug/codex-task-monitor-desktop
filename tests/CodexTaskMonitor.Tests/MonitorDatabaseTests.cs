using CodexTaskMonitor.Core;

namespace CodexTaskMonitor.Tests;

public sealed class MonitorDatabaseTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "CodexTaskMonitor.Tests",
        Guid.NewGuid().ToString("N"));

    public MonitorDatabaseTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task PersistsSnapshotCursorAndNotificationDedupe()
    {
        var database = new MonitorDatabase(Path.Combine(_directory, "monitor.db"));
        await database.InitializeAsync();
        var snapshot = new CodexTaskSnapshot
        {
            ThreadId = "thread-1",
            TurnId = "turn-1",
            Title = "Test",
            Project = "Project",
            Cwd = _directory,
            RolloutPath = Path.Combine(_directory, "session.jsonl"),
            Status = CodexTaskStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            LastActivityAt = DateTimeOffset.UtcNow,
        };

        await database.SaveSnapshotAsync(snapshot);
        await database.SaveCursorAsync(snapshot.RolloutPath, 42, 42);
        var firstNotification = await database.TryMarkNotificationAsync("event-1");
        var duplicateNotification = await database.TryMarkNotificationAsync("event-1");

        var restored = await database.LoadSnapshotsAsync();
        Assert.Equal(CodexTaskStatus.Completed, restored["thread-1"].Status);
        Assert.Equal(42, await database.LoadCursorAsync(snapshot.RolloutPath));
        Assert.True(firstNotification);
        Assert.False(duplicateNotification);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }
}
