using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CodexTaskMonitor.Core;

namespace CodexTaskMonitor.App;

public sealed partial class MainViewModel : ObservableObject
{
    private const string AllProjects = "全部项目";
    private readonly CodexMonitorService _monitor;
    private readonly Action _showWindow;
    private readonly Action _exit;
    private readonly Action<bool> _setDoNotDisturb;
    private readonly Action<CodexTaskSnapshot> _openTask;
    private IReadOnlyList<CodexTaskSnapshot> _allTasks = [];

    public MainViewModel(CodexMonitorService monitor, Action showWindow, Action exit,
        bool doNotDisturbEnabled, Action<bool> setDoNotDisturb, Action<CodexTaskSnapshot>? openTask = null)
    {
        _monitor = monitor;
        _showWindow = showWindow;
        _exit = exit;
        _isDoNotDisturb = doNotDisturbEnabled;
        _setDoNotDisturb = setDoNotDisturb;
        _openTask = openTask ?? (task => DeepLinkLauncher.Open(task));
    }

    public ObservableCollection<TaskGroupViewModel> Tasks { get; } = [];
    public ObservableCollection<TaskGroupViewModel> RecentTasks { get; } = [];
    public ObservableCollection<string> Projects { get; } = [AllProjects];
    public IReadOnlyList<string> SortOptions { get; } = ["最近活动", "运行时长"];

