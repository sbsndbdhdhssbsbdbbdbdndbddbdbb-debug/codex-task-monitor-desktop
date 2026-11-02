using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CodexTaskMonitor.Core;

namespace CodexTaskMonitor.App;

public sealed partial class DesktopPetViewModel : ObservableObject
{
    private readonly Action _showTaskCenter;
    private readonly Action<CodexTaskSnapshot> _openTask;
    private readonly Action<RecentTaskAcknowledgement> _acknowledgeRecentTask;
    private readonly Action<bool>? _setDoNotDisturb;
    private readonly HashSet<RecentTaskAcknowledgement> _acknowledgedRecentTasks;
    private IReadOnlyList<CodexTaskSnapshot> _rootTasks = Array.Empty<CodexTaskSnapshot>();
    private CodexTaskSnapshot? _primaryTask;

    public DesktopPetViewModel(
        Action showTaskCenter,
        Action<CodexTaskSnapshot> openTask,
        PetPackage selectedPet,
        IEnumerable<RecentTaskAcknowledgement> acknowledgedRecentTasks,
        Action<RecentTaskAcknowledgement> acknowledgeRecentTask,
        bool doNotDisturbEnabled = false,
        Action<bool>? setDoNotDisturb = null)
    {
        _showTaskCenter = showTaskCenter;
        _openTask = openTask;
        _selectedPet = selectedPet;
        _acknowledgedRecentTasks = new HashSet<RecentTaskAcknowledgement>(acknowledgedRecentTasks);
        _acknowledgeRecentTask = acknowledgeRecentTask;
        _isDoNotDisturb = doNotDisturbEnabled;
        _setDoNotDisturb = setDoNotDisturb;
    }

    public ObservableCollection<DesktopPetTaskItem> CategoryTasks { get; } = [];

    [ObservableProperty]
    private string _face = "...";

    [ObservableProperty]
    private string _headline = "正在连接 Codex";

    [ObservableProperty]
    private string _detail = "等待任务状态…";

