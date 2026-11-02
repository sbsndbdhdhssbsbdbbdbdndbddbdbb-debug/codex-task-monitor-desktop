namespace CodexTaskMonitor.Core;

public sealed class TaskStateMachine
{
    private readonly HashSet<string> _pendingInputCalls = new(StringComparer.Ordinal);
    private int _anonymousPendingInputs;

    public TaskStateMachine(CodexThreadMetadata metadata, CodexTaskSnapshot? restored = null)
    {
        Snapshot = restored is null
            ? FromMetadata(metadata)
            : MergeMetadata(restored, metadata);
        if (Snapshot.Status == CodexTaskStatus.NeedsInput)
        {
            _anonymousPendingInputs = 1;
        }
    }

    public CodexTaskSnapshot Snapshot { get; private set; }

    public void UpdateMetadata(CodexThreadMetadata metadata)
    {
        Snapshot = MergeMetadata(Snapshot, metadata);
    }

    public StateTransition Apply(ParsedSessionEvent sessionEvent)
    {
        var previous = Snapshot;
        MonitorEventKind? kind = null;
        var timestamp = sessionEvent.Timestamp;

        switch (sessionEvent.Type)
        {
            case "session_meta":
                Snapshot = Snapshot with
                {
                    Cwd = sessionEvent.Cwd ?? Snapshot.Cwd,
                    Project = ProjectName(sessionEvent.Cwd ?? Snapshot.Cwd),
                    IsSubagent = sessionEvent.IsSubagent ?? Snapshot.IsSubagent,
                    ParentThreadId = sessionEvent.ParentThreadId ?? Snapshot.ParentThreadId,
                    AgentNickname = sessionEvent.AgentNickname ?? Snapshot.AgentNickname,
                    AgentRole = sessionEvent.AgentRole ?? Snapshot.AgentRole,
                };
                break;

            case "turn_context":
                Snapshot = Snapshot with
                {
                    TurnId = sessionEvent.TurnId ?? Snapshot.TurnId,
                    Cwd = sessionEvent.Cwd ?? Snapshot.Cwd,
                    Project = ProjectName(sessionEvent.Cwd ?? Snapshot.Cwd),
                };
                break;

            case "task_started":
                _pendingInputCalls.Clear();
                _anonymousPendingInputs = 0;
                Snapshot = Snapshot with
                {
                    TurnId = sessionEvent.TurnId ?? Snapshot.TurnId,
                    Status = CodexTaskStatus.Running,
                    StartedAt = timestamp,
                    CompletedAt = null,
                    LastActivityAt = timestamp,
                    UpdatedAt = timestamp,
                    Activity = "Codex 正在处理任务",
                };
                kind = MonitorEventKind.Started;
                break;

            case "request_user_input":
                if (!string.IsNullOrWhiteSpace(sessionEvent.CallId))
                {
                    _pendingInputCalls.Add(sessionEvent.CallId);
                }
                else
                {
                    _anonymousPendingInputs++;
                }

                Snapshot = Snapshot with
                {
                    Status = CodexTaskStatus.NeedsInput,
                    LastActivityAt = timestamp,
                    UpdatedAt = timestamp,
                    Activity = "等待你的输入",
                };
                kind = MonitorEventKind.NeedsInput;
                break;

            case "tool_output":
                if (!string.IsNullOrWhiteSpace(sessionEvent.CallId))
                {
                    _pendingInputCalls.Remove(sessionEvent.CallId);
                }
                else if (_anonymousPendingInputs > 0)
                {
                    _anonymousPendingInputs--;
                }

                Snapshot = Snapshot with
                {
                    Status = HasPendingInput ? CodexTaskStatus.NeedsInput : CodexTaskStatus.Running,
                    LastActivityAt = timestamp,
                    UpdatedAt = timestamp,
                    Activity = HasPendingInput ? "等待你的输入" : "工具已返回结果",
                };
                kind = MonitorEventKind.Progress;
                break;

            case "task_complete":
                Snapshot = Snapshot with
                {
                    TurnId = sessionEvent.TurnId ?? Snapshot.TurnId,
                    Status = HasPendingInput ? CodexTaskStatus.NeedsInput : CodexTaskStatus.Completed,
                    CompletedAt = HasPendingInput ? null : timestamp,
                    LastActivityAt = timestamp,
                    UpdatedAt = timestamp,
                    Activity = HasPendingInput ? "等待你的输入" : "任务已经完成",
                };
                kind = HasPendingInput ? MonitorEventKind.NeedsInput : MonitorEventKind.Completed;
                break;

            case "turn_aborted":
                Snapshot = Snapshot with
                {
                    Status = CodexTaskStatus.Aborted,
                    CompletedAt = timestamp,
                    LastActivityAt = timestamp,
                    UpdatedAt = timestamp,
                    Activity = "任务已中断",
                };
                kind = MonitorEventKind.Aborted;
                break;

            case "turn_error":
            case "stream_error":
            case "fatal_error":
                Snapshot = Snapshot with
                {
                    Status = CodexTaskStatus.Failed,
                    CompletedAt = timestamp,
                    LastActivityAt = timestamp,
                    UpdatedAt = timestamp,
                    Activity = string.IsNullOrWhiteSpace(sessionEvent.Message)
                        ? "任务执行失败"
                        : sessionEvent.Message,
                };
                kind = MonitorEventKind.Failed;
                break;

            case "token_count":
                Snapshot = Touch(
                    Snapshot with { TokensUsed = Math.Max(Snapshot.TokensUsed, sessionEvent.TokensUsed ?? 0) },
                    timestamp,
                    Snapshot.Status == CodexTaskStatus.NeedsInput ? "等待你的输入" : "正在生成内容");
                kind = MonitorEventKind.Progress;
                break;

            case "reasoning":
            case "agent_reasoning":
                Snapshot = Touch(Snapshot, timestamp, "正在思考");
                kind = MonitorEventKind.Progress;
                break;

            case "tool_call":
                Snapshot = Touch(
                    Snapshot,
                    timestamp,
                    string.IsNullOrWhiteSpace(sessionEvent.ToolName)
                        ? "正在调用工具"
                        : $"正在调用 {sessionEvent.ToolName}");
                kind = MonitorEventKind.Progress;
                break;

            case "assistant_message":
            case "agent_message":
                Snapshot = Touch(Snapshot, timestamp, "正在整理回答");
                kind = MonitorEventKind.Progress;
                break;

            case "user_message":
                if (Snapshot.Status is CodexTaskStatus.Idle or CodexTaskStatus.Completed
                    or CodexTaskStatus.Aborted or CodexTaskStatus.Failed or CodexTaskStatus.Stale)
                {
                    Snapshot = Snapshot with
                    {
                        Status = CodexTaskStatus.Running,
                        StartedAt = timestamp,
                        CompletedAt = null,
                    };
                    kind = MonitorEventKind.Started;
                }

                Snapshot = Touch(Snapshot, timestamp, "收到新任务");
                break;
        }

        return new StateTransition(Snapshot, kind, Snapshot != previous);
    }

