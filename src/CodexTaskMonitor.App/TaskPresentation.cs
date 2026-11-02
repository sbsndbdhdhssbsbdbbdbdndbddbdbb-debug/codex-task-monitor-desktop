using CodexTaskMonitor.Core;

namespace CodexTaskMonitor.App;

public static class TaskPresentation
{
    public static string RelativeTime(DateTimeOffset timestamp)
    {
        if (timestamp == default) return "时间未记录";
        var elapsed = DateTimeOffset.UtcNow - timestamp;
        return elapsed < TimeSpan.FromMinutes(1) ? "刚刚"
            : elapsed < TimeSpan.FromHours(1) ? $"{(int)elapsed.TotalMinutes} 分钟前"
            : elapsed < TimeSpan.FromDays(1) ? $"{(int)elapsed.TotalHours} 小时前"
            : $"{(int)elapsed.TotalDays} 天前";
    }

    public static string Duration(double? seconds)
    {
        if (seconds is null) return "未记录";
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds.Value));
        return duration.TotalHours >= 1 ? $"{(int)duration.TotalHours}小时{duration.Minutes}分"
            : duration.TotalMinutes >= 1 ? $"{(int)duration.TotalMinutes}分{duration.Seconds:D2}秒"
            : $"{(int)duration.TotalSeconds}秒";
    }

    public static string StatusLabel(CodexTaskStatus status) => status switch
    {
        CodexTaskStatus.Running => "进行中",
        CodexTaskStatus.Completed => "已完成",
        CodexTaskStatus.NeedsInput or CodexTaskStatus.Failed => "需处理",
        CodexTaskStatus.Aborted => "已中止",
        _ => "等待中",
    };

    public static string StatusGlyph(CodexTaskStatus status) => status switch
    {
        CodexTaskStatus.Completed => "\uE73E",
        CodexTaskStatus.Running => "\uE895",
        CodexTaskStatus.NeedsInput => "\uE8F2",
        CodexTaskStatus.Failed => "\uE783",
        _ => "\uE8A5",
    };
}