    [ObservableProperty]
    private string _summary = "0 个进行中";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpriteState))]
    [NotifyPropertyChangedFor(nameof(SpriteAnimationRow))]
    private CodexTaskStatus _status = CodexTaskStatus.Idle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpriteState))]
    [NotifyPropertyChangedFor(nameof(SpriteAnimationRow))]
    private PetPackage _selectedPet = null!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandGlyph))]
    private bool _isExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyTitle))]
    private string _selectedCategory = "active";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompactCount))]
    private int _runningCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompactCount))]
    private int _attentionCount;

    [ObservableProperty]
    private int _recentCount;

    [ObservableProperty]
    private bool _hasCategoryTasks;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FooterText))]
    [NotifyPropertyChangedFor(nameof(DoNotDisturbTooltip))]
    private bool _isDoNotDisturb;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FooterText))]
    private bool _isPaused;

    public string DoNotDisturbTooltip => IsDoNotDisturb ? "关闭勿扰" : "开启勿扰";
    public string FooterText => IsPaused ? "监听已暂停 · 保留上次状态"
        : IsDoNotDisturb ? "勿扰中 · 任务继续监听" : $"{CategoryTasks.Count} 项{CategoryName} · 本机监听中";
    private string CategoryName => SelectedCategory switch { "attention" => "需处理", "recent" => "最近完成", _ => "进行中" };

    public void SetMonitoringState(bool paused, bool doNotDisturb)
    {
        IsPaused = paused;
        IsDoNotDisturb = doNotDisturb;
    }

    [RelayCommand]
    private void ToggleDoNotDisturb()
    {
        IsDoNotDisturb = !IsDoNotDisturb;
        _setDoNotDisturb?.Invoke(IsDoNotDisturb);
    }

    public string ExpandGlyph => IsExpanded ? "\uE70E" : "\uE70D";

    public int CompactCount => AttentionCount > 0 ? AttentionCount : RunningCount;

    public string EmptyTitle => SelectedCategory switch
    {
        "attention" => "没有需要处理的任务",
        "recent" => "还没有最近完成的任务",
        _ => "目前没有进行中的任务",
    };

    public PetSpriteState SpriteState => SelectedPet.GetSpriteState(Status.ToDisplayCategory());

    public int SpriteAnimationRow => SelectedPet.GetAnimationRow(Status.ToDisplayCategory());

    public void SelectPet(PetPackage pet) => SelectedPet = pet;

    public void Refresh(IReadOnlyList<CodexTaskSnapshot> tasks)
    {
        _rootTasks = tasks.Where(task => !task.IsSubagent).ToArray();
        RunningCount = _rootTasks.Count(task => task.DisplayCategory == TaskDisplayCategory.InProgress);
        AttentionCount = _rootTasks.Count(task => task.DisplayCategory == TaskDisplayCategory.NeedsAttention);
        RefreshRecentCount();

        Summary = AttentionCount > 0
            ? $"{AttentionCount} 个需要你处理 · {RunningCount} 个进行中"
            : RunningCount > 0
                ? $"{RunningCount} 个任务正在进行"
                : RecentCount > 0
                    ? $"今天有 {RecentCount} 个最近完成"
                    : "暂时没有进行中的任务";

        var priorityTasks = _rootTasks
            .Where(task => task.DisplayCategory is TaskDisplayCategory.NeedsAttention
                or TaskDisplayCategory.InProgress)
            .OrderBy(task => Priority(task.Status))
            .ThenByDescending(task => task.LastActivityAt)
            .ToArray();
        _primaryTask = priorityTasks.FirstOrDefault()
            ?? _rootTasks.Where(IsVisibleRecentTask)
                .OrderBy(task => Priority(task.Status))
                .ThenByDescending(task => task.LastActivityAt)
                .FirstOrDefault();
        RefreshCategoryTasks();

        if (_primaryTask is null)
        {
            Status = CodexTaskStatus.Idle;
            Face = "...";
            Headline = "Codex 在等你";
            Detail = "点击查看任务中心";
            return;
        }

        Status = _primaryTask.Status;
        Face = _primaryTask.Status switch
        {
            CodexTaskStatus.NeedsInput => "?",
            CodexTaskStatus.Failed => "!",
            CodexTaskStatus.Stale => "~",
            CodexTaskStatus.Running => "o",
            CodexTaskStatus.Completed => "^",
            CodexTaskStatus.Aborted => "-",
            _ => "...",
        };
        Headline = _primaryTask.Title;
        Detail = _primaryTask.Status == CodexTaskStatus.Running
            ? $"{_primaryTask.Activity} · {FormatDuration(_primaryTask.DurationSeconds)}"
            : _primaryTask.Activity;
    }

    partial void OnSelectedCategoryChanged(string value) => RefreshCategoryTasks();

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void SelectCategory(string? category)
    {
        if (category is "active" or "attention" or "recent")
        {
            SelectedCategory = category;
        }
    }

    [RelayCommand]
    private void ShowTaskCenter() => _showTaskCenter();

    [RelayCommand]
    private void OpenPrimaryTask()
    {
        if (_primaryTask is not null)
        {
            _openTask(_primaryTask);
        }
    }

    [RelayCommand]
    private void OpenTask(DesktopPetTaskItem? task)
    {
        if (task is not null)
        {
            if (SelectedCategory == "recent" && IsRecentTask(task.Snapshot))
            {
                var acknowledgement = ToAcknowledgement(task.Snapshot);
                if (_acknowledgedRecentTasks.Add(acknowledgement))
                {
                    _acknowledgeRecentTask(acknowledgement);
                    RefreshRecentCount();
                    RefreshCategoryTasks();
                }
            }

            _openTask(task.Snapshot);
        }
    }

    private void RefreshCategoryTasks()
    {
        var selectedTasks = SelectedCategory switch
        {
            "attention" => _rootTasks.Where(task => task.DisplayCategory == TaskDisplayCategory.NeedsAttention),
            "recent" => _rootTasks.Where(IsVisibleRecentTask),
            _ => _rootTasks.Where(task => task.DisplayCategory == TaskDisplayCategory.InProgress),
        };
        var taskItems = selectedTasks
            .OrderBy(task => Priority(task.Status))
            .ThenByDescending(task => task.LastActivityAt)
            .Select(task => new DesktopPetTaskItem(task))
            .ToArray();

        HasCategoryTasks = taskItems.Length > 0;
        if (CategoryTasks.Select(item => item.Snapshot)
            .SequenceEqual(taskItems.Select(item => item.Snapshot)))
        {
            foreach (var item in CategoryTasks) item.RefreshTime();
            OnPropertyChanged(nameof(FooterText));
            return;
        }

        if (CategoryTasks.Count == taskItems.Length
            && CategoryTasks.Select(item => item.Snapshot.ThreadId)
                .SequenceEqual(taskItems.Select(item => item.Snapshot.ThreadId)))
        {
            for (var index = 0; index < taskItems.Length; index++)
            {
                CategoryTasks[index] = taskItems[index];
            }
            OnPropertyChanged(nameof(FooterText));
            return;
        }

        CategoryTasks.Clear();
        foreach (var task in taskItems)
        {
            CategoryTasks.Add(task);
        }
        OnPropertyChanged(nameof(FooterText));
    }

    private static bool IsRecentTask(CodexTaskSnapshot task) => task.DisplayCategory == TaskDisplayCategory.RecentlyCompleted
        && task.LastActivityAt >= DateTimeOffset.UtcNow.AddHours(-24);

    private bool IsVisibleRecentTask(CodexTaskSnapshot task) => IsRecentTask(task)
        && !_acknowledgedRecentTasks.Contains(ToAcknowledgement(task));

    private void RefreshRecentCount() => RecentCount = _rootTasks.Count(IsVisibleRecentTask);

    private static RecentTaskAcknowledgement ToAcknowledgement(CodexTaskSnapshot task)
        => new(task.ThreadId, task.LastActivityAt);

    private static int Priority(CodexTaskStatus status)
    {
        return status switch
        {
            CodexTaskStatus.Failed => 0,
            CodexTaskStatus.NeedsInput => 1,
            CodexTaskStatus.Stale => 2,
            CodexTaskStatus.Running => 3,
            CodexTaskStatus.Completed => 4,
            CodexTaskStatus.Aborted => 5,
            _ => 6,
        };
    }

    private static string FormatDuration(double? seconds)
    {
        if (seconds is null)
        {
            return "刚刚开始";
        }

        var duration = TimeSpan.FromSeconds(seconds.Value);
        return duration.TotalHours >= 1
            ? $"已运行 {(int)duration.TotalHours} 小时 {duration.Minutes} 分"
            : duration.TotalMinutes >= 1
                ? $"已运行 {(int)duration.TotalMinutes} 分"
                : "刚刚开始";
    }
}