    [ObservableProperty] private string _selectedView = "active";
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _isDoNotDisturb;
    [ObservableProperty] private int _runningCount;
    [ObservableProperty] private int _attentionCount;
    [ObservableProperty] private int _recentCount;
    [ObservableProperty] private string _lastUpdatedText = "正在连接 Codex";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _selectedProject = AllProjects;
    [ObservableProperty] private string _selectedSort = "最近活动";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasTaskDetails))] [NotifyPropertyChangedFor(nameof(ShowRecentTasks))]
    private TaskGroupViewModel? _selectedTask;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CompanionState))] [NotifyPropertyChangedFor(nameof(CompanionAnimationRow))]
    private PetPackage? _selectedPet;
    [ObservableProperty] private bool _petAnimationsEnabled;

    public string PauseButtonText => IsPaused ? "恢复监听" : "暂停监听";
    public string PauseGlyph => IsPaused ? "\uE768" : "\uE769";
    public string ListenerStatusText => IsPaused ? "监听已暂停" : "本机监听中";
    public Brush ListenerBrush => IsPaused ? Brushes.DimGray : Brushes.Black;
    public string DoNotDisturbButtonText => IsDoNotDisturb ? "勿扰中" : "开启勿扰";
    public bool HasFilters => !string.IsNullOrWhiteSpace(SearchText) || SelectedProject != AllProjects;
    public bool HasTaskDetails => SelectedTask is not null;
    public bool ShowRecentTasks => !HasTaskDetails;
    public string VisibleTaskCountText => $"{Tasks.Count} 项任务";
    public string ViewTitle => SelectedView switch { "attention" => "需要你处理", "recent" => "最近完成", _ => "正在进行" };
    public string EmptyTitle => HasFilters ? "没有找到匹配的任务" : SelectedView switch
    {
        "attention" => "没有需要处理的任务",
        "recent" => "最近 24 小时还没有完成的任务",
        _ => "这会儿，暂时没有任务在跑",
    };
    public string WorkspaceSummary => IsPaused ? "监听已暂停" : AttentionCount > 0 ? $"有 {AttentionCount} 个任务需要你处理"
        : RunningCount > 0 ? $"{RunningCount} 个任务正在进行" : "目前没有进行中的任务";
    public string WorkspaceDetail => IsPaused ? "当前显示暂停前的任务状态"
        : $"进行中 {RunningCount} · 需处理 {AttentionCount} · 最近 24 小时完成 {RecentCount}";
    private TaskDisplayCategory CompanionCategory => AttentionCount > 0 ? TaskDisplayCategory.NeedsAttention
        : RunningCount > 0 ? TaskDisplayCategory.InProgress : TaskDisplayCategory.Hidden;
    public PetSpriteState CompanionState => SelectedPet?.GetSpriteState(CompanionCategory) ?? PetSpriteState.Idle;
    public int CompanionAnimationRow => SelectedPet?.GetAnimationRow(CompanionCategory) ?? 0;

    public void SetCompanion(PetPackage package, bool animationsEnabled)
    {
        SelectedPet = package;
        PetAnimationsEnabled = animationsEnabled && SystemParameters.ClientAreaAnimation;
    }

    public void Refresh(IReadOnlyList<CodexTaskSnapshot>? tasks = null)
    {
        _allTasks = tasks ?? _monitor.GetTasks();
        var roots = _allTasks.Where(task => !task.IsSubagent).ToArray();
        RunningCount = roots.Count(task => task.DisplayCategory == TaskDisplayCategory.InProgress);
        AttentionCount = roots.Count(task => task.DisplayCategory == TaskDisplayCategory.NeedsAttention);
        RecentCount = roots.Count(IsRecent);
        var projectNames = roots.Select(task => task.Project).Where(project => !string.IsNullOrWhiteSpace(project))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(project => project, StringComparer.CurrentCultureIgnoreCase).ToArray();
        if (!Projects.SequenceEqual(projectNames.Prepend(AllProjects)))
        {
            Projects.Clear();
            Projects.Add(AllProjects);
            foreach (var project in projectNames) Projects.Add(project);
        }
        if (!Projects.Contains(SelectedProject)) SelectedProject = AllProjects;
        var children = _allTasks.Where(task => task.IsSubagent && task.ParentThreadId is not null)
            .GroupBy(task => task.ParentThreadId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(child => new TaskRowViewModel(child)).ToArray(), StringComparer.Ordinal);
        TaskGroupViewModel Group(CodexTaskSnapshot task) => new(task,
            children.TryGetValue(task.ThreadId, out var agents) ? agents : []);
        var matches = roots.Where(IsVisible);
        var visible = (SelectedSort == "运行时长"
            ? matches.OrderByDescending(task => task.DurationSeconds ?? 0).ThenByDescending(task => task.LastActivityAt)
            : matches.OrderByDescending(task => task.LastActivityAt)).Select(Group).ToArray();
        var selectedId = SelectedTask?.ThreadId;
        Reconcile(Tasks, visible);
        SelectedTask = Tasks.FirstOrDefault(task => task.ThreadId == selectedId);
        Reconcile(RecentTasks, roots.Where(IsRecent).OrderByDescending(task => task.CompletedAt ?? task.LastActivityAt).Take(3).Select(Group).ToArray());
        LastUpdatedText = IsPaused ? "监听已暂停" : _monitor.LastSuccessfulRefreshAt is { } updated
            ? $"扫描于 {updated.ToLocalTime():HH:mm:ss}" : "等待首次扫描";
        RefreshPresentation();
    }

    private static void Reconcile(ObservableCollection<TaskGroupViewModel> target, IReadOnlyList<TaskGroupViewModel> items)
    {
        for (var index = 0; index < items.Count; index++)
        {
            var next = items[index];
            var current = target.ToList().FindIndex(item => item.ThreadId == next.ThreadId);
            if (current < 0) target.Insert(index, next);
            else
            {
                if (current != index) target.Move(current, index);
                target[index].UpdateFrom(next);
            }
            target[index].RefreshTime();
            foreach (var child in target[index].Children) child.RefreshTime();
        }
        while (target.Count > items.Count) target.RemoveAt(target.Count - 1);
    }

    private void RefreshPresentation()
    {
        foreach (var name in new[] { nameof(WorkspaceSummary), nameof(WorkspaceDetail), nameof(CompanionState), nameof(CompanionAnimationRow),
                     nameof(HasFilters), nameof(EmptyTitle), nameof(ViewTitle), nameof(VisibleTaskCountText) }) OnPropertyChanged(name);
    }
    partial void OnSelectedViewChanged(string value) => Refresh(_allTasks);
    partial void OnSearchTextChanged(string value) => Refresh(_allTasks);
    partial void OnSelectedProjectChanged(string value) => Refresh(_allTasks);
    partial void OnSelectedSortChanged(string value) => Refresh(_allTasks);
    partial void OnIsPausedChanged(bool value)
    {
        foreach (var name in new[] { nameof(PauseButtonText), nameof(PauseGlyph), nameof(ListenerStatusText), nameof(ListenerBrush) }) OnPropertyChanged(name);
        RefreshPresentation();
    }
    partial void OnIsDoNotDisturbChanged(bool value) => OnPropertyChanged(nameof(DoNotDisturbButtonText));
    public void SetDoNotDisturb(bool enabled) => IsDoNotDisturb = enabled;

    [RelayCommand] private void SelectView(string? view) { if (view is "active" or "attention" or "recent") SelectedView = view; }
    [RelayCommand] private void ResetFilters() { SearchText = ""; SelectedProject = AllProjects; }
    [RelayCommand] private void ClearSelection() => SelectedTask = null;
    [RelayCommand] private void InspectRecent(TaskGroupViewModel? task)
    {
        if (task is null) return;
        ResetFilters();
        SelectedView = "recent";
        SelectedTask = Tasks.FirstOrDefault(item => item.ThreadId == task.ThreadId);
    }
    [RelayCommand] private void OpenTask(TaskRowViewModel? task) { if (task is not null) _openTask(task.Snapshot); }
    [RelayCommand] private void TogglePause()
    {
        IsPaused = !IsPaused;
        _monitor.SetPaused(IsPaused);
        LastUpdatedText = IsPaused ? "监听已暂停" : "监听已恢复";
    }
    [RelayCommand] private void ToggleDoNotDisturb() { IsDoNotDisturb = !IsDoNotDisturb; _setDoNotDisturb(IsDoNotDisturb); }
    [RelayCommand] private void ShowWindow() => _showWindow();
    [RelayCommand] private void Exit() => _exit();
    [RelayCommand] private async Task RefreshNow()
    {
        try { await _monitor.RefreshNowAsync(); Refresh(); }
        catch (Exception) { LastUpdatedText = "刷新未成功，请稍后重试"; }
    }

    private static bool IsRecent(CodexTaskSnapshot task) => task.DisplayCategory == TaskDisplayCategory.RecentlyCompleted
        && task.LastActivityAt >= DateTimeOffset.UtcNow.AddHours(-24);
    private bool IsVisible(CodexTaskSnapshot task)
    {
        var matchesView = SelectedView switch
        {
            "attention" => task.DisplayCategory == TaskDisplayCategory.NeedsAttention,
            "recent" => IsRecent(task),
            _ => task.DisplayCategory == TaskDisplayCategory.InProgress,
        };
        return matchesView && (SelectedProject == AllProjects || string.Equals(task.Project, SelectedProject, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(SearchText) || task.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || task.Project.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || task.Activity.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
    }
}

public class TaskRowViewModel(CodexTaskSnapshot snapshot) : ObservableObject
{
    public CodexTaskSnapshot Snapshot { get; private set; } = snapshot;
    public string ThreadId => Snapshot.ThreadId;
    public string Title => Snapshot.Title;
    public string Project => Snapshot.Project;
    public string Activity => Snapshot.Activity;
    public string DirectoryPath => Snapshot.Cwd;
    public CodexTaskStatus Status => Snapshot.Status;
    public TaskDisplayCategory DisplayCategory => Snapshot.DisplayCategory;
    public DateTimeOffset LastActivityAt => Snapshot.LastActivityAt;
    public double? DurationSeconds => Snapshot.DurationSeconds;
    public string DurationText => TaskPresentation.Duration(DurationSeconds);
    public string LastActivityText => TaskPresentation.RelativeTime(LastActivityAt);
    public string StartedAtText => Snapshot.StartedAt is { } time ? time.ToLocalTime().ToString("MM-dd HH:mm:ss") : "未记录";
    public string LastActivityClock => LastActivityAt == default ? "未记录" : LastActivityAt.ToLocalTime().ToString("MM-dd HH:mm:ss");
    public string StatusLabel => TaskPresentation.StatusLabel(Status);
    public string StatusGlyph => TaskPresentation.StatusGlyph(Status);
    public string AgentLabel => Snapshot.AgentNickname ?? Snapshot.AgentRole ?? "子任务";
    public void RefreshTime()
    {
        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(LastActivityText));
    }

    protected void UpdateSnapshot(CodexTaskSnapshot snapshot)
    {
        if (Snapshot == snapshot) return;
        Snapshot = snapshot;
        foreach (var name in new[] { nameof(Snapshot), nameof(Title), nameof(Project), nameof(Activity), nameof(DirectoryPath), nameof(Status),
                     nameof(DisplayCategory), nameof(LastActivityAt), nameof(StartedAtText), nameof(LastActivityClock), nameof(StatusLabel), nameof(StatusGlyph), nameof(AgentLabel) })
            OnPropertyChanged(name);
    }
}

public sealed class TaskGroupViewModel(CodexTaskSnapshot snapshot, IReadOnlyList<TaskRowViewModel> children) : TaskRowViewModel(snapshot)
{
    public IReadOnlyList<TaskRowViewModel> Children { get; private set; } = children;
    public bool HasChildren => Children.Count > 0;
    public string ChildrenSummary => $"{Children.Count} 个子任务";

    public void UpdateFrom(TaskGroupViewModel next)
    {
        UpdateSnapshot(next.Snapshot);
        if (!Children.Select(child => child.Snapshot).SequenceEqual(next.Children.Select(child => child.Snapshot)))
        {
            Children = next.Children;
            OnPropertyChanged(nameof(Children));
            OnPropertyChanged(nameof(HasChildren));
            OnPropertyChanged(nameof(ChildrenSummary));
        }
    }
}
