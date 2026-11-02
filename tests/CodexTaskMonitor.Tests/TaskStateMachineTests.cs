using CodexTaskMonitor.Core;

namespace CodexTaskMonitor.Tests;

public sealed class TaskStateMachineTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-07-29T08:00:00Z");

    [Fact]
    public void TransitionsFromRunningToCompleted()
    {
        var machine = CreateMachine();

        var started = machine.Apply(new ParsedSessionEvent("task_started", Start, TurnId: "turn-1"));
        var completed = machine.Apply(new ParsedSessionEvent("task_complete", Start.AddSeconds(4), TurnId: "turn-1"));

        Assert.Equal(CodexTaskStatus.Running, started.Snapshot.Status);
        Assert.Equal(MonitorEventKind.Started, started.EventKind);
        Assert.Equal(CodexTaskStatus.Completed, completed.Snapshot.Status);
        Assert.Equal(MonitorEventKind.Completed, completed.EventKind);
        Assert.Equal(Start.AddSeconds(4), completed.Snapshot.CompletedAt);
    }

    [Fact]
    public void KeepsTaskWaitingUntilInputCallReturns()
    {
        var machine = CreateMachine();
        machine.Apply(new ParsedSessionEvent("task_started", Start, TurnId: "turn-1"));

        var waiting = machine.Apply(
            new ParsedSessionEvent("request_user_input", Start.AddSeconds(1), CallId: "call-1"));
        var prematureComplete = machine.Apply(
            new ParsedSessionEvent("task_complete", Start.AddSeconds(2), TurnId: "turn-1"));
        var resumed = machine.Apply(
            new ParsedSessionEvent("tool_output", Start.AddSeconds(3), CallId: "call-1"));

        Assert.Equal(CodexTaskStatus.NeedsInput, waiting.Snapshot.Status);
        Assert.Equal(CodexTaskStatus.NeedsInput, prematureComplete.Snapshot.Status);
        Assert.Equal(CodexTaskStatus.Running, resumed.Snapshot.Status);
    }

    [Theory]
    [InlineData("turn_aborted", CodexTaskStatus.Aborted, MonitorEventKind.Aborted)]
    [InlineData("turn_error", CodexTaskStatus.Failed, MonitorEventKind.Failed)]
    [InlineData("stream_error", CodexTaskStatus.Failed, MonitorEventKind.Failed)]
    public void RecognizesTerminalFailures(
        string eventType,
        CodexTaskStatus expectedStatus,
        MonitorEventKind expectedKind)
    {
        var machine = CreateMachine();
        machine.Apply(new ParsedSessionEvent("task_started", Start, TurnId: "turn-1"));

        var result = machine.Apply(new ParsedSessionEvent(eventType, Start.AddSeconds(1)));

        Assert.Equal(expectedStatus, result.Snapshot.Status);
        Assert.Equal(expectedKind, result.EventKind);
    }

    [Fact]
    public void MarksSilentRunningTaskStale()
    {
        var machine = CreateMachine();
        machine.Apply(new ParsedSessionEvent("task_started", Start));

        var result = machine.MarkStale(Start.AddMinutes(11), TimeSpan.FromMinutes(10));

        Assert.Equal(CodexTaskStatus.Stale, result.Snapshot.Status);
        Assert.Equal(MonitorEventKind.Stale, result.EventKind);
    }

    [Fact]
    public void RestoredNeedsInputRemainsPendingUntilOutput()
    {
        var metadata = Metadata();
        var restored = new TaskStateMachine(metadata)
            .Apply(new ParsedSessionEvent("request_user_input", Start, CallId: "old-call"))
            .Snapshot;
        var machine = new TaskStateMachine(metadata, restored);

        var complete = machine.Apply(
            new ParsedSessionEvent("task_complete", Start.AddSeconds(1), TurnId: "turn-1"));
        var output = machine.Apply(
            new ParsedSessionEvent("tool_output", Start.AddSeconds(2)));

        Assert.Equal(CodexTaskStatus.NeedsInput, complete.Snapshot.Status);
        Assert.Equal(CodexTaskStatus.Running, output.Snapshot.Status);
    }

    private static TaskStateMachine CreateMachine()
    {
        return new TaskStateMachine(Metadata());
    }

    private static CodexThreadMetadata Metadata()
    {
        return new CodexThreadMetadata(
            "thread-1",
            @"C:\sessions\thread-1.jsonl",
            "Build the monitor",
            @"C:\work\monitor",
            Start,
            Start,
            false,
            false);
    }
}
