using System.Globalization;
using System.Text.Json;

namespace CodexTaskMonitor.Core;

public static class SessionEventParser
{
    public static ParsedSessionEvent? Parse(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var topLevelType = GetString(root, "type");
            var timestamp = ParseTimestamp(GetString(root, "timestamp")) ?? DateTimeOffset.UtcNow;
            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return topLevelType switch
            {
                "session_meta" => ParseSessionMeta(payload, timestamp),
                "turn_context" => new ParsedSessionEvent(
                    "turn_context",
                    timestamp,
                    TurnId: GetString(payload, "turn_id"),
                    Cwd: GetString(payload, "cwd")),
                "event_msg" => ParseEventMessage(payload, timestamp),
                "response_item" => ParseResponseItem(payload, timestamp),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ParsedSessionEvent ParseSessionMeta(JsonElement payload, DateTimeOffset timestamp)
    {
        var source = payload.TryGetProperty("source", out var sourceElement)
            ? sourceElement.GetRawText()
            : string.Empty;
        var threadSource = GetString(payload, "thread_source");
        var isSubagent = string.Equals(threadSource, "subagent", StringComparison.OrdinalIgnoreCase)
                         || source.Contains("\"subagent\"", StringComparison.OrdinalIgnoreCase);
        string? parentThreadId = GetString(payload, "parent_thread_id");
        string? nickname = GetString(payload, "agent_nickname");
        string? role = GetString(payload, "agent_role");

        if (payload.TryGetProperty("source", out sourceElement)
            && sourceElement.ValueKind == JsonValueKind.Object
            && sourceElement.TryGetProperty("subagent", out var subagent)
            && subagent.ValueKind == JsonValueKind.Object
            && subagent.TryGetProperty("thread_spawn", out var spawn)
            && spawn.ValueKind == JsonValueKind.Object)
        {
            parentThreadId = GetString(spawn, "parent_thread_id");
            nickname ??= GetString(spawn, "agent_nickname");
            role ??= GetString(spawn, "agent_role");
        }

        return new ParsedSessionEvent(
            "session_meta",
            timestamp,
            ThreadId: GetString(payload, "id") ?? GetString(payload, "session_id"),
            Cwd: GetString(payload, "cwd"),
            ParentThreadId: parentThreadId,
            AgentNickname: nickname,
            AgentRole: role,
            IsSubagent: isSubagent);
    }

    private static ParsedSessionEvent ParseEventMessage(JsonElement payload, DateTimeOffset timestamp)
    {
        var type = GetString(payload, "type") ?? "event_msg";
        var message = FirstText(payload, "last_agent_message", "last_assistant_message", "message", "content", "text");
        return new ParsedSessionEvent(
            type,
            timestamp,
            TurnId: GetString(payload, "turn_id"),
            CallId: GetString(payload, "call_id"),
            ToolName: GetString(payload, "name"),
            TokensUsed: type == "token_count" ? FindLargestTokenTotal(payload) : null,
            Message: message);
    }

    private static ParsedSessionEvent ParseResponseItem(JsonElement payload, DateTimeOffset timestamp)
    {
        var payloadType = GetString(payload, "type") ?? "response_item";
        var name = GetString(payload, "name") ?? GetString(payload, "function_name");
        if (name is null
            && payload.TryGetProperty("function", out var function)
            && function.ValueKind == JsonValueKind.Object)
        {
            name = GetString(function, "name");
        }

        var callId = GetString(payload, "call_id")
                     ?? GetString(payload, "tool_call_id")
                     ?? GetString(payload, "function_call_id")
                     ?? GetString(payload, "id");
        var role = GetString(payload, "role");
        var message = ExtractText(payload);

        var normalizedType = payloadType switch
        {
            "function_call" or "custom_tool_call" or "tool_use" when
                string.Equals(name, "request_user_input", StringComparison.OrdinalIgnoreCase)
                => "request_user_input",
            "function_call" or "custom_tool_call" or "tool_use" => "tool_call",
            "function_call_output" or "custom_tool_call_output" => "tool_output",
            "message" => role == "user" ? "user_message" : "assistant_message",
            "reasoning" => "reasoning",
            _ => payloadType,
        };

        return new ParsedSessionEvent(
            normalizedType,
            timestamp,
            CallId: callId,
            ToolName: name,
            Role: role,
            Message: message);
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            return numeric > 10_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(numeric)
                : DateTimeOffset.FromUnixTimeSeconds(numeric);
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var timestamp)
            ? timestamp.ToUniversalTime()
            : null;
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null,
        };
    }

    private static string? FirstText(JsonElement payload, params string[] names)
    {
        foreach (var name in names)
        {
            if (!payload.TryGetProperty(name, out var value))
            {
                continue;
            }

            var text = ExtractText(value);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static string? ExtractText(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Array:
            {
                var parts = element.EnumerateArray()
                    .Select(ExtractText)
                    .Where(text => !string.IsNullOrWhiteSpace(text));
                var combined = string.Join(Environment.NewLine, parts!);
                return string.IsNullOrWhiteSpace(combined) ? null : combined;
            }
            case JsonValueKind.Object:
                foreach (var name in new[] { "text", "content", "message", "value" })
                {
                    if (element.TryGetProperty(name, out var value))
                    {
                        var text = ExtractText(value);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return text;
                        }
                    }
                }

                break;
        }

        return null;
    }

    private static long? FindLargestTokenTotal(JsonElement element)
    {
        long? best = null;
        Walk(element);
        return best;

        void Walk(JsonElement current)
        {
            if (current.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in current.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Number
                        && property.Value.TryGetInt64(out var value)
                        && (property.Name.Contains("total", StringComparison.OrdinalIgnoreCase)
                            || property.Name.Equals("output_tokens", StringComparison.OrdinalIgnoreCase)))
                    {
                        best = Math.Max(best ?? 0, value);
                    }
                    else
                    {
                        Walk(property.Value);
                    }
                }
            }
            else if (current.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in current.EnumerateArray())
                {
                    Walk(item);
                }
            }
        }
    }
}