    public StateTransition MarkStale(DateTimeOffset now, TimeSpan threshold)
    {
        if (Snapshot.Status != CodexTaskStatus.Running || now - Snapshot.LastActivityAt < threshold)
        {
            return new StateTransition(Snapshot, null, false);
        }

        Snapshot = Snapshot with
        {
            Status = CodexTaskStatus.Stale,
            UpdatedAt = now,
            Activity = "长时间没有新活动",
        };
        return new StateTransition(Snapshot, MonitorEventKind.Stale, true);
    }

    private bool HasPendingInput => _pendingInputCalls.Count > 0 || _anonymousPendingInputs > 0;

    private static CodexTaskSnapshot Touch(
        CodexTaskSnapshot snapshot,
        DateTimeOffset timestamp,
        string activity)
    {
        return snapshot with
        {
            Status = snapshot.Status == CodexTaskStatus.NeedsInput
                ? CodexTaskStatus.NeedsInput
                : CodexTaskStatus.Running,
            LastActivityAt = timestamp,
            UpdatedAt = timestamp,
            Activity = activity,
        };
    }

    private static CodexTaskSnapshot FromMetadata(CodexThreadMetadata metadata)
    {
        return new CodexTaskSnapshot
        {
            ThreadId = metadata.ThreadId,
            Title = SanitizeTitle(metadata.Title),
            Project = ProjectName(metadata.Cwd),
            Cwd = metadata.Cwd,
            RolloutPath = metadata.RolloutPath,
            CreatedAt = metadata.CreatedAt,
            UpdatedAt = metadata.UpdatedAt,
            LastActivityAt = metadata.UpdatedAt,
            Archived = metadata.Archived,
            IsSubagent = metadata.IsSubagent,
            ParentThreadId = metadata.ParentThreadId,
            AgentNickname = metadata.AgentNickname,
            AgentRole = metadata.AgentRole,
        };
    }

    private static CodexTaskSnapshot MergeMetadata(
        CodexTaskSnapshot snapshot,
        CodexThreadMetadata metadata)
    {
        return snapshot with
        {
            Title = SanitizeTitle(metadata.Title),
            Project = ProjectName(metadata.Cwd),
            Cwd = metadata.Cwd,
            RolloutPath = metadata.RolloutPath,
            Archived = metadata.Archived,
            IsSubagent = metadata.IsSubagent,
            ParentThreadId = metadata.ParentThreadId ?? snapshot.ParentThreadId,
            AgentNickname = metadata.AgentNickname ?? snapshot.AgentNickname,
            AgentRole = metadata.AgentRole ?? snapshot.AgentRole,
        };
    }

    private static string ProjectName(string cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd))
        {
            return "Codex";
        }

        return Path.GetFileName(cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } name
            ? name
            : cwd;
    }

    private static string SanitizeTitle(string title)
    {
        var normalized = string.Join(" ", (title ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0)
        {
            return "未命名任务";
        }

        return normalized.Length <= 120 ? normalized : $"{normalized[..117]}...";
    }
}
