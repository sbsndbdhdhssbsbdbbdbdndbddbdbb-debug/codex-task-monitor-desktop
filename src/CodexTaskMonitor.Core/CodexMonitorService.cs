using System.Collections.Concurrent;

namespace CodexTaskMonitor.Core;

public sealed class CodexMonitorService : IAsyncDisposable
{
    private readonly MonitorPaths _paths;
    private readonly MonitorDatabase _database;
    private readonly ThreadCatalogReader _catalog;
    private readonly CodexLogFailureReader _logFailures;
    private readonly MonitorSettings _settings;
    private readonly ConcurrentDictionary<string, TrackedSession> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _wakeSignal = new(0, 1);
    private readonly CancellationTokenSource _stop = new();
    private FileSystemWatcher? _watcher;
    private Task? _monitorLoop;
    private DateTimeOffset _nextCatalogRefresh = DateTimeOffset.MinValue;
    private DateTimeOffset _nextLogRefresh = DateTimeOffset.MinValue;
    private IReadOnlyDictionary<string, CodexTaskSnapshot> _restored =
        new Dictionary<string, CodexTaskSnapshot>();

    public CodexMonitorService(
        MonitorPaths paths,
        MonitorDatabase database,
        MonitorSettings settings)
    {
        _paths = paths;
        _database = database;
        _catalog = new ThreadCatalogReader(paths);
        _logFailures = new CodexLogFailureReader(paths);
        _settings = settings;
    }

    public event EventHandler<MonitorEvent>? TaskEvent;

    public bool IsPaused { get; private set; }
    public DateTimeOffset? LastSuccessfulRefreshAt { get; private set; }
    public string? LastRefreshError { get; private set; }

    public IReadOnlyList<CodexTaskSnapshot> GetTasks()
    {
        return _sessions.Values
            .Select(session => session.StateMachine.Snapshot)
            .OrderBy(snapshot => StatusOrder(snapshot.Status))
            .ThenByDescending(snapshot => snapshot.LastActivityAt)
            .ToArray();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        _restored = await _database.LoadSnapshotsAsync(cancellationToken);
        await RefreshCatalogAsync(seedOnly: true, cancellationToken);
        ConfigureWatcher();
        _monitorLoop = RunLoopAsync(_stop.Token);
    }

    public void SetPaused(bool paused)
    {
        IsPaused = paused;
        Wake();
    }

    public async Task RefreshNowAsync(CancellationToken cancellationToken = default)
    {
        await ReconcileAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _watcher?.Dispose();
        Wake();
        if (_monitorLoop is not null)
        {
            try
            {
                await _monitorLoop;
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }

        _stop.Dispose();
        _wakeSignal.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!IsPaused)
                {
                    await ReconcileAsync(cancellationToken);
                }
            }
            catch (IOException exception)
            {
                // Codex can rotate or hold a session file while a turn is being written.
                LastRefreshError = exception.Message;
            }
            catch (UnauthorizedAccessException exception)
            {
                // Retry on the next reconciliation rather than terminating the tray app.
                LastRefreshError = exception.Message;
            }

