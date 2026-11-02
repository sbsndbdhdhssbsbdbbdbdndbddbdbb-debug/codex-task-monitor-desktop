using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CodexTaskMonitor.App;

public partial class MainWindow : Window
{
    private readonly Action _openSettings;
    private readonly Action _toggleDesktopPet;
    private readonly Action _openPetPicker;
    private readonly Action _openMonitorStatus;
    private readonly Action _showDesktopPet;
    private string? _inspectedThreadId;

    public MainWindow(
        MainViewModel viewModel,
        Action openSettings,
        Action toggleDesktopPet,
        Action openPetPicker,
        Action openMonitorStatus,
        Action? showDesktopPet = null)
    {
        InitializeComponent();
        DataContext = viewModel;
        TrayIcon.LeftClickCommand = viewModel.ShowWindowCommand;
        _openSettings = openSettings;
        _toggleDesktopPet = toggleDesktopPet;
        _openPetPicker = openPetPicker;
        _openMonitorStatus = openMonitorStatus;
        _showDesktopPet = showDesktopPet ?? toggleDesktopPet;
        viewModel.PropertyChanged += OnViewModelChanged;
        Closed += (_, _) => viewModel.PropertyChanged -= OnViewModelChanged;
    }

    public bool AllowClose { get; set; }

    public void DisposeTray()
    {
        TrayIcon.Dispose();
    }

    public void InitializeTray()
    {
        TrayIcon.ForceCreate();
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

    private void OnSettingsClick(object sender, RoutedEventArgs e) => _openSettings();

    private void OnDiagnosticsClick(object sender, RoutedEventArgs e) => _openMonitorStatus();

    private void OnHideClick(object sender, RoutedEventArgs e) => Hide();

    private void OnTrayOpenClick(object sender, RoutedEventArgs e)
        => ((MainViewModel)DataContext).ShowWindowCommand.Execute(null);

    private void OnTrayPauseClick(object sender, RoutedEventArgs e)
        => ((MainViewModel)DataContext).TogglePauseCommand.Execute(null);

    private void OnTrayDoNotDisturbClick(object sender, RoutedEventArgs e)
        => ((MainViewModel)DataContext).ToggleDoNotDisturbCommand.Execute(null);

    private void OnTrayToggleDesktopPetClick(object sender, RoutedEventArgs e)
        => _toggleDesktopPet();

    private void OnPetPickerClick(object sender, RoutedEventArgs e) => _openPetPicker();

    private void OnShowPetClick(object sender, RoutedEventArgs e) => _showDesktopPet();

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.SelectedTask)) return;
        var threadId = ((MainViewModel)DataContext).SelectedTask?.ThreadId;
        if (threadId == _inspectedThreadId) return;
        _inspectedThreadId = threadId;
        InspectorRoot.ScrollToTop();
        if (threadId is not null && SystemParameters.ClientAreaAnimation)
            InspectorRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(0.65, 1, TimeSpan.FromMilliseconds(120)));
    }

    private void OnTaskDoubleClick(object sender, MouseButtonEventArgs e)
    {
        for (var element = e.OriginalSource as DependencyObject; element is not null;
             element = element is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(element))
            if (element is Button) return;
        if (((MainViewModel)DataContext).SelectedTask is { } task)
        {
            ((MainViewModel)DataContext).OpenTaskCommand.Execute(task);
            e.Handled = true;
        }
    }

    private void OnTaskKeyDown(object sender, KeyEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        if (e.Key == Key.Escape) { vm.ClearSelectionCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Enter && Keyboard.FocusedElement is not Button && vm.SelectedTask is { } task)
        {
            vm.OpenTaskCommand.Execute(task);
            e.Handled = true;
        }
    }

    private void OnTrayExitClick(object sender, RoutedEventArgs e)
        => ((MainViewModel)DataContext).ExitCommand.Execute(null);
}
