using System.Threading;
using System.Windows;
using System.Windows.Threading;
using CodexTaskMonitor.Core;

namespace CodexTaskMonitor.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private MonitorPaths? _paths;
    private SettingsStore? _settingsStore;
    private PetLibraryStore? _petLibraryStore;
    private IReadOnlyList<PetPackage> _petPackages = Array.Empty<PetPackage>();
    private MonitorSettings _settings = new();
    private CodexMonitorService? _monitor;
    private MonitorApiHost? _api;
    private MainWindow? _window;
    private PetPickerWindow? _petPicker;
    private bool _openingPetPicker;
    private MainViewModel? _viewModel;
    private DesktopPetWindow? _desktopPet;
    private DesktopPetViewModel? _desktopPetViewModel;
    private ToastManager? _toasts;
    private DispatcherTimer? _uiTimer;
    private bool _uiDirty = true;
    private bool _exiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(
            initiallyOwned: true,
            @"Local\CodexTaskMonitor.SingleInstance",
            out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Codex Task Monitor 已在运行。",
                "Codex Task Monitor",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            _paths = MonitorPaths.ForCurrentUser();
            AppLog.Initialize(_paths.LogsDirectory);
            _settingsStore = new SettingsStore(_paths.SettingsFile);
            _settings = await _settingsStore.LoadAsync();
            _petLibraryStore = new PetLibraryStore(PetLibraryPaths.ResolvePetsRoot());
            _petPackages = await PetPackageRegistry.LoadAllAsync(_petLibraryStore);
            if (e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase))
            {
                _settings = _settings with { StartMinimized = true };
            }

            var database = new MonitorDatabase(_paths.MonitorDatabase);
            _monitor = new CodexMonitorService(_paths, database, _settings);
            _viewModel = new MainViewModel(
                _monitor,
                ShowMainWindow,
                RequestExit,
                _settings.DoNotDisturbEnabled,
                SetDoNotDisturb);
            _viewModel.SetCompanion(PetPackageRegistry.Get(_petPackages, _settings.DesktopPetPackageId), _settings.DesktopPetAnimationsEnabled);
            _window = new MainWindow(_viewModel, OpenSettings, ToggleDesktopPet, OpenPetPicker, OpenMonitorStatus, ShowDesktopPet);
            MainWindow = _window;
            _desktopPetViewModel = new DesktopPetViewModel(
                ShowMainWindow,
                task => DeepLinkLauncher.Open(task),
                PetPackageRegistry.Get(_petPackages, _settings.DesktopPetPackageId),
                _settings.AcknowledgedRecentTasks,
                AcknowledgeRecentTask,
                _settings.DoNotDisturbEnabled,
                SetDoNotDisturb);
            _desktopPet = new DesktopPetWindow(
                _desktopPetViewModel,
                _settings.DesktopPetLeft,
                _settings.DesktopPetTop,
                _settings.DesktopPetAlwaysOnTop,
                _settings.DesktopPetCollapsed,
                SaveDesktopPetState);
            _desktopPet.SetAnimationsEnabled(_settings.DesktopPetAnimationsEnabled);
            _toasts = CreateToastManager();
            _monitor.TaskEvent += OnTaskEvent;
            _window.InitializeTray();

            if (!_settings.StartMinimized)
            {
                _window.Show();
            }

            if (_settings.DesktopPetEnabled)
            {
                _desktopPet.Show();
            }

            _uiTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1),
            };
            _uiTimer.Tick += (_, _) =>
            {
                if (_monitor is null)
                {
                    return;
                }

                if (_uiDirty || _window.IsVisible || _desktopPet?.IsVisible == true)
                {
                    var tasks = _monitor.GetTasks();
                    _viewModel.Refresh(tasks);
                    _desktopPetViewModel?.SetMonitoringState(_viewModel.IsPaused, _viewModel.IsDoNotDisturb);
                    _desktopPetViewModel?.Refresh(tasks);
                    _uiDirty = false;
                }
            };
            _uiTimer.Start();

            await _monitor.StartAsync();
            var initialTasks = _monitor.GetTasks();
            _viewModel.Refresh(initialTasks);
            _desktopPetViewModel.Refresh(initialTasks);
            _toasts?.RefreshTasks(initialTasks);

            _api = new MonitorApiHost(_monitor, _paths, _settings);
            try
            {
                await _api.StartAsync();
            }
            catch (Exception exception)
            {
                await AppLog.WriteAsync("ERROR", "Pet API failed to start", exception);
            }

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            await AppLog.WriteAsync("INFO", "Codex Task Monitor started");
        }
        catch (Exception exception)
        {
            await AppLog.WriteAsync("FATAL", "Application startup failed", exception);
            MessageBox.Show(
                $"启动失败：{exception.Message}",
                "Codex Task Monitor",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            await DisposeServicesAsync();
            Shutdown(1);
        }
    }

    private void OnTaskEvent(object? sender, MonitorEvent monitorEvent)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _uiDirty = true;
            _toasts?.Handle(monitorEvent);
        });
    }

    private ToastManager CreateToastManager()
    {
        return new ToastManager(
            _settings,
            () => _window?.IsActive == true,
            ShowMainWindow);
    }

    private void ShowMainWindow()
    {
        if (_window is null)
        {
            return;
        }

        if (!_window.IsVisible)
        {
            _window.Show();
        }

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }

    private void OpenMonitorStatus()
    {
        if (_monitor is null || _paths is null)
        {
            return;
        }

        var lastScan = _monitor.LastSuccessfulRefreshAt?.ToLocalTime().ToString("HH:mm:ss")
                       ?? "尚未完成首次检查";
        var hasError = !string.IsNullOrWhiteSpace(_monitor.LastRefreshError);
        var message = hasError
            ? "监控暂时遇到读取问题，任务列表可能不是最新的。\n"
              + "请稍后点击刷新；若持续出现，请重启应用后再试。"
            : "监控运行正常，正在持续检查 Codex 任务变化。";

        MessageBox.Show(
            _window,
            $"{message}\n\n"
            + $"正在跟踪 {_monitor.GetTasks().Count} 个任务\n"
            + $"上次检查：{lastScan}",
            hasError ? "监控需要注意" : "监控状态正常",
            MessageBoxButton.OK,
            hasError ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private async void OpenSettings()
    {
        if (_window is null || _settingsStore is null)
        {
            return;
        }

        var dialog = new SettingsWindow(_settings) { Owner = _window };
        if (dialog.ShowDialog() != true || dialog.Result is null)
        {
            return;
        }

        var previous = _settings;
        _settings = dialog.Result;
        await _settingsStore.SaveAsync(_settings);
        try
        {
            StartupRegistration.SetEnabled(_settings.AutoStart);
        }
        catch (Exception exception)
        {
            await AppLog.WriteAsync("WARN", "Unable to update startup registration", exception);
        }

        _toasts = CreateToastManager();
        _viewModel?.SetDoNotDisturb(_settings.DoNotDisturbEnabled);
        ApplyDesktopPetSettings();
        if (previous.ApiPort != _settings.ApiPort
            || previous.PetApiEnabled != _settings.PetApiEnabled)
        {
            MessageBox.Show(
                _window,
                "桌宠 API 的端口与启用状态会在下次启动时生效。",
                "设置已保存",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private async void RequestExit()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _uiTimer?.Stop();
        if (_window is not null)
        {
            _window.AllowClose = true;
            _window.DisposeTray();
            _window.Close();
        }

        if (_desktopPet is not null)
        {
            _desktopPet.AllowClose = true;
            _desktopPet.Close();
        }

        await DisposeServicesAsync();
        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        Shutdown();
    }

    private async Task DisposeServicesAsync()
    {
        if (_monitor is not null)
        {
            _monitor.TaskEvent -= OnTaskEvent;
        }

        if (_api is not null)
        {
            await _api.DisposeAsync();
            _api = null;
        }

        if (_monitor is not null)
        {
            await _monitor.DisposeAsync();
            _monitor = null;
        }
    }

    private void ToggleDesktopPet()
    {
        if (_desktopPet is null)
        {
            return;
        }

        if (_desktopPet.IsVisible)
        {
            _desktopPet.Hide();
        }
        else
        {
            _desktopPet.Show();
        }
    }

    private async void OpenPetPicker()
    {
        if (_window is null || _settingsStore is null || _petLibraryStore is null)
        {
            return;
        }

        var petLibraryStore = _petLibraryStore!;
        if (_petPicker is not null) { _petPicker.Activate(); return; }
        if (_openingPetPicker) return;
        _openingPetPicker = true;
        try
        {
            _petPackages = await Task.Run(() => PetPackageRegistry.LoadAllAsync(petLibraryStore));
            _petPicker = new PetPickerWindow(_petPackages, _settings.DesktopPetPackageId, petLibraryStore, ApplyPetAsync) { Owner = _window };
            _petPicker.ShowDialog();
        }
        catch (Exception exception)
        {
            await AppLog.WriteAsync("ERROR", "Unable to open pet library", exception);
            MessageBox.Show("桌宠库暂时无法打开，请稍后重试。", "桌宠库");
        }
        finally { _petPicker = null; _openingPetPicker = false; }
    }

    private async Task ApplyPetAsync(PetPackage package)
    {
        if (_settingsStore is null) return;
        _settings = _settings with { DesktopPetPackageId = package.Id, DesktopPetEnabled = true };
        await _settingsStore.SaveAsync(_settings);
        _desktopPetViewModel?.SelectPet(package);
        _viewModel?.SetCompanion(package, _settings.DesktopPetAnimationsEnabled);
        if (_desktopPet is not null)
        {
            _desktopPet.Show();
            _desktopPet.RestorePet();
        }
    }

    private void ApplyDesktopPetSettings()
    {
        if (_desktopPet is null)
        {
            return;
        }

        _desktopPet.ApplySettings(
            _settings.DesktopPetLeft,
            _settings.DesktopPetTop,
            _settings.DesktopPetAlwaysOnTop);
        _desktopPetViewModel?.SelectPet(PetPackageRegistry.Get(_petPackages, _settings.DesktopPetPackageId));
        _viewModel?.SetCompanion(PetPackageRegistry.Get(_petPackages, _settings.DesktopPetPackageId), _settings.DesktopPetAnimationsEnabled);
        _desktopPetViewModel?.SetMonitoringState(_viewModel?.IsPaused ?? false, _settings.DoNotDisturbEnabled);
        _desktopPet.SetAnimationsEnabled(_settings.DesktopPetAnimationsEnabled);
        if (_settings.DesktopPetEnabled)
        {
            if (!_desktopPet.IsVisible)
            {
                _desktopPet.Show();
            }
        }
        else
        {
            _desktopPet.Hide();
        }
    }

    private async void SaveDesktopPetState(double left, double top, bool collapsed)
    {
        if (_settingsStore is null)
        {
            return;
        }

        _settings = _settings with
        {
            DesktopPetLeft = left,
            DesktopPetTop = top,
            DesktopPetCollapsed = collapsed,
        };
        await _settingsStore.SaveAsync(_settings);
    }

    private async void SetDoNotDisturb(bool enabled)
    {
        _viewModel?.SetDoNotDisturb(enabled);
        _desktopPetViewModel?.SetMonitoringState(_viewModel?.IsPaused ?? false, enabled);
        if (_settingsStore is null || _settings.DoNotDisturbEnabled == enabled)
        {
            return;
        }

        _settings = _settings with { DoNotDisturbEnabled = enabled };
        _toasts?.DismissCurrent();
        _toasts = CreateToastManager();
        await _settingsStore.SaveAsync(_settings);
    }

    private async void ShowDesktopPet()
    {
        if (_desktopPet is null || _settingsStore is null) return;
        _settings = _settings with { DesktopPetEnabled = true };
        _desktopPet.Show();
        _desktopPet.RestorePet();
        await _settingsStore.SaveAsync(_settings);
    }

    private async void AcknowledgeRecentTask(RecentTaskAcknowledgement acknowledgement)
    {
        if (_settingsStore is null)
        {
            return;
        }

        var cutoff = DateTimeOffset.UtcNow.AddHours(-_settings.RecentCompletionHours);
        var acknowledgements = _settings.AcknowledgedRecentTasks
            .Where(item => item.CompletedAt >= cutoff && item != acknowledgement)
            .Append(acknowledgement)
            .ToArray();
        _settings = _settings with { AcknowledgedRecentTasks = acknowledgements };
        await _settingsStore.SaveAsync(_settings);
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        _ = AppLog.WriteAsync("ERROR", "Unhandled UI exception", e.Exception);
        e.Handled = true;
        MessageBox.Show(
            _window,
            $"出现了一个错误：{e.Exception.Message}",
            "Codex Task Monitor",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
