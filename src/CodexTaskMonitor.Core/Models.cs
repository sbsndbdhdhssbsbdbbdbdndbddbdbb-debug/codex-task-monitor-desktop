using System.Text.Json.Serialization;

namespace CodexTaskMonitor.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CodexTaskStatus
{
    Idle,
    Running,
    NeedsInput,
    Completed,
    Aborted,
    Failed,
    Stale,
}

/// <summary>
/// The three task groups shown to users. Detailed Codex states remain available
/// on each task for diagnostics and context.
/// </summary>
public enum TaskDisplayCategory
{
    InProgress,
    NeedsAttention,
    RecentlyCompleted,
    Hidden,
}

public static class CodexTaskStatusExtensions
{
    public static TaskDisplayCategory ToDisplayCategory(this CodexTaskStatus status) => status switch
    {
        CodexTaskStatus.Running => TaskDisplayCategory.InProgress,
        CodexTaskStatus.NeedsInput or CodexTaskStatus.Failed
            => TaskDisplayCategory.NeedsAttention,
        CodexTaskStatus.Completed => TaskDisplayCategory.RecentlyCompleted,
        _ => TaskDisplayCategory.Hidden,
    };
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MonitorEventKind
{
    Started,
    Progress,
    NeedsInput,
    Completed,
    Aborted,
    Failed,
    Stale,
}

public sealed record CodexThreadMetadata(
    string ThreadId,
    string RolloutPath,
    string Title,
    string Cwd,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool Archived,
    bool IsSubagent,
    string? ParentThreadId = null,
    string? AgentNickname = null,
    string? AgentRole = null);

public sealed record CodexTaskSnapshot
{
    public required string ThreadId { get; init; }
    public string? TurnId { get; init; }
    public required string Title { get; init; }
    public required string Project { get; init; }
    public required string Cwd { get; init; }
    public required string RolloutPath { get; init; }
    public CodexTaskStatus Status { get; init; } = CodexTaskStatus.Idle;
    public string Activity { get; init; } = "等待 Codex";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset LastActivityAt { get; init; }
    public long TokensUsed { get; init; }
    public bool Archived { get; init; }
    public bool IsSubagent { get; init; }
    public string? ParentThreadId { get; init; }
    public string? AgentNickname { get; init; }
    public string? AgentRole { get; init; }
    public string DeepLink => $"codex://threads/{ThreadId}";
    public TaskDisplayCategory DisplayCategory => Status.ToDisplayCategory();

    public double? DurationSeconds
    {
        get
        {
            if (StartedAt is null)
            {
                return null;
            }

            var end = CompletedAt ?? DateTimeOffset.UtcNow;
            return Math.Max(0, (end - StartedAt.Value).TotalSeconds);
        }
    }
}

public sealed record MonitorEvent(
    int Version,
    string EventId,
    MonitorEventKind Kind,
    DateTimeOffset OccurredAt,
    CodexTaskSnapshot Task)
{
    public string EventName => Kind switch
    {
        MonitorEventKind.Started => "task.started",
        MonitorEventKind.Progress => "task.progress",
        MonitorEventKind.NeedsInput => "task.needs_input",
        MonitorEventKind.Completed => "task.completed",
        MonitorEventKind.Aborted => "task.aborted",
        MonitorEventKind.Failed => "task.failed",
        MonitorEventKind.Stale => "task.stale",
        _ => "task.progress",
    };
}

public sealed record ParsedSessionEvent(
    string Type,
    DateTimeOffset Timestamp,
    string? TurnId = null,
    string? CallId = null,
    string? ToolName = null,
    string? Role = null,
    string? ThreadId = null,
    string? Cwd = null,
    string? ParentThreadId = null,
    string? AgentNickname = null,
    string? AgentRole = null,
    bool? IsSubagent = null,
    long? TokensUsed = null,
    string? Message = null);

public sealed record StateTransition(
    CodexTaskSnapshot Snapshot,
    MonitorEventKind? EventKind,
    bool Changed);
