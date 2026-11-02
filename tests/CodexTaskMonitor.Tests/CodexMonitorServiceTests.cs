using CodexTaskMonitor.Core;
using Microsoft.Data.Sqlite;

namespace CodexTaskMonitor.Tests;

public sealed class CodexMonitorServiceTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CodexTaskMonitor.Tests",
        Guid.NewGuid().ToString("N"));

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        Directory.Delete(_root, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task DetectsCompletedPartialLineAndDoesNotReplayAfterRestart()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.SessionsRoot);
        Directory.CreateDirectory(paths.AppDataRoot);
        var rollout = Path.Combine(paths.SessionsRoot, "session.jsonl");
        var startedAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await File.WriteAllTextAsync(
            rollout,
            Line("session_meta", new
            {
                id = "thread-1",
                cwd = _root,
                source = "appServer",
            }, startedAt)
            + Line("event_msg", new
            {
                type = "task_started",
                turn_id = "turn-1",
            }, startedAt));
        await CreateStateDatabaseAsync(paths, rollout, startedAt);

        var settings = new MonitorSettings
        {
            PollIntervalMilliseconds = 100,
            ReconcileIntervalMilliseconds = 200,
        };
        var database = new MonitorDatabase(paths.MonitorDatabase);
        var monitor = new CodexMonitorService(paths, database, settings);
        var completed = new TaskCompletionSource<MonitorEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.TaskEvent += (_, monitorEvent) =>
        {
            if (monitorEvent.Kind == MonitorEventKind.Completed)
            {
                completed.TrySetResult(monitorEvent);
            }
        };
        await monitor.StartAsync();
        Assert.Equal(CodexTaskStatus.Running, monitor.GetTasks().Single().Status);

        var completion = Line("event_msg", new
        {
            type = "task_complete",
            turn_id = "turn-1",
            last_agent_message = "done",
        }, DateTimeOffset.UtcNow);
        var split = completion.Length / 2;
        await File.AppendAllTextAsync(rollout, completion[..split]);
        await Task.Delay(350);
        Assert.False(completed.Task.IsCompleted);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await File.AppendAllTextAsync(rollout, completion[split..]);
        var result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        stopwatch.Stop();

        Assert.Equal("thread-1", result.Task.ThreadId);
        Assert.Equal(CodexTaskStatus.Completed, result.Task.Status);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
        await monitor.DisposeAsync();

        var restarted = new CodexMonitorService(
            paths,
            new MonitorDatabase(paths.MonitorDatabase),
            settings);
        var replayed = false;
        restarted.TaskEvent += (_, monitorEvent) =>
        {
            if (monitorEvent.Kind == MonitorEventKind.Completed)
            {
                replayed = true;
            }
        };
        await restarted.StartAsync();
        await Task.Delay(500);

        Assert.False(replayed);
        Assert.Equal(CodexTaskStatus.Completed, restarted.GetTasks().Single().Status);
        await restarted.DisposeAsync();
    }

    [Fact]
    public async Task ReseedsStaleTaskWhenItsSessionBecomesActiveAgain()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.SessionsRoot);
        Directory.CreateDirectory(paths.AppDataRoot);
        var rollout = Path.Combine(paths.SessionsRoot, "resumed.jsonl");
        var originalTime = DateTimeOffset.UtcNow.AddHours(-1);
        await File.WriteAllTextAsync(
            rollout,
            Line("session_meta", new { id = "thread-resumed", cwd = _root, source = "appServer" }, originalTime)
            + Line("event_msg", new { type = "task_started", turn_id = "turn-old" }, originalTime));
        await CreateStateDatabaseAsync(paths, rollout, originalTime, "thread-resumed");

        var monitorDatabase = new MonitorDatabase(paths.MonitorDatabase);
        await monitorDatabase.InitializeAsync();
        await monitorDatabase.SaveSnapshotAsync(new CodexTaskSnapshot
        {
            ThreadId = "thread-resumed",
            TurnId = "turn-old",
            Title = "Resumed task",
            Project = "test",
            Cwd = _root,
            RolloutPath = rollout,
            Status = CodexTaskStatus.Stale,
            CreatedAt = originalTime,
            UpdatedAt = originalTime.AddMinutes(15),
            StartedAt = originalTime,
            LastActivityAt = originalTime.AddMinutes(5),
        });
        await monitorDatabase.SaveCursorAsync(rollout, new FileInfo(rollout).Length, new FileInfo(rollout).Length);
        await File.AppendAllTextAsync(rollout, Line("event_msg", new { type = "agent_reasoning" }, DateTimeOffset.UtcNow));

        await using var monitor = new CodexMonitorService(
            paths,
            new MonitorDatabase(paths.MonitorDatabase),
            new MonitorSettings { PollIntervalMilliseconds = 60_000, ReconcileIntervalMilliseconds = 60_000 });
        await monitor.StartAsync();

        var task = Assert.Single(monitor.GetTasks());
        Assert.Equal(CodexTaskStatus.Running, task.Status);
        Assert.True(task.LastActivityAt > originalTime);
    }

    private MonitorPaths CreatePaths()
    {
        var codex = Path.Combine(_root, ".codex");
        var appData = Path.Combine(_root, "appdata");
        return new MonitorPaths(
            codex,
            Path.Combine(codex, "sessions"),
            appData,
            Path.Combine(appData, "monitor.db"),
            Path.Combine(appData, "settings.json"),
            Path.Combine(appData, "integration.json"),
            Path.Combine(appData, "state.json"),
            Path.Combine(appData, "logs"));
    }

    private static string Line(string type, object payload, DateTimeOffset timestamp)
    {
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            timestamp,
            type,
            payload,
        }) + "\n";
    }

    private static async Task CreateStateDatabaseAsync(
        MonitorPaths paths,
        string rollout,
        DateTimeOffset timestamp,
        string threadId = "thread-1")
    {
        Directory.CreateDirectory(paths.CodexHome);
        var databasePath = Path.Combine(paths.CodexHome, "state_1.sqlite");
        await using var connection = new SqliteConnection(
            $"Data Source={databasePath};Mode=ReadWriteCreate;Pooling=False");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE threads (
                id TEXT PRIMARY KEY,
                rollout_path TEXT,
                title TEXT,
                cwd TEXT,
                created_at_ms INTEGER,
                updated_at_ms INTEGER,
                created_at INTEGER,
                updated_at INTEGER,
                archived INTEGER,
                source TEXT,
                thread_source TEXT,
                agent_nickname TEXT,
                agent_role TEXT,
                recency_at_ms INTEGER
            );
            INSERT INTO threads VALUES (
                $threadId, $rollout, 'Integration task', $cwd,
                $milliseconds, $milliseconds, $seconds, $seconds,
                0, 'appServer', 'root', NULL, NULL, $milliseconds
            );
            """;
        command.Parameters.AddWithValue("$rollout", rollout);
        command.Parameters.AddWithValue("$threadId", threadId);
        command.Parameters.AddWithValue("$cwd", paths.CodexHome);
        command.Parameters.AddWithValue("$milliseconds", timestamp.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$seconds", timestamp.ToUnixTimeSeconds());
        await command.ExecuteNonQueryAsync();
    }
}
