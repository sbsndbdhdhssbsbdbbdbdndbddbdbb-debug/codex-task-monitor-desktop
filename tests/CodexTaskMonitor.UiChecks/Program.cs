using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CodexTaskMonitor.App;
using CodexTaskMonitor.Core;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources = new ResourceDictionary { Source = new Uri("pack://application:,,,/CodexTaskMonitor;component/Themes/MonitorTheme.xaml") };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        _ = Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
        {
            try
            {
                if (args.Contains("--petdex-live")) await RunLiveCatalogAsync(Path.GetFullPath(args[0]));
                else if (args.Contains("--floating")) await RunFloatingAsync(Path.GetFullPath(args[0]));
                else await RunAsync(Path.GetFullPath(args[0]), args.Contains("--main"));
            }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
            finally { Environment.Exit(Environment.ExitCode); }
        });
        Dispatcher.Run();
    }

    private static async Task RunAsync(string root, bool mainOnly)
    {
        var output = Path.Combine(root, "artifacts", "ui-review");
        Directory.CreateDirectory(output);
        var fixture = Path.Combine(output, "fixtures", Guid.NewGuid().ToString("N"));
        var store = new PetLibraryStore(Path.Combine(fixture, "Pets"));
        var packages = PetPackageRegistry.All;
        var paths = new MonitorPaths(fixture, fixture, fixture, Path.Combine(fixture, "db"), "", "", "", fixture);
        await using var monitor = new CodexMonitorService(paths, new MonitorDatabase(paths.MonitorDatabase), new());
        var openedTasks = new List<string>();
        var shownPet = 0;
        var openedPicker = 0;
        var savedDoNotDisturb = false;
        MainViewModel? sharedMain = null;
        DesktopPetViewModel? sharedFloating = null;
        void SyncDoNotDisturb(bool enabled)
        {
            savedDoNotDisturb = enabled;
            sharedMain?.SetDoNotDisturb(enabled);
            sharedFloating?.SetMonitoringState(sharedMain?.IsPaused ?? false, enabled);
        }
        var vm = new MainViewModel(monitor, () => { }, () => { }, false, SyncDoNotDisturb, task => openedTasks.Add(task.ThreadId));
        sharedMain = vm;
        vm.SetCompanion(PetPackageRegistry.Get(packages, "mint"), false);
        sharedFloating = new DesktopPetViewModel(() => { }, _ => { }, vm.SelectedPet!, [], _ => { }, false, SyncDoNotDisturb);
        var samples = Enumerable.Range(0, 5).Select(i => new CodexTaskSnapshot
        {
            ThreadId = $"sample-{i}", Title = i == 0 ? "优化桌宠库的下载与切换体验" : $"界面检查任务 {i}",
            Project = i == 0 ? "Codex 任务提示器" : "示例项目", Cwd = @"C:\Example\Project", RolloutPath = "",
            Status = i < 3 ? CodexTaskStatus.Running : i == 3 ? CodexTaskStatus.NeedsInput : CodexTaskStatus.Completed,
            Activity = "正在检查界面与资源", LastActivityAt = DateTimeOffset.UtcNow.AddMinutes(-i), StartedAt = DateTimeOffset.UtcNow.AddMinutes(-14 - i * 2),
            CompletedAt = i == 4 ? DateTimeOffset.UtcNow.AddMinutes(-4) : null,
        }).ToArray();
        var child = samples[0] with { ThreadId = "child-0", IsSubagent = true, ParentThreadId = samples[0].ThreadId, AgentNickname = "界面检查", Activity = "检查小窗口的文字布局" };
        samples = samples.Append(child).ToArray();
        vm.Refresh(samples);
        var main = new MainWindow(vm, () => { }, () => { }, () => openedPicker++, () => { }, () => shownPet++);
        main.Show();
        await Capture(main, output, "main-desktop");
        Check(vm.RunningCount == 3 && vm.AttentionCount == 1 && vm.RecentCount == 1, "main counts exclude subagents and preserve the three categories");
        var unchangedRow = vm.Tasks[0];
        var durationUpdates = 0;
        unchangedRow.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TaskRowViewModel.DurationText)) durationUpdates++; };
        vm.SelectedTask = unchangedRow;
        vm.Refresh(samples);
        Check(ReferenceEquals(unchangedRow, vm.Tasks[0]), "unchanged task rows retain identity");
        Check(vm.SelectedTask?.ThreadId == unchangedRow.ThreadId && vm.SelectedTask?.Children.Count == 1, "task inspection survives refresh and includes subagents");
        Check(durationUpdates > 0, "elapsed time updates without rebuilding unchanged task rows");
        await Capture(main, output, "main-details");
        var liveUpdate = samples.Select(task => task.ThreadId == unchangedRow.ThreadId ? task with { Activity = "工具已返回，正在核对结果" } : task).ToArray();
        vm.Refresh(liveUpdate);
        Check(ReferenceEquals(vm.SelectedTask, unchangedRow) && vm.SelectedTask?.Activity == "工具已返回，正在核对结果", "live progress updates inspected content without replacing the selected row");
        vm.Refresh(samples);
        vm.OpenTaskCommand.Execute(vm.SelectedTask!.Children[0]);
        Check(openedTasks.Single() == "child-0", "subagent open targets its own conversation");
        vm.SelectedSort = "运行时长";
        Check(vm.Tasks[0].ThreadId == "sample-2" && vm.SelectedTask?.ThreadId == unchangedRow.ThreadId, "duration sorting preserves selected task");
        vm.SelectedSort = "最近活动";
        vm.SelectedProject = "Codex 任务提示器";
        Check(vm.Tasks.Count == 1 && vm.HasFilters, "project filter narrows the list");
        vm.ResetFiltersCommand.Execute(null);
        Check(vm.Tasks.Count == 3 && !vm.HasFilters, "clear filters restores the current category");
        Invoke(main, "OnShowPetClick");
        Invoke(main, "OnPetPickerClick");
        Check(shownPet == 1 && openedPicker == 1, "companion controls call the desktop and library actions");
        vm.ToggleDoNotDisturbCommand.Execute(null);
        Check(savedDoNotDisturb && sharedFloating.IsDoNotDisturb, "main do-not-disturb synchronizes with floating controls");
        await Capture(main, output, "main-dnd");
        sharedFloating.ToggleDoNotDisturbCommand.Execute(null);
        Check(!savedDoNotDisturb && !vm.IsDoNotDisturb, "floating do-not-disturb synchronizes with main controls");
        vm.TogglePauseCommand.Execute(null);
        sharedFloating.SetMonitoringState(vm.IsPaused, vm.IsDoNotDisturb);
        Check(monitor.IsPaused && vm.PauseButtonText == "恢复监听" && sharedFloating.FooterText.Contains("暂停"), "pause state has a restore action and reaches the floating footer");
        await Capture(main, output, "main-paused");
        vm.TogglePauseCommand.Execute(null);
        vm.InspectRecentCommand.Execute(vm.RecentTasks[0]);
        Check(vm.SelectedView == "recent" && vm.SelectedTask?.ThreadId == "sample-4", "recent-completion shortcut opens the corresponding inspection");
        await Capture(main, output, "main-recent");
        vm.SelectedView = "active";
        vm.ClearSelectionCommand.Execute(null);
        main.Width = main.MinWidth; main.Height = main.MinHeight;
        await Capture(main, output, "main-compact");
        vm.SelectedView = "attention";
        Check(vm.Tasks.Count == 1, "attention filter");
        vm.SearchText = "no-results";
        await Capture(main, output, "main-empty");
        Check(vm.EmptyTitle.Contains("匹配") && vm.HasFilters, "filtered empty state offers a recoverable search");
        vm.ResetFiltersCommand.Execute(null);
        vm.Refresh([]);
        await Capture(main, output, "main-idle");
        Check(!vm.HasTaskDetails && vm.RecentTasks.Count == 0, "empty data removes stale inspection and recent rows");
        main.AllowClose = true; main.DisposeTray(); main.Close();
        if (mainOnly) { Console.WriteLine("Main UI checks passed"); return; }

        var localSprite = Path.Combine(root, "src", "CodexTaskMonitor.App", "Assets", "PixelPetSpriteSheet.png");
        using var handler = new FixtureHandler(File.ReadAllBytes(localSprite));
        var client = new PetdexCatalogClient(new HttpClient(handler));
        var applied = "";
        var picker = new PetPickerWindow(packages, "mint", store, pet => { applied = pet.Id; return Task.CompletedTask; }, client);
        picker.Show();
        await Capture(picker, output, "library-desktop");
        picker.Width = picker.MinWidth; picker.Height = picker.MinHeight;
        await Capture(picker, output, "library-compact");
        picker.Width = 1060; picker.Height = 760;
        handler.CatalogStatus = HttpStatusCode.Forbidden;
        Invoke(picker, "OnDiscoverClick");
        await Until(() => !picker.IsBusy);
        Check(picker.HasError && picker.StatusMessage.Contains("403"), "catalog failures explain the actual server response");
        Check(picker.IsCatalogNotLoaded && picker.Pets.Count == packages.Count, "catalog failure preserves installed pets and offers retry");
        await Capture(picker, output, "discover-connection-error");
        handler.CatalogStatus = null;
        Invoke(picker, "OnRefreshPetsClick");
        await Until(() => !picker.IsBusy);
        Check(!picker.HasError && !picker.IsCatalogNotLoaded && picker.FilteredCatalog.Count == 12, "header refresh reconnects the online catalog on the discover tab");
        await Task.Delay(350);
        Check(picker.FilteredCatalog.Count == 12, "catalog page has 12 entries");
        await Capture(picker, output, "discover");
        Check(handler.MaximumConcurrent <= 3, "preview concurrency is bounded");
        handler.CatalogStatus = HttpStatusCode.ServiceUnavailable;
        Invoke(picker, "OnReloadCatalogClick");
        await Until(() => !picker.IsBusy);
        Check(picker.HasError && picker.FilteredCatalog.Count == 12 && picker.StatusMessage.Contains("保留"), "failed refresh retains the already loaded catalog");
        handler.CatalogStatus = null;
        Invoke(picker, "OnNextCatalogPageClick");
        Check(picker.FilteredCatalog.Count == 12, "paging never accumulates entries");
        Invoke(picker, "OnPreviousCatalogPageClick");
        picker.SearchText = "no-results";
        await Capture(picker, output, "discover-empty");
        picker.SearchText = "";
        picker.InstallValue = "npx petdex@latest install preview-0";
        Invoke(picker, "OnInstallClick");
        await Until(() => !picker.IsBusy);
        Check(picker.IsVisible && applied == "preview-0", "download applies without closing library");
        Check(picker.Pets.Any(p => p.Package.Id == applied && p.IsSelected), "installed pet appears and is selected");
        await Capture(picker, output, "installed-success");
        var requests = handler.PackageRequests;
        picker.InstallValue = "preview-0";
        Invoke(picker, "OnInstallClick");
        await Until(() => !picker.IsBusy);
        Check(handler.PackageRequests == requests, "installed pet is reused without download");
        picker.InstallValue = "invalid input!";
        Invoke(picker, "OnInstallClick");
        Check(picker.HasError, "invalid input is recoverable");
        await Capture(picker, output, "install-error");
        picker.InstallValue = "preview-1";
        handler.DelayPackages = true;
        Invoke(picker, "OnInstallClick");
        await Capture(picker, output, "install-loading");
        Invoke(picker, "OnCancelOperationClick");
        await Until(() => !picker.IsBusy);
        Check(!Directory.Exists(Path.Combine(fixture, "Pets", "preview-1")), "cancelled download leaves no package");
        picker.Close();
        var settings = new SettingsWindow(new());
        settings.Show(); await Capture(settings, output, "settings"); settings.Close();
        Directory.Delete(fixture, recursive: true);
        Console.WriteLine($"UI checks passed. Rendered screenshots: {output}");
    }

    private static void Invoke(object target, string method) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, [target, new RoutedEventArgs()]);
    private static async Task RunLiveCatalogAsync(string root)
    {
        var output = Path.Combine(root, "artifacts", "ui-review");
        Directory.CreateDirectory(output);
        var store = new PetLibraryStore(Path.Combine(root, "Pets"));
        var packages = await PetPackageRegistry.LoadAllAsync(store);
        var picker = new PetPickerWindow(packages, "shinchan", store);
        picker.Show();
        Invoke(picker, "OnDiscoverClick");
        for (var count = 0; picker.IsBusy && count < 1800; count++) await Task.Delay(50);
        Check(!picker.IsBusy && !picker.HasError && picker.FilteredCatalog.Count > 0, $"live catalog loads through the production client: {picker.StatusMessage}");
        Console.WriteLine($"Live catalog entries: {picker.Catalog.Count}");
        await Task.Delay(3000);
        await Capture(picker, output, "discover-live");
        picker.Close();
    }
    private static async Task RunFloatingAsync(string root)
    {
        var output = Path.Combine(root, "artifacts", "ui-review");
        Directory.CreateDirectory(output);
        var packages = PetPackageRegistry.All;
        var palette = new FloatingStatusBrushConverter();
        Check(new[] { CodexTaskStatus.Running, CodexTaskStatus.Completed, CodexTaskStatus.NeedsInput, CodexTaskStatus.Failed }
            .All(status => ContrastOnWhite(((SolidColorBrush)palette.Convert(status, typeof(Brush), null!, System.Globalization.CultureInfo.InvariantCulture)).Color) >= 4.5),
            "floating status labels meet text contrast requirements");
        var pet = PetPackageRegistry.Get(packages, "mint");
        var opened = new List<string>();
        var acknowledged = new List<RecentTaskAcknowledgement>();
        var vm = new DesktopPetViewModel(() => { }, task => opened.Add(task.ThreadId), pet, [], acknowledged.Add);
        var tasks = Enumerable.Range(0, 4).Select(i => new CodexTaskSnapshot
        {
            ThreadId = $"floating-{i}",
            Title = i == 0 ? "调整悬浮窗的配色与交互，检查长任务名称是否能完整阅读" : $"完成界面验收第 {i} 项，整理结果并保留可回看的任务记录",
            Project = "任务提示器", Cwd = @"C:\Example\Project", RolloutPath = "", Status = i == 0 ? CodexTaskStatus.Running : CodexTaskStatus.Completed,
            Activity = "正在检查任务状态", LastActivityAt = DateTimeOffset.UtcNow.AddMinutes(-i), StartedAt = DateTimeOffset.UtcNow.AddMinutes(-4),
        }).ToArray();
        vm.Refresh(tasks);
        Check(vm.CategoryTasks[0].TimeText.Contains("已运行"), "floating running row exposes real elapsed time");
        vm.ToggleDoNotDisturbCommand.Execute(null);
        Check(vm.IsDoNotDisturb && vm.FooterText.Contains("勿扰"), "floating do-not-disturb exposes its active state");
        vm.ToggleDoNotDisturbCommand.Execute(null);
        var area = SystemParameters.WorkArea;
        var window = new DesktopPetWindow(vm, area.Right - 180, area.Bottom - 180, false, false, (_, _, _) => { });
        window.SetAnimationsEnabled(false);
        window.Show();
        await Capture(window, output, "floating-pet");
        var right = window.Left + window.Width;
        var bottom = window.Top + window.Height;
        vm.ToggleExpandedCommand.Execute(null);
        await Capture(window, output, "floating-active");
        vm.ToggleDoNotDisturbCommand.Execute(null);
        await Capture(window, output, "floating-dnd");
        vm.SetMonitoringState(true, vm.IsDoNotDisturb);
        await Capture(window, output, "floating-paused");
        vm.SetMonitoringState(false, false);
        Check(Math.Abs(window.Left + window.Width - right) < 1 && Math.Abs(window.Top + window.Height - bottom) < 1, "opening the tray preserves the pet anchor");
        vm.Refresh(tasks.Select(task => task.ThreadId == "floating-0" ? task with { Title = "短任务" } : task).ToArray());
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        window.UpdateLayout();
        var shortTaskHeight = window.Height;
        vm.Refresh(tasks);
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        window.UpdateLayout();
        Check(window.Height > shortTaskHeight, "live title changes resize the tray when the task count stays unchanged");
        vm.SelectCategoryCommand.Execute("recent");
        await Capture(window, output, "floating-recent");
        Check(vm.CategoryTasks.Count == 3, "floating recent tab has three completed tasks");
        Check(vm.CategoryTasks.All(item => item.StatusLabel == "已完成" && item.SecondaryText == "任务提示器"), "completed rows show readable status and project");
        vm.OpenTaskCommand.Execute(vm.CategoryTasks[0]);
        Check(opened.Count == 1 && acknowledged.Count == 1 && vm.RecentCount == 2, "task click opens its conversation and acknowledges the result");
        var attention = tasks[0] with { ThreadId = "needs-reply", Status = CodexTaskStatus.NeedsInput, Title = "选择最终的导出文件，等待你的回复" };
        vm.Refresh(tasks.Prepend(attention).ToArray());
        vm.SelectCategoryCommand.Execute("attention");
        await Capture(window, output, "floating-attention");
        vm.Refresh([]);
        vm.SelectCategoryCommand.Execute("active");
        await Capture(window, output, "floating-empty");
        Check(vm.EmptyTitle == "目前没有进行中的任务", "empty state describes the current category");
        var manyTasks = Enumerable.Range(0, 20).Select(i => tasks[0] with { ThreadId = $"long-{i}" }).ToArray();
        vm.Refresh(manyTasks);
        await Capture(window, output, "floating-scroll");
        var panel = (FrameworkElement)window.FindName("TaskListPanel");
        var scroller = (ScrollViewer)window.FindName("TaskListScroller");
        Check(window.ActualHeight <= window.MaxHeight && panel.ActualHeight <= 378 && scroller.ScrollableHeight > 0, "long lists stay inside the window and scroll");
        scroller.ScrollToBottom();
        await Capture(window, output, "floating-scroll-bottom");
        var finalButton = Descendants<Button>(scroller).Last(button => button.DataContext is DesktopPetTaskItem);
        var bounds = finalButton.TransformToAncestor(scroller).TransformBounds(new Rect(finalButton.RenderSize));
        Check(bounds.Bottom <= scroller.ActualHeight + 1, "last task is not clipped at the bottom");
        var longListHeight = window.Height;
        vm.Refresh([]);
        await Capture(window, output, "floating-after-reset");
        Check(window.Height < longListHeight, "empty categories shrink instead of retaining the previous list height");
        Invoke(window, "OnCompactClick");
        await Capture(window, output, "floating-orb");
        Check(window.ActualWidth == 48 && window.ActualHeight == 48, "compact orb has a true 48px window");
        Check(((FrameworkElement)window.Content).ActualHeight <= 48, "compact orb has no hidden pet-sized layout area");
        Invoke(window, "OnRestoreClick");
        await Capture(window, output, "floating-restored");
        Check(window.Width == 126 && !vm.IsExpanded, "orb restores the pet without losing the UI state");
        window.AllowClose = true;
        window.Close();
        Console.WriteLine("Floating UI checks passed");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static double ContrastOnWhite(Color color)
    {
        static double Channel(byte value)
        {
            var channel = value / 255.0;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        var luminance = 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        return 1.05 / (luminance + 0.05);
    }
    private static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); Console.WriteLine("PASS " + label); }
    private static async Task Until(Func<bool> predicate)
    {
        for (var count = 0; count < 200; count++) { if (predicate()) return; await Task.Delay(25); }
        throw new TimeoutException("UI operation did not finish");
    }
    private static async Task Capture(Window window, string directory, string name)
    {
        await Task.Delay(240);
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        if (window is MainWindow && window.FindName("InspectorRoot") is ScrollViewer inspector)
        {
            inspector.BeginAnimation(UIElement.OpacityProperty, null);
            inspector.Opacity = 1;
        }
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var image = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.AllowsTransparency ? Brushes.WhiteSmoke : window.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        image.Render(background);
        image.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }

    private sealed class FixtureHandler(byte[] sprite) : HttpMessageHandler
    {
        public int PackageRequests;
        public bool DelayPackages;
        public HttpStatusCode? CatalogStatus;
        public int MaximumConcurrent;
        private int _active;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var uri = request.RequestUri!;
            if (uri == PetdexCatalogClient.ManifestUri && CatalogStatus is { } status)
                return new HttpResponseMessage(status);
            if (uri == PetdexCatalogClient.ManifestUri)
                return Response(JsonSerializer.SerializeToUtf8Bytes(new { pets = Enumerable.Range(0, 26).Select(i => new
                {
                    slug = $"preview-{i}", displayName = $"预览角色 {i}", submittedBy = "界面测试素材", description = "本地测试角色",
                    spritesheetUrl = $"https://example.test/{i}.png", petJsonUrl = $"https://example.test/{i}.json",
                }) }));
            if (uri.AbsolutePath.EndsWith(".json"))
            {
                PackageRequests++;
                if (DelayPackages) await Task.Delay(10000, token);
                return Response(Encoding.UTF8.GetBytes("{\"displayName\":\"新伙伴\",\"description\":\"本地流程测试素材\"}"));
            }
            var active = Interlocked.Increment(ref _active);
            MaximumConcurrent = Math.Max(MaximumConcurrent, active);
            try { await Task.Delay(30, token); return Response(sprite); }
            finally { Interlocked.Decrement(ref _active); }
        }
        private static HttpResponseMessage Response(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    }
}
