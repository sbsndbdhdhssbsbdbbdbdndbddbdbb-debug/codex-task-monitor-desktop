using System.Media;
using System.Windows;
using System.Windows.Threading;
using CodexTaskMonitor.Core;

namespace CodexTaskMonitor.App;

public sealed class ToastManager
{
    private readonly MonitorSettings _settings;
    private readonly Func<bool> _isMainWindowActive;
    private readonly Action _showMainWindow;
    private readonly List<MonitorEvent> _pending = new();
    private readonly DispatcherTimer _aggregateTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(800),
    };
    private readonly DispatcherTimer _attentionReminderTimer = new()
    {
        Interval = TimeSpan.FromMinutes(1),
    };
    private readonly Dictionary<string, PendingAttentionReminder> _pendingAttention = new(StringComparer.Ordinal);
    private ToastWindow? _current;

    public ToastManager(
        MonitorSettings settings,
        Func<bool> isMainWindowActive,
        Action showMainWindow)
    {
        _settings = settings;
        _isMainWindowActive = isMainWindowActive;
        _showMainWindow = showMainWindow;
        _aggregateTimer.Tick += (_, _) => Flush();
        _attentionReminderTimer.Tick += (_, _) => SendAttentionReminders();
        _attentionReminderTimer.Start();
    }

    public void Handle(MonitorEvent monitorEvent)
    {
        TrackAttentionState(monitorEvent);
        if (!ShouldNotify(monitorEvent))
        {
            return;
        }

        _pending.Add(monitorEvent);
        _aggregateTimer.Stop();
        _aggregateTimer.Start();
    }

    public void RefreshTasks(IEnumerable<CodexTaskSnapshot> tasks)
    {
        var awaiting = tasks
            .Where(task => task.Status == CodexTaskStatus.NeedsInput)
            .ToDictionary(task => task.ThreadId, StringComparer.Ordinal);
        foreach (var task in awaiting.Values)
        {
            _pendingAttention.TryAdd(task.ThreadId, new PendingAttentionReminder(
                task,
                DateTimeOffset.UtcNow.AddMinutes(_settings.AttentionReminderMinutes)));
        }

        foreach (var threadId in _pendingAttention.Keys.Except(awaiting.Keys).ToArray())
        {
            _pendingAttention.Remove(threadId);
        }
    }

    public void DismissCurrent()
    {
        _aggregateTimer.Stop();
        _pending.Clear();
        _current?.Close();
        _current = null;
    }

    private void TrackAttentionState(MonitorEvent monitorEvent)
    {
        if (monitorEvent.Kind == MonitorEventKind.NeedsInput)
        {
            _pendingAttention[monitorEvent.Task.ThreadId] = new PendingAttentionReminder(
                monitorEvent.Task,
                DateTimeOffset.UtcNow.AddMinutes(_settings.AttentionReminderMinutes));
            return;
        }

        if (monitorEvent.Task.Status != CodexTaskStatus.NeedsInput)
        {
            _pendingAttention.Remove(monitorEvent.Task.ThreadId);
        }
    }

    private void SendAttentionReminders()
    {
        if (_settings.DoNotDisturbEnabled || _pendingAttention.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var (threadId, reminder) in _pendingAttention.ToArray())
        {
            if (reminder.NextReminderAt > now)
            {
                continue;
            }

            _pending.Add(new MonitorEvent(
                1,
                Guid.NewGuid().ToString("N"),
                MonitorEventKind.NeedsInput,
                now,
                reminder.Task));
            _pendingAttention[threadId] = reminder with
            {
                NextReminderAt = now.AddMinutes(_settings.AttentionReminderMinutes),
            };
        }

        if (_pending.Count > 0)
        {
            _aggregateTimer.Stop();
            _aggregateTimer.Start();
        }
    }

    private bool ShouldNotify(MonitorEvent monitorEvent)
    {
        if (_settings.DoNotDisturbEnabled)
        {
            return false;
        }

        if (monitorEvent.Kind == MonitorEventKind.Completed && monitorEvent.Task.IsSubagent)
        {
            return false;
        }

        if (monitorEvent.Kind == MonitorEventKind.Stale)
        {
            return false;
        }

        if (monitorEvent.Kind is MonitorEventKind.NeedsInput
            or MonitorEventKind.Failed
            or MonitorEventKind.Aborted)
        {
            return true;
        }

        if (monitorEvent.Kind != MonitorEventKind.Completed)
        {
            return false;
        }

        return _settings.NotificationMode switch
        {
            NotificationMode.Always => true,
            NotificationMode.BackgroundOnly => !_isMainWindowActive(),
            NotificationMode.NeedsAttentionOnly or NotificationMode.Never => false,
            _ => false,
        };
    }

    private void Flush()
    {
        _aggregateTimer.Stop();
        if (_pending.Count == 0)
        {
            return;
        }

        var events = _pending.ToArray();
        _pending.Clear();
        var primary = events
            .OrderBy(monitorEvent => Priority(monitorEvent.Kind))
            .First();

        _current?.Close();
        _current = new ToastWindow();
        _current.Configure(
            primary,
            _settings.PrivacyMode,
            events.Length,
            events.Length == 1
                ? () => DeepLinkLauncher.Open(primary.Task)
                : _showMainWindow);
        _current.Closed += (_, _) => _current = null;
        _current.Show();

        if (_settings.SoundEnabled)
        {
            if (primary.Kind is MonitorEventKind.NeedsInput or MonitorEventKind.Failed)
            {
                SystemSounds.Exclamation.Play();
            }
            else
            {
                SystemSounds.Asterisk.Play();
            }
        }
    }

    private static int Priority(MonitorEventKind kind)
    {
        return kind switch
        {
            MonitorEventKind.Failed => 0,
            MonitorEventKind.NeedsInput => 1,
            MonitorEventKind.Stale => 2,
            MonitorEventKind.Aborted => 3,
            _ => 4,
        };
    }

    private sealed record PendingAttentionReminder(
        CodexTaskSnapshot Task,
        DateTimeOffset NextReminderAt);
}
