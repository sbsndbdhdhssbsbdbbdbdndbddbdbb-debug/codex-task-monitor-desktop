using Microsoft.Data.Sqlite;

namespace CodexTaskMonitor.Core;

public sealed record CodexLogFailure(
    long Id,
    string ThreadId,
    DateTimeOffset Timestamp,
    string Message);

public sealed class CodexLogFailureReader
{
    private readonly MonitorPaths _paths;
    private string? _databasePath;
    private long _lastSeenId;
    private bool _initialized;

    public CodexLogFailureReader(MonitorPaths paths)
    {
        _paths = paths;
    }

    public async Task<IReadOnlyList<CodexLogFailure>> ReadNewAsync(
        CancellationToken cancellationToken = default)
    {
        var databasePath = _paths.FindLatestLogsDatabase();
        if (databasePath is null)
        {
            return Array.Empty<CodexLogFailure>();
        }

        if (!string.Equals(databasePath, _databasePath, StringComparison.OrdinalIgnoreCase))
        {
            _databasePath = databasePath;
            _lastSeenId = 0;
            _initialized = false;
        }

        try
        {
            await using var connection = await OpenAsync(databasePath, cancellationToken);
            if (!_initialized)
            {
                var seed = connection.CreateCommand();
                seed.CommandText = "SELECT COALESCE(MAX(id), 0) FROM logs";
                _lastSeenId = Convert.ToInt64(await seed.ExecuteScalarAsync(cancellationToken));
                _initialized = true;
                return Array.Empty<CodexLogFailure>();
            }

            var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id, ts, thread_id, feedback_log_body
                FROM logs
                WHERE id > $lastSeen
                  AND level = 'ERROR'
                  AND thread_id IS NOT NULL
                  AND feedback_log_body IS NOT NULL
                ORDER BY id
                LIMIT 1000
                """;
            command.Parameters.AddWithValue("$lastSeen", _lastSeenId);

            var failures = new List<CodexLogFailure>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetInt64(0);
                _lastSeenId = Math.Max(_lastSeenId, id);
                var message = reader.GetString(3);
                if (!IsTerminalFailure(message))
                {
                    continue;
                }

                failures.Add(new CodexLogFailure(
                    id,
                    reader.GetString(2),
                    DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(1)),
                    Condense(message)));
            }

            return failures;
        }
        catch (SqliteException)
        {
            return Array.Empty<CodexLogFailure>();
        }
    }

    public static bool IsTerminalFailure(string message)
    {
        return TerminalMarkers.Any(marker =>
            message.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<SqliteConnection> OpenAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static string Condense(string message)
    {
        var normalized = string.Join(" ", message.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 240 ? normalized : $"{normalized[..237]}...";
    }

    private static readonly string[] TerminalMarkers =
    {
        "turn failed",
        "failed to run turn",
        "error running turn",
        "stream error",
        "stream closed before completion",
        "stream disconnected before completion",
        "maximum retries exceeded",
        "fatal error",
    };
}
