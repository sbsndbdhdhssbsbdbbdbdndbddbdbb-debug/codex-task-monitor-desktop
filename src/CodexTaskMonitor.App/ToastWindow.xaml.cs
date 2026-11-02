using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodexTaskMonitor.Core;

namespace CodexTaskMonitor.App;

public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromSeconds(7),
    };
    private Action? _clicked;

    public ToastWindow()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Close();
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 18;
            Top = area.Bottom - ActualHeight - 18;
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    public void Configure(
        MonitorEvent monitorEvent,
        bool privacyMode,
        int aggregateCount,
        Action clicked)
    {
        _clicked = clicked;
        KindText.Text = monitorEvent.Kind switch
        {
            MonitorEventKind.NeedsInput => "CODEX 需要你处理",
            MonitorEventKind.Completed => "CODEX 已完成",
            MonitorEventKind.Aborted => "CODEX 已中止",
            MonitorEventKind.Failed => "CODEX 执行失败",
            MonitorEventKind.Stale => "CODEX 可能卡住",
            _ => "CODEX 任务更新",
        };
        TitleText.Text = privacyMode ? "Codex 任务" : monitorEvent.Task.Title;
        ActivityText.Text = privacyMode
            ? monitorEvent.Task.Status switch
            {
                CodexTaskStatus.NeedsInput => "等待你的输入",
                CodexTaskStatus.Completed => "任务已经完成",
                CodexTaskStatus.Aborted => "任务已经中止",
                CodexTaskStatus.Failed => "任务执行失败",
                CodexTaskStatus.Stale => "长时间没有新活动",
                _ => "Codex 任务已更新",
            }
            : monitorEvent.Task.Activity;
        CountText.Text = aggregateCount > 1 ? $"{aggregateCount} 个更新" : monitorEvent.Task.Project;
        AccentBar.Background = new SolidColorBrush(monitorEvent.Kind switch
        {
            MonitorEventKind.NeedsInput => Color.FromRgb(203, 128, 25),
            MonitorEventKind.Failed => Color.FromRgb(190, 61, 55),
            MonitorEventKind.Aborted => Color.FromRgb(105, 112, 116),
            MonitorEventKind.Stale => Color.FromRgb(153, 91, 45),
            _ => Color.FromRgb(26, 119, 90),
        });
    }

    private void OnMouseEnter(object sender, MouseEventArgs e) => _timer.Stop();

    private void OnMouseLeave(object sender, MouseEventArgs e) => _timer.Start();

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _clicked?.Invoke();
        Close();
    }
}
