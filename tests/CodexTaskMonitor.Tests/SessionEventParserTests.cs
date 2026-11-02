using CodexTaskMonitor.Core;

namespace CodexTaskMonitor.Tests;

public sealed class SessionEventParserTests
{
    [Fact]
    public void ParsesTaskLifecycleEvents()
    {
        var started = SessionEventParser.Parse(
            """{"timestamp":"2026-07-29T08:00:00Z","type":"event_msg","payload":{"type":"task_started","turn_id":"turn-1"}}""");
        var completed = SessionEventParser.Parse(
            """{"timestamp":"2026-07-29T08:01:00Z","type":"event_msg","payload":{"type":"task_complete","turn_id":"turn-1","last_agent_message":"done"}}""");

        Assert.Equal("task_started", started?.Type);
        Assert.Equal("turn-1", started?.TurnId);
        Assert.Equal("task_complete", completed?.Type);
        Assert.Equal("done", completed?.Message);
    }

    [Fact]
    public void NormalizesRequestUserInputAndOutput()
    {
        var request = SessionEventParser.Parse(
            """{"timestamp":"2026-07-29T08:00:00Z","type":"response_item","payload":{"type":"function_call","name":"request_user_input","call_id":"call-7"}}""");
        var output = SessionEventParser.Parse(
            """{"timestamp":"2026-07-29T08:00:01Z","type":"response_item","payload":{"type":"function_call_output","call_id":"call-7","output":"approved"}}""");

        Assert.Equal("request_user_input", request?.Type);
        Assert.Equal("call-7", request?.CallId);
        Assert.Equal("tool_output", output?.Type);
        Assert.Equal("call-7", output?.CallId);
    }

    [Fact]
    public void ExtractsSubagentParentMetadata()
    {
        var parsed = SessionEventParser.Parse(
            """
            {"timestamp":"2026-07-29T08:00:00Z","type":"session_meta","payload":{"id":"child","cwd":"C:\\work","source":{"subagent":{"thread_spawn":{"parent_thread_id":"parent","agent_nickname":"Scout","agent_role":"explorer"}}}}}
            """);

        Assert.True(parsed?.IsSubagent);
        Assert.Equal("parent", parsed?.ParentThreadId);
        Assert.Equal("Scout", parsed?.AgentNickname);
        Assert.Equal("explorer", parsed?.AgentRole);
    }

    [Fact]
    public void ExtractsCurrentDesktopSubagentParentMetadata()
    {
        var parsed = SessionEventParser.Parse(
            """
            {"timestamp":"2026-07-29T08:00:00Z","type":"session_meta","payload":{"id":"child","parent_thread_id":"parent","cwd":"C:\\work","source":{"subagent":{"other":"guardian"}},"thread_source":"subagent"}}
            """);

        Assert.True(parsed?.IsSubagent);
        Assert.Equal("parent", parsed?.ParentThreadId);
    }

    [Fact]
    public void ReturnsNullForPartialJson()
    {
        Assert.Null(SessionEventParser.Parse("""{"type":"event_msg","payload":"""));
    }

    [Theory]
    [InlineData("turn failed after retry", true)]
    [InlineData("stream disconnected before completion", true)]
    [InlineData("failed to refresh available models", false)]
    [InlineData("temporary connection retry", false)]
    public void FiltersTerminalLogFailures(string message, bool expected)
    {
        Assert.Equal(expected, CodexLogFailureReader.IsTerminalFailure(message));
    }
}