            var awakened = await _wakeSignal.WaitAsync(
                TimeSpan.FromMilliseconds(_settings.PollIntervalMilliseconds),
                cancellationToken);
            if (awakened)
            {
                await Task.Delay(
                    Math.Min(500, _settings.PollIntervalMilliseconds),
                    cancellationToken);
            }
        }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow >= _nextCatalogRefresh)
        {
            await RefreshCatalogAsync(seedOnly: false, cancellationToken);
        }

        foreach (var session in _sessions.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(session.Metadata.RolloutPath))
            {
                continue;
            }

            await PollSessionAsync(session, suppressEvents: false, cancellationToken);
            var stale = session.StateMachine.MarkStale(
                DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(_settings.StaleAfterMinutes));
            if (stale.Changed)
            {
                await PersistAndPublishAsync(session, stale, cancellationToken);
            }
        }

        if (DateTimeOffset.UtcNow < _nextLogRefresh)
        {
            LastSuccessfulRefreshAt = DateTimeOffset.UtcNow;
            LastRefreshError = null;
            return;
        }

        _nextLogRefresh = DateTimeOffset.UtcNow.AddSeconds(10);
        foreach (var failure in await _logFailures.ReadNewAsync(cancellationToken))
        {
            if (!_sessions.TryGetValue(failure.ThreadId, out var failedSession)
                || failedSession.StateMachine.Snapshot.Status != CodexTaskStatus.Running)
            {
                continue;
            }

            var transition = failedSession.StateMachine.Apply(new ParsedSessionEvent(
                "fatal_error",
                failure.Timestamp,
                Message: failure.Message));
            await PersistAndPublishAsync(failedSession, transition, cancellationToken);
        }

        LastSuccessfulRefreshAt = DateTimeOffset.UtcNow;
        LastRefreshError = null;
    }

    private async Task RefreshCatalogAsync(bool seedOnly, CancellationToken cancellationToken)
    {
        var metadataRows = await _catalog.ReadAsync(cancellationToken);
        foreach (var metadata in metadataRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_sessions.TryGetValue(metadata.ThreadId, out var existing))
            {
                existing.Metadata = metadata;
                existing.StateMachine.UpdateMetadata(metadata);
                continue;
            }

            _restored.TryGetValue(metadata.ThreadId, out var restored);
            var session = new TrackedSession(metadata, new TaskStateMachine(metadata, restored));
            if (!_sessions.TryAdd(metadata.ThreadId, session))
            {
                continue;
            }

            if (File.Exists(metadata.RolloutPath))
            {
                var persistedCursor = await _database.LoadCursorAsync(metadata.RolloutPath, cancellationToken);
                var shouldReseed = restored?.Status == CodexTaskStatus.Stale;
                if (!shouldReseed
                    && persistedCursor is not null
                    && persistedCursor.Value <= new FileInfo(metadata.RolloutPath).Length)
                {
                    session.Position = persistedCursor.Value;
                    await PollSessionAsync(session, suppressEvents: seedOnly, cancellationToken);
                }
                else
                {
                    session.Position = await IncrementalJsonlReader.FindSeedPositionAsync(
                        metadata.RolloutPath,
                        cancellationToken: cancellationToken);
                    await PollSessionAsync(session, suppressEvents: true, cancellationToken);
                }
            }

            await _database.SaveSnapshotAsync(session.StateMachine.Snapshot, cancellationToken);
        }

        _nextCatalogRefresh = DateTimeOffset.UtcNow.AddMilliseconds(
            _settings.ReconcileIntervalMilliseconds);
    }

    private async Task PollSessionAsync(
        TrackedSession session,
        bool suppressEvents,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(session.Metadata.RolloutPath);
        if (file.Length == session.LastKnownLength
            && file.LastWriteTimeUtc == session.LastWriteTimeUtc)
        {
            return;
        }

        var read = await IncrementalJsonlReader.ReadAvailableAsync(
            session.Metadata.RolloutPath,
            session.Position,
            cancellationToken);
        session.Position = read.Position;
        StateTransition? pendingProgress = null;
        foreach (var line in read.Lines)
        {
            var parsed = SessionEventParser.Parse(line);
            if (parsed is null)
            {
                continue;
            }

            var transition = session.StateMachine.Apply(parsed);
            if (!transition.Changed)
            {
                continue;
            }

            if (transition.EventKind is null or MonitorEventKind.Progress)
            {
                pendingProgress = transition;
                continue;
            }

            if (suppressEvents)
            {
                await _database.SaveSnapshotAsync(transition.Snapshot, cancellationToken);
            }
            else
            {
                await PersistAndPublishAsync(session, transition, cancellationToken);
            }

            pendingProgress = null;
        }

        if (pendingProgress is not null)
        {
            if (suppressEvents)
            {
                await _database.SaveSnapshotAsync(pendingProgress.Snapshot, cancellationToken);
            }
            else
            {
                await PersistAndPublishAsync(session, pendingProgress, cancellationToken);
            }
        }

        file.Refresh();
        var length = file.Length;
        session.LastKnownLength = length;
        session.LastWriteTimeUtc = file.LastWriteTimeUtc;
        await _database.SaveCursorAsync(
            session.Metadata.RolloutPath,
            session.Position,
            length,
            cancellationToken);
    }

    private async Task PersistAndPublishAsync(
        TrackedSession session,
        StateTransition transition,
        CancellationToken cancellationToken)
    {
        await _database.SaveSnapshotAsync(transition.Snapshot, cancellationToken);
        if (transition.EventKind is null)
        {
            return;
        }

        var kind = transition.EventKind.Value;
        if (kind is MonitorEventKind.Completed or MonitorEventKind.NeedsInput
            or MonitorEventKind.Aborted or MonitorEventKind.Failed or MonitorEventKind.Stale)
        {
            var eventKey =
                $"{transition.Snapshot.ThreadId}|{transition.Snapshot.TurnId}|{kind}|{transition.Snapshot.LastActivityAt:O}";
            if (!await _database.TryMarkNotificationAsync(eventKey, cancellationToken))
            {
                return;
            }
        }

        var monitorEvent = new MonitorEvent(
            1,
            Guid.NewGuid().ToString("N"),
            kind,
            DateTimeOffset.UtcNow,
            transition.Snapshot);
        TaskEvent?.Invoke(this, monitorEvent);
    }

    private void ConfigureWatcher()
    {
        if (!Directory.Exists(_paths.SessionsRoot))
        {
            return;
        }

        _watcher = new FileSystemWatcher(_paths.SessionsRoot, "*.jsonl")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += (_, _) => Wake();
        _watcher.Created += (_, _) => Wake();
        _watcher.Renamed += (_, _) => Wake();
        _watcher.Error += (_, _) => Wake();
    }

    private void Wake()
    {
        if (_wakeSignal.CurrentCount == 0)
        {
            try
            {
                _wakeSignal.Release();
            }
            catch (ObjectDisposedException)
            {
                // Shutdown raced with a final file-system event.
            }
        }
    }

    private static int StatusOrder(CodexTaskStatus status)
    {
        return status switch
        {
            CodexTaskStatus.NeedsInput => 0,
            CodexTaskStatus.Failed => 1,
            CodexTaskStatus.Running => 2,
            CodexTaskStatus.Stale => 3,
            CodexTaskStatus.Completed => 4,
            CodexTaskStatus.Aborted => 5,
            _ => 6,
        };
    }

    private sealed class TrackedSession
    {
        public TrackedSession(CodexThreadMetadata metadata, TaskStateMachine stateMachine)
        {
            Metadata = metadata;
            StateMachine = stateMachine;
        }

        public CodexThreadMetadata Metadata { get; set; }
        public TaskStateMachine StateMachine { get; }
        public long Position { get; set; }
        public long LastKnownLength { get; set; } = -1;
        public DateTime LastWriteTimeUtc { get; set; } = DateTime.MinValue;
    }
}
