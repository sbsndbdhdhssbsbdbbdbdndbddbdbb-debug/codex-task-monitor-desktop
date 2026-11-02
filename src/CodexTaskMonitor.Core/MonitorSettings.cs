using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexTaskMonitor.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotificationMode
{
    Always,
    BackgroundOnly,
    NeedsAttentionOnly,
    Never,
}

public sealed record MonitorSettings
{
    public int ApiPort { get; init; } = 47831;
    public int PollIntervalMilliseconds { get; init; } = 1000;
    public int ReconcileIntervalMilliseconds { get; init; } = 2000;
    public int StaleAfterMinutes { get; init; } = 10;
    public int RecentCompletionHours { get; init; } = 24;
    public int AttentionReminderMinutes { get; init; } = 30;
    public NotificationMode NotificationMode { get; init; } = NotificationMode.BackgroundOnly;
    public bool SoundEnabled { get; init; } = true;
    public bool DoNotDisturbEnabled { get; init; }
    public bool PetApiEnabled { get; init; } = true;
    public bool PrivacyMode { get; init; } = true;
    public bool AutoStart { get; init; }
    public bool StartMinimized { get; init; }
    public bool DesktopPetEnabled { get; init; } = true;
    public bool DesktopPetCollapsed { get; init; }
    public bool DesktopPetAlwaysOnTop { get; init; } = true;
    public bool DesktopPetAnimationsEnabled { get; init; } = true;
    public string DesktopPetPackageId { get; init; } = "mint";
    public double? DesktopPetLeft { get; init; }
    public double? DesktopPetTop { get; init; }
    public IReadOnlyList<RecentTaskAcknowledgement> AcknowledgedRecentTasks { get; init; }
        = Array.Empty<RecentTaskAcknowledgement>();
}

public sealed record RecentTaskAcknowledgement(string ThreadId, DateTimeOffset CompletedAt);

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _path;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public SettingsStore(string path)
    {
        _path = path;
    }

    public async Task<MonitorSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_path))
            {
                var defaults = new MonitorSettings();
                await SaveAsync(defaults, cancellationToken);
                return defaults;
            }

            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<MonitorSettings>(stream, JsonOptions, cancellationToken)
                   ?? new MonitorSettings();
        }
        catch (IOException)
        {
            return new MonitorSettings();
        }
        catch (JsonException)
        {
            return new MonitorSettings();
        }
    }

    public async Task SaveAsync(MonitorSettings settings, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tempPath = $"{_path}.{Environment.ProcessId}.tmp";
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(tempPath, _path, true);
        }
        finally { _writeGate.Release(); }
    }
}
