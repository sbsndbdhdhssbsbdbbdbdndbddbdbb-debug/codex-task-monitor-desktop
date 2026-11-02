using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace CodexTaskMonitor.Core;

public sealed partial class ThreadCatalogReader
{
    private readonly MonitorPaths _paths;

    public ThreadCatalogReader(MonitorPaths paths)
    {
        _paths = paths;
    }

    public async Task<IReadOnlyList<CodexThreadMetadata>> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        var database = _paths.FindLatestStateDatabase();
        if (database is not null)
        {
            try
            {
                var rows = await ReadFromDatabaseAsync(database, cancellationToken);
                if (rows.Count > 0)
                {
                    return await MergeRecentSessionMetadataAsync(rows, cancellationToken);
                }
            }
            catch (SqliteException)
            {
                // JSONL discovery remains available when Codex changes or locks its schema.
            }
        }

        return await ReadFromSessionsAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<CodexThreadMetadata>> ReadFromDatabaseAsync(
        string database,
        CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = database,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        };
        await using var connection = new SqliteConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, rollout_path, title, cwd, created_at_ms, updated_at_ms,
                   created_at, updated_at, archived, source, thread_source,
                   agent_nickname, agent_role
            FROM threads
            ORDER BY recency_at_ms DESC
            LIMIT 500
            """;

        var result = new List<CodexThreadMetadata>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var source = reader.IsDBNull(9) ? string.Empty : reader.GetString(9);
            var threadSource = reader.IsDBNull(10) ? string.Empty : reader.GetString(10);
            var isSubagent = threadSource.Equals("subagent", StringComparison.OrdinalIgnoreCase)
                             || source.Contains("subagent", StringComparison.OrdinalIgnoreCase);
            var created = ReadEpoch(reader, 4, 6);
            var updated = ReadEpoch(reader, 5, 7);
            result.Add(new CodexThreadMetadata(
                reader.GetString(0),
                NormalizePath(reader.GetString(1)),
                reader.GetString(2),
                NormalizePath(reader.GetString(3)),
                created,
                updated,
                reader.GetInt64(8) != 0,
                isSubagent,
                AgentNickname: reader.IsDBNull(11) ? null : reader.GetString(11),
                AgentRole: reader.IsDBNull(12) ? null : reader.GetString(12)));
        }

        await HydrateMissingSessionMetadataAsync(result, cancellationToken);

        for (var index = 0; index < result.Count; index++)
        {
            var item = result[index];
            if (!item.IsSubagent
                || item.ParentThreadId is not null
                || !File.Exists(item.RolloutPath))
            {
                continue;
            }

            var header = await ReadSessionMetaAsync(item.RolloutPath, cancellationToken);
            if (header is not null)
            {
                result[index] = item with
                {
                    ParentThreadId = header.ParentThreadId,
                    AgentNickname = header.AgentNickname ?? item.AgentNickname,
                    AgentRole = header.AgentRole ?? item.AgentRole,
                };
            }
        }

        return result;
    }

    private async Task HydrateMissingSessionMetadataAsync(
        List<CodexThreadMetadata> metadata,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_paths.SessionsRoot))
        {
            return;
        }

        var missing = metadata
            .Select((item, index) => (item, index))
            .Where(value => string.IsNullOrWhiteSpace(value.item.RolloutPath))
            .ToDictionary(value => value.item.ThreadId, value => value.index, StringComparer.Ordinal);
        if (missing.Count == 0)
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(
                     _paths.SessionsRoot,
                     "*.jsonl",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var match = ThreadIdInFileName().Match(Path.GetFileNameWithoutExtension(path));
            if (!match.Success || !missing.Remove(match.Value, out var index))
            {
                continue;
            }

            var header = await ReadSessionHeaderAsync(path, cancellationToken);
            if (header is null)
            {
                continue;
            }

            var item = metadata[index];
            metadata[index] = item with
            {
                RolloutPath = header.RolloutPath,
                Cwd = string.IsNullOrWhiteSpace(item.Cwd) ? header.Cwd : item.Cwd,
                Title = string.IsNullOrWhiteSpace(item.Title) ? header.Title : item.Title,
                IsSubagent = item.IsSubagent || header.IsSubagent,
                ParentThreadId = header.ParentThreadId ?? item.ParentThreadId,
                AgentNickname = header.AgentNickname ?? item.AgentNickname,
                AgentRole = header.AgentRole ?? item.AgentRole,
            };

            if (missing.Count == 0)
            {
                return;
            }
        }
    }

    private async Task<IReadOnlyList<CodexThreadMetadata>> ReadFromSessionsAsync(
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_paths.SessionsRoot))
        {
            return Array.Empty<CodexThreadMetadata>();
        }

        var result = new List<CodexThreadMetadata>();
        foreach (var path in Directory.EnumerateFiles(
                     _paths.SessionsRoot,
                     "*.jsonl",
                     SearchOption.AllDirectories)
                 .OrderByDescending(File.GetLastWriteTimeUtc)
                 .Take(500))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = await ReadSessionHeaderAsync(path, cancellationToken);
            if (metadata is not null)
            {
                result.Add(metadata);
            }
        }

        return result;
    }

    private async Task<IReadOnlyList<CodexThreadMetadata>> MergeRecentSessionMetadataAsync(
        IReadOnlyList<CodexThreadMetadata> databaseRows,
        CancellationToken cancellationToken)
    {
        // A resumed older task can be absent from Codex's recency-limited database
        // result while its JSONL file is actively receiving new events.
        var merged = databaseRows.ToDictionary(item => item.ThreadId, StringComparer.Ordinal);
        foreach (var session in await ReadFromSessionsAsync(cancellationToken))
        {
            if (!merged.TryGetValue(session.ThreadId, out var existing))
            {
                merged[session.ThreadId] = session;
                continue;
            }

            merged[session.ThreadId] = existing with
            {
                RolloutPath = string.IsNullOrWhiteSpace(existing.RolloutPath)
                    ? session.RolloutPath
                    : existing.RolloutPath,
                Cwd = string.IsNullOrWhiteSpace(existing.Cwd) ? session.Cwd : existing.Cwd,
                Title = string.IsNullOrWhiteSpace(existing.Title) ? session.Title : existing.Title,
                IsSubagent = existing.IsSubagent || session.IsSubagent,
                ParentThreadId = existing.ParentThreadId ?? session.ParentThreadId,
                AgentNickname = existing.AgentNickname ?? session.AgentNickname,
                AgentRole = existing.AgentRole ?? session.AgentRole,
            };
        }

        return merged.Values
            .OrderByDescending(item => item.UpdatedAt)
            .Take(500)
            .ToArray();
    }

    private static async Task<CodexThreadMetadata?> ReadSessionHeaderAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            8192,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream);
        string? threadId = null;
        string? cwd = null;
        var title = "未命名任务";
        var isSubagent = false;
        string? parentThreadId = null;
        string? nickname = null;
        string? role = null;

        for (var i = 0; i < 80 && !reader.EndOfStream; i++)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            var parsed = SessionEventParser.Parse(line);
            if (parsed?.Type == "session_meta")
            {
                threadId = parsed.ThreadId;
                cwd = parsed.Cwd;
                isSubagent = parsed.IsSubagent ?? false;
                parentThreadId = parsed.ParentThreadId;
                nickname = parsed.AgentNickname;
                role = parsed.AgentRole;
            }
            else if (parsed?.Type == "user_message" && !string.IsNullOrWhiteSpace(parsed.Message))
            {
                title = parsed.Message;
            }

            if (threadId is not null && title != "未命名任务")
            {
                break;
            }
        }

        if (threadId is null)
        {
            return null;
        }

        var file = new FileInfo(path);
        return new CodexThreadMetadata(
            threadId,
            path,
            title,
            cwd ?? string.Empty,
            file.CreationTimeUtc,
            file.LastWriteTimeUtc,
            false,
            isSubagent,
            parentThreadId,
            nickname,
            role);
    }

    private static async Task<ParsedSessionEvent?> ReadSessionMetaAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream);
        for (var index = 0; index < 5 && !reader.EndOfStream; index++)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                return null;
            }

            var parsed = SessionEventParser.Parse(line);
            if (parsed?.Type == "session_meta")
            {
                return parsed;
            }
        }

        return null;
    }

    private static DateTimeOffset ReadEpoch(SqliteDataReader reader, int millisecondsIndex, int secondsIndex)
    {
        if (!reader.IsDBNull(millisecondsIndex))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(millisecondsIndex));
        }

        return DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(secondsIndex));
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.GetFullPath(path);
    }

    [GeneratedRegex("[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$", RegexOptions.IgnoreCase)]
    private static partial Regex ThreadIdInFileName();
}