public sealed class DesktopPetTaskItem(CodexTaskSnapshot snapshot) : ObservableObject
{
    public CodexTaskSnapshot Snapshot { get; } = snapshot;
    public string Title => Snapshot.Title;
    public string Activity => Snapshot.Status == CodexTaskStatus.Running
        ? Snapshot.Activity
        : Snapshot.Status switch
        {
            CodexTaskStatus.Failed => "任务失败，需要查看",
            CodexTaskStatus.NeedsInput => "等待你的回复",
            CodexTaskStatus.Stale => "等待新的活动",
            CodexTaskStatus.Completed => "任务已完成",
            CodexTaskStatus.Aborted => "任务已中止",
            _ => Snapshot.Activity,
        };

    public CodexTaskStatus Status => Snapshot.Status;

    public string SecondaryText => Status == CodexTaskStatus.Completed ? Snapshot.Project : Activity;

    public string StatusLabel => TaskPresentation.StatusLabel(Status);
    public string StatusGlyph => TaskPresentation.StatusGlyph(Status);
    public string TimeText => Status == CodexTaskStatus.Running
        ? Snapshot.DurationSeconds is { } seconds ? $"已运行 {TaskPresentation.Duration(seconds)}" : "刚刚开始"
        : TaskPresentation.RelativeTime(Snapshot.CompletedAt ?? Snapshot.LastActivityAt);
    public string FullDescription => $"{Title}\n{Snapshot.Project}\n{StatusLabel} · {Activity}\n{TimeText}";
    public void RefreshTime()
    {
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(FullDescription));
    }
}
