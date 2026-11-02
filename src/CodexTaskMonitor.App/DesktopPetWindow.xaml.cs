using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CodexTaskMonitor.App;

public partial class DesktopPetWindow : Window
{
    private readonly Action<double, double, bool> _saveState;
    private readonly DesktopPetViewModel _viewModel;
    private Point _petPointerDown;
    private bool _petPressed;
    private bool _isCollapsed;
    private bool _animationsEnabled = SystemParameters.ClientAreaAnimation;
    private bool _resizeQueued;

    public DesktopPetWindow(
        DesktopPetViewModel viewModel,
        double? left,
        double? top,
        bool alwaysOnTop,
        bool collapsed,
        Action<double, double, bool> saveState)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.CategoryTasks.CollectionChanged += OnCategoryTasksChanged;
        Left = left ?? double.NaN;
        Top = top ?? double.NaN;
        Topmost = alwaysOnTop;
        _isCollapsed = collapsed;
        _saveState = saveState;
    }

    public bool AllowClose { get; set; }

    public void RestorePet() => SetCollapsed(false);

    public void SetAnimationsEnabled(bool enabled)
    {
        _animationsEnabled = enabled && SystemParameters.ClientAreaAnimation;
        PixelPet.IsAnimationEnabled = _animationsEnabled;
    }

    public void ApplySettings(double? left, double? top, bool alwaysOnTop)
    {
        Topmost = alwaysOnTop;
        if (left is not null && top is not null)
        {
            Left = left.Value;
            Top = top.Value;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var area = GetCurrentScreenWorkArea();
        if (double.IsNaN(Left) || double.IsNaN(Top)
            || Left < area.Left
            || Top < area.Top
            || Left > area.Right - ActualWidth
            || Top > area.Bottom - ActualHeight)
        {
            var primaryArea = GetPrimaryScreenWorkArea();
            Left = primaryArea.Right - ActualWidth - 20;
            Top = primaryArea.Bottom - ActualHeight - 54;
        }

        UpdateHeight();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (AllowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (FindParent<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        DragMove();
        SnapAndSavePosition();
    }

    private void OnPetPointerDown(object sender, MouseButtonEventArgs e)
    {
        _petPointerDown = e.GetPosition(this);
        _petPressed = true;
        PetSurface.CaptureMouse();
        e.Handled = true;
    }

    private void OnPetPointerMove(object sender, MouseEventArgs e)
    {
        if (!_petPressed || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _petPointerDown.X) < 5 && Math.Abs(point.Y - _petPointerDown.Y) < 5)
        {
            return;
        }

        _petPressed = false;
        PetSurface.ReleaseMouseCapture();
        DragMove();
        SnapAndSavePosition();
    }

    private void OnPetPointerUp(object sender, MouseButtonEventArgs e)
    {
        if (!_petPressed)
        {
            return;
        }

        _petPressed = false;
        PetSurface.ReleaseMouseCapture();
        _viewModel.ToggleExpandedCommand.Execute(null);
        e.Handled = true;
    }

    private void OnCompactPointerDown(object sender, MouseButtonEventArgs e)
    {
        _petPointerDown = e.GetPosition(this);
        _petPressed = true;
        CompactOrb.CaptureMouse();
        e.Handled = true;
    }

    private void OnCompactPointerMove(object sender, MouseEventArgs e)
    {
        if (!_petPressed || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _petPointerDown.X) < 5 && Math.Abs(point.Y - _petPointerDown.Y) < 5)
        {
            return;
        }

        _petPressed = false;
        CompactOrb.ReleaseMouseCapture();
        DragMove();
        SnapCompactOrbToNearestEdge();
    }

    private void OnCompactPointerUp(object sender, MouseButtonEventArgs e)
    {
        if (!_petPressed)
        {
            e.Handled = true;
            return;
        }

        _petPressed = false;
        CompactOrb.ReleaseMouseCapture();
        SetCollapsed(false);
        e.Handled = true;
    }

    private void OnCompactClick(object sender, RoutedEventArgs e) => SetCollapsed(true);

    private void OnRestoreClick(object sender, RoutedEventArgs e) => SetCollapsed(false);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DesktopPetViewModel.IsExpanded)
            or nameof(DesktopPetViewModel.HasCategoryTasks))
        {
            UpdateHeight();
            if (e.PropertyName == nameof(DesktopPetViewModel.IsExpanded) && _viewModel.IsExpanded && _animationsEnabled)
            {
                TaskListPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.65, 1, TimeSpan.FromMilliseconds(150)));
                ((TranslateTransform)TaskListPanel.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(4, 0, TimeSpan.FromMilliseconds(150)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            }
        }

        if (e.PropertyName == nameof(DesktopPetViewModel.SelectedCategory))
        {
            TaskListScroller.ScrollToTop();
        }
    }

    private void OnCategoryTasksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is not NotifyCollectionChangedAction.Replace)
        {
            UpdateHeight();
        }
        else QueueExpandedResize();
    }

    private void UpdateHeight()
    {
        if (_isCollapsed)
        {
            PetLayout.RowDefinitions[1].Height = new GridLength(0);
            TaskListPanel.Visibility = Visibility.Collapsed;
            PetBody.Visibility = Visibility.Collapsed;
            CompactOrb.Visibility = Visibility.Visible;
            SetWindowSize(48, 48);
            return;
        }

        CompactOrb.Visibility = Visibility.Collapsed;
        PetLayout.RowDefinitions[1].Height = new GridLength(126);
        PetBody.Visibility = Visibility.Visible;
        TaskListPanel.Visibility = _viewModel.IsExpanded
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!_viewModel.IsExpanded)
        {
            SetWindowSize(126, 126);
            return;
        }

        ResizeExpandedWindow();
        QueueExpandedResize();
    }

    private void QueueExpandedResize()
    {
        if (_resizeQueued || !_viewModel.IsExpanded || _isCollapsed) return;
        _resizeQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _resizeQueued = false;
            ResizeExpandedWindow();
        });
    }

    private void ResizeExpandedWindow()
    {
        if (!_viewModel.IsExpanded || _isCollapsed)
        {
            return;
        }

        // The tray content changes with each category, so measure it instead of guessing from row count.
        TaskListPanel.Measure(new Size(404, MaxHeight - 132));
        var trayHeight = TaskListPanel.DesiredSize.Height;
        SetWindowSize(430, Math.Min(MaxHeight, Math.Ceiling(trayHeight) + 132));
    }

    private void SetWindowSize(double width, double height)
    {
        var positioned = IsLoaded && !double.IsNaN(Left) && !double.IsNaN(Top);
        var right = Left + Width;
        var bottom = Top + Height;
        var area = positioned ? GetCurrentScreenWorkArea() : Rect.Empty;
        Width = width;
        Height = height;
        if (positioned)
        {
            Left = Math.Clamp(right - width, area.Left, Math.Max(area.Left, area.Right - width));
            Top = Math.Clamp(bottom - height, area.Top, Math.Max(area.Top, area.Bottom - height));
        }
    }

    private void SnapAndSavePosition()
    {
        const double snapDistance = 24;
        var area = GetCurrentScreenWorkArea();
        Left = Math.Clamp(Left, area.Left, area.Right - Width);
        Top = Math.Clamp(Top, area.Top, area.Bottom - Height);
        if (Math.Abs(Left - area.Left) <= snapDistance) Left = area.Left;
        if (Math.Abs(Top - area.Top) <= snapDistance) Top = area.Top;
        if (Math.Abs(area.Right - (Left + Width)) <= snapDistance) Left = area.Right - Width;
        if (Math.Abs(area.Bottom - (Top + Height)) <= snapDistance) Top = area.Bottom - Height;
        _saveState(Left, Top, _isCollapsed);
    }

    private void SetCollapsed(bool collapsed)
    {
        if (_isCollapsed == collapsed)
        {
            return;
        }

        var right = Left + Width;
        var bottom = Top + Height;
        _isCollapsed = collapsed;
        if (collapsed)
        {
            _viewModel.IsExpanded = false;
        }

        UpdateHeight();
        Left = right - Width;
        Top = bottom - Height;

        if (collapsed)
        {
            SnapCompactOrbToNearestEdge();
        }
        else
        {
            SnapAndSavePosition();
        }
    }

    private void SnapCompactOrbToNearestEdge()
    {
        var area = GetCurrentScreenWorkArea();
        Left = Math.Clamp(Left, area.Left, area.Right - Width);
        Top = Math.Clamp(Top, area.Top, area.Bottom - Height);

        var distances = new[]
        {
            Math.Abs(Left - area.Left),
            Math.Abs(Top - area.Top),
            Math.Abs(area.Right - (Left + Width)),
            Math.Abs(area.Bottom - (Top + Height)),
        };
        switch (Array.IndexOf(distances, distances.Min()))
        {
            case 0: Left = area.Left; break;
            case 1: Top = area.Top; break;
            case 2: Left = area.Right - Width; break;
            default: Top = area.Bottom - Height; break;
        }

        _saveState(Left, Top, true);
    }

    private Rect GetCurrentScreenWorkArea()
    {
        if (double.IsNaN(Left) || double.IsNaN(Top))
        {
            return GetPrimaryScreenWorkArea();
        }

        var centerX = (int)Math.Round(Left + Width / 2);
        var centerY = (int)Math.Round(Top + Height / 2);
        return GetMonitorWorkArea(centerX, centerY);
    }

    private static Rect GetPrimaryScreenWorkArea()
        => GetMonitorWorkArea(0, 0);

    private static Rect GetMonitorWorkArea(int x, int y)
    {
        var monitor = MonitorFromPoint(new NativePoint(x, y), MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(monitor, ref info)
            ? new Rect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top)
            : SystemParameters.WorkArea;
    }

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint(int x, int y)
    {
        public readonly int X = x;
        public readonly int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    private static T? FindParent<T>(DependencyObject? element)
        where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T match)
            {
                return match;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }
}
