using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using CodexTaskMonitor.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CodexTaskMonitor.App;

public sealed class MonitorApiHost : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly CodexMonitorService _monitor;
    private readonly MonitorPaths _paths;
    private readonly MonitorSettings _settings;
    private readonly AtomicStateWriter _stateWriter;
    private readonly ConcurrentDictionary<Guid, Channel<MonitorEvent>> _clients = new();
    private WebApplication? _application;
    private string _token = string.Empty;

    public MonitorApiHost(
        CodexMonitorService monitor,
        MonitorPaths paths,
        MonitorSettings settings)
    {
        _monitor = monitor;
        _paths = paths;
        _settings = settings;
        _stateWriter = new AtomicStateWriter(paths.StateFile);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        TryDeleteStaleIntegrationFile();
        if (!_settings.PetApiEnabled)
        {
            return;
        }

        _token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{_settings.ApiPort}");
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.Equals("/api/v1/health"))
            {
                await next();
                return;
            }

            var authorization = context.Request.Headers.Authorization.ToString();
            if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(authorization[7..]),
                    Encoding.UTF8.GetBytes(_token)))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next();
        });

        app.MapGet("/api/v1/health", () => Results.Json(new
        {
            status = "ok",
            version = 1,
            paused = _monitor.IsPaused,
            taskCount = _monitor.GetTasks().Count,
            updatedAt = DateTimeOffset.UtcNow,
        }, JsonOptions));
        app.MapGet("/api/v1/tasks", () =>
            Results.Json(new
            {
                version = 1,
                tasks = _monitor.GetTasks().Select(ToPublicTask),
            }, JsonOptions));
        app.MapGet("/api/v1/tasks/{threadId}", (string threadId) =>
        {
            var task = _monitor.GetTasks()
                .FirstOrDefault(item => string.Equals(
                    item.ThreadId,
                    threadId,
                    StringComparison.Ordinal));
            return task is null
                ? Results.NotFound()
                : Results.Json(ToPublicTask(task), JsonOptions);
        });
        app.Map("/api/v1/events", HandleWebSocketAsync);

        _application = app;
        _monitor.TaskEvent += OnTaskEvent;
        await app.StartAsync(cancellationToken);
        await WriteIntegrationFileAsync(cancellationToken);
        await WriteStateAsync(cancellationToken);
        await AppLog.WriteAsync(
            "INFO",
            $"Pet API listening on http://127.0.0.1:{_settings.ApiPort}");
    }

    public async ValueTask DisposeAsync()
    {
        _monitor.TaskEvent -= OnTaskEvent;
        foreach (var client in _clients.Values)
        {
            client.Writer.TryComplete();
        }

        if (_application is not null)
        {
            await _application.StopAsync(TimeSpan.FromSeconds(3));
            await _application.DisposeAsync();
        }

        TryDeleteStaleIntegrationFile();
    }

    private async Task HandleWebSocketAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<MonitorEvent>(new BoundedChannelOptions(128)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        _clients[id] = channel;
        using var connectionStop = CancellationTokenSource.CreateLinkedTokenSource(
            context.RequestAborted);
        try
        {
            await SendSnapshotAsync(socket, connectionStop.Token);
            var sendTask = SendEventsAsync(socket, channel.Reader, connectionStop.Token);
            var receiveTask = ReceiveUntilClosedAsync(socket, connectionStop.Token);
            await Task.WhenAny(sendTask, receiveTask);
            connectionStop.Cancel();
            await IgnoreConnectionShutdownAsync(sendTask, receiveTask);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected.
        }
        catch (WebSocketException)
        {
            // Client disconnected without a close handshake.
        }
        finally
        {
            _clients.TryRemove(id, out _);
        }
    }

    private async Task SendSnapshotAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1,
            eventName = "snapshot",
            occurredAt = DateTimeOffset.UtcNow,
            tasks = _monitor.GetTasks().Select(ToPublicTask),
        }, JsonOptions);
        await socket.SendAsync(
            payload,
            WebSocketMessageType.Text,
            true,
            cancellationToken);
    }

    private async Task SendEventsAsync(
        WebSocket socket,
        ChannelReader<MonitorEvent> events,
        CancellationToken cancellationToken)
    {
        await foreach (var monitorEvent in events.ReadAllAsync(cancellationToken))
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(
                ToPublicEvent(monitorEvent),
                JsonOptions);
            await socket.SendAsync(
                payload,
                WebSocketMessageType.Text,
                true,
                cancellationToken);
        }
    }

    private static async Task ReceiveUntilClosedAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType != WebSocketMessageType.Close)
            {
                continue;
            }

            await socket.CloseOutputAsync(
                result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                result.CloseStatusDescription,
                CancellationToken.None);
            return;
        }
    }

    private static async Task IgnoreConnectionShutdownAsync(params Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // The peer closed or the app is shutting down.
        }
        catch (WebSocketException)
        {
            // The peer disconnected without a close handshake.
        }
    }

    private void OnTaskEvent(object? sender, MonitorEvent monitorEvent)
    {
        foreach (var client in _clients.Values)
        {
            client.Writer.TryWrite(monitorEvent);
        }

        _ = WriteStateSafelyAsync();
    }

    private async Task WriteStateSafelyAsync()
    {
        try
        {
            await WriteStateAsync();
        }
        catch (Exception exception)
        {
            await AppLog.WriteAsync("WARN", "Unable to write state.json", exception);
        }
    }

    private Task WriteStateAsync(CancellationToken cancellationToken = default)
    {
        var tasks = _monitor.GetTasks()
            .Select(task => _settings.PrivacyMode ? SanitizeForPrivacy(task) : task)
            .ToArray();
        return _stateWriter.WriteAsync(tasks, cancellationToken);
    }

    private object ToPublicTask(CodexTaskSnapshot task)
    {
        return new
        {
            task.ThreadId,
            task.TurnId,
            title = _settings.PrivacyMode ? "Codex 任务" : task.Title,
            task.Project,
            task.Status,
            activity = _settings.PrivacyMode ? PrivateActivity(task.Status) : task.Activity,
            task.CreatedAt,
            task.UpdatedAt,
            task.StartedAt,
            task.CompletedAt,
            task.LastActivityAt,
            task.TokensUsed,
            task.IsSubagent,
            task.ParentThreadId,
            task.AgentNickname,
            task.AgentRole,
            task.DeepLink,
            task.DurationSeconds,
        };
    }

    private object ToPublicEvent(MonitorEvent monitorEvent)
    {
        return new
        {
            monitorEvent.Version,
            monitorEvent.EventId,
            eventName = monitorEvent.EventName,
            monitorEvent.Kind,
            monitorEvent.OccurredAt,
            task = ToPublicTask(monitorEvent.Task),
        };
    }

    private async Task WriteIntegrationFileAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.AppDataRoot);
        var tempPath = $"{_paths.IntegrationFile}.{Environment.ProcessId}.tmp";
        await File.WriteAllTextAsync(
            tempPath,
            JsonSerializer.Serialize(new
            {
                version = 1,
                baseUrl = $"http://127.0.0.1:{_settings.ApiPort}",
                token = _token,
                stateFile = _paths.StateFile,
                websocketUrl = $"ws://127.0.0.1:{_settings.ApiPort}/api/v1/events",
                updatedAt = DateTimeOffset.UtcNow,
            }, new JsonSerializerOptions(JsonOptions) { WriteIndented = true }),
            cancellationToken);
        File.Move(tempPath, _paths.IntegrationFile, true);
    }

    private CodexTaskSnapshot SanitizeForPrivacy(CodexTaskSnapshot task)
    {
        return task with
        {
            Title = "Codex 任务",
            Activity = PrivateActivity(task.Status),
            Cwd = string.Empty,
            RolloutPath = string.Empty,
        };
    }

    private static string PrivateActivity(CodexTaskStatus status)
    {
        return status switch
        {
            CodexTaskStatus.Running => "Codex 正在处理任务",
            CodexTaskStatus.NeedsInput => "等待你的输入",
            CodexTaskStatus.Completed => "任务已经完成",
            CodexTaskStatus.Aborted => "任务已经中止",
            CodexTaskStatus.Failed => "任务执行失败",
            CodexTaskStatus.Stale => "长时间没有新活动",
            _ => "等待 Codex",
        };
    }

    private void TryDeleteStaleIntegrationFile()
    {
        try
        {
            if (File.Exists(_paths.IntegrationFile))
            {
                File.Delete(_paths.IntegrationFile);
            }
        }
        catch (IOException)
        {
            // A previous instance may still be exiting; startup will fail cleanly if the port is busy.
        }
    }
}
