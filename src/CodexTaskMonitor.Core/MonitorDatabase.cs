using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace CodexTaskMonitor.Core;

public sealed class MonitorDatabase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MonitorDatabase(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        }.ConnectionString;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            var command = connection.CreateCommand();
            command.CommandText =
                """
                PRAGMA journal_mode = WAL;
                CREATE TABLE IF NOT EXISTS task_snapshots (
                    thread_id TEXT PRIMARY KEY,
                    payload_json TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS file_cursors (
                    rollout_path TEXT PRIMARY KEY,
                    position INTEGER NOT NULL,
                    file_length INTEGER NOT NULL,
                    updated_at TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS notification_events (
                    event_key TEXT PRIMARY KEY,
                    created_at TEXT NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, CodexTaskSnapshot>> LoadSnapshotsAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            var command = connection.CreateCommand();
            command.CommandText = "SELECT thread_id, payload_json FROM task_snapshots";
            var result = new Dictionary<string, CodexTaskSnapshot>(StringComparer.Ordinal);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                try
                {
                    var snapshot = JsonSerializer.Deserialize<CodexTaskSnapshot>(reader.GetString(1), JsonOptions);
                    if (snapshot is not null)
                    {
                        result[reader.GetString(0)] = snapshot;
                    }
                }
                catch (JsonException)
                {
                    // Ignore one damaged row instead of losing the remaining state.
                }
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveSnapshotAsync(
        CodexTaskSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO task_snapshots(thread_id, payload_json, updated_at)
                VALUES ($threadId, $payload, $updatedAt)
                ON CONFLICT(thread_id) DO UPDATE SET
                    payload_json = excluded.payload_json,
                    updated_at = excluded.updated_at
                """;
            command.Parameters.AddWithValue("$threadId", snapshot.ThreadId);
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(snapshot, JsonOptions));
            command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<long?> LoadCursorAsync(
        string rolloutPath,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            var command = connection.CreateCommand();
            command.CommandText = "SELECT position FROM file_cursors WHERE rollout_path = $path";
            command.Parameters.AddWithValue("$path", rolloutPath);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value is long position ? position : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveCursorAsync(
        string rolloutPath,
        long position,
        long fileLength,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO file_cursors(rollout_path, position, file_length, updated_at)
                VALUES ($path, $position, $length, $updatedAt)
                ON CONFLICT(rollout_path) DO UPDATE SET
                    position = excluded.position,
                    file_length = excluded.file_length,
                    updated_at = excluded.updated_at
                """;
            command.Parameters.AddWithValue("$path", rolloutPath);
            command.Parameters.AddWithValue("$position", position);
            command.Parameters.AddWithValue("$length", fileLength);
            command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> TryMarkNotificationAsync(
        string eventKey,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT OR IGNORE INTO notification_events(event_key, created_at)
                VALUES ($key, $createdAt);
                SELECT changes();
                """;
            command.Parameters.AddWithValue("$key", eventKey);
            command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt64(result) > 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
