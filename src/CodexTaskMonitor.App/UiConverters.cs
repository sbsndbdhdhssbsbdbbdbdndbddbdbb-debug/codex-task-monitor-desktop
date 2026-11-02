using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CodexTaskMonitor.Core;

namespace CodexTaskMonitor.App;

public sealed class StatusTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is CodexTaskStatus status ? status switch
        {
            CodexTaskStatus.Running => "运行中",
            CodexTaskStatus.NeedsInput => "需要你处理",
            CodexTaskStatus.Completed => "已完成",
            CodexTaskStatus.Aborted => "已中止",
            CodexTaskStatus.Failed => "失败",
            CodexTaskStatus.Stale => "可能卡住",
            _ => "空闲",
        } : "未知";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return new SolidColorBrush(value is CodexTaskStatus status ? status switch
        {
            CodexTaskStatus.Running => Color.FromRgb(26, 119, 90),
            CodexTaskStatus.NeedsInput => Color.FromRgb(190, 117, 20),
            CodexTaskStatus.Completed => Color.FromRgb(66, 86, 78),
            CodexTaskStatus.Aborted => Color.FromRgb(105, 112, 116),
            CodexTaskStatus.Failed => Color.FromRgb(190, 61, 55),
            CodexTaskStatus.Stale => Color.FromRgb(153, 91, 45),
            _ => Color.FromRgb(105, 112, 116),
        } : Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class FloatingStatusBrushConverter : IValueConverter
{
    private static readonly Brush Ink = FrozenBrush(36, 36, 36);
    private static readonly Brush Muted = FrozenBrush(96, 96, 96);
    private static readonly Brush Attention = FrozenBrush(146, 86, 8);
    private static readonly Brush Failure = FrozenBrush(165, 42, 40);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        CodexTaskStatus.Running => Ink,
        CodexTaskStatus.NeedsInput or CodexTaskStatus.Stale => Attention,
        CodexTaskStatus.Failed => Failure,
        _ => Muted,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;

    private static Brush FrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}

public sealed class BoolVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class InverseBoolVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class ZeroVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class PositiveVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class SelectedViewConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value as string, parameter as string, StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class DurationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double seconds)
        {
            return "尚未开始";
        }

        var duration = TimeSpan.FromSeconds(seconds);
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}小时 {duration.Minutes}分"
            : duration.TotalMinutes >= 1
                ? $"{(int)duration.TotalMinutes}分 {duration.Seconds}秒"
                : $"{Math.Max(1, duration.Seconds)}秒";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
