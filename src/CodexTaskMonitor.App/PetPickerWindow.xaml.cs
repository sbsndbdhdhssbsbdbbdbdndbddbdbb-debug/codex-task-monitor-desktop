using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CommunityToolkit.Mvvm.ComponentModel;
using CodexTaskMonitor.Core;

namespace CodexTaskMonitor.App;

public partial class PetPickerWindow : Window, INotifyPropertyChanged
{
    private const int PageSize = 12;
    private readonly PetLibraryStore _store;
    private readonly PetdexCatalogClient _client;
    private readonly Func<PetPackage, Task>? _applyPet;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    private bool _catalogLoaded;
    private bool _closed;
    private int _page;
    private int _matches;
    private string _installValue = "";
    private string _searchText = "";
    private bool _isCatalogVisible;
    private bool _isBusy;
    private bool _hasError;
    private string _statusMessage = "";
    private string _catalogErrorMessage = "可以重新加载，或先使用已安装的伙伴。";
    private PetPickerItem? _currentPet;

    public PetPickerWindow(IEnumerable<PetPackage> packages, string selectedPackageId, PetLibraryStore store,
        Func<PetPackage, Task>? applyPet = null, PetdexCatalogClient? client = null)
    {
        InitializeComponent();
        _store = store;
        _applyPet = applyPet;
        _client = client ?? new PetdexCatalogClient();
        Previews = new PetPreviewSession(_client);
        Pets = new(packages.Select(package => new PetPickerItem(package, package.Id == selectedPackageId)));
        _currentPet = Pets.FirstOrDefault(item => item.IsSelected) ?? Pets.FirstOrDefault();
        DataContext = this;
        Loaded += (_, _) =>
        {
            if (AnimationsEnabled) LibraryContent.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0.65, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut } });
        };
        Closed += (_, _) => { _closed = true; _lifetime.Cancel(); Previews.Dispose(); _lifetime.Dispose(); };
    }

    public ObservableCollection<PetPickerItem> Pets { get; }
    public ObservableCollection<PetdexCatalogItem> Catalog { get; } = [];
    public ObservableCollection<PetdexCatalogItem> FilteredCatalog { get; } = [];
    public PetPreviewSession Previews { get; }
    public PetPackage? Result { get; private set; }
    public bool AnimationsEnabled => SystemParameters.ClientAreaAnimation;
    public PetPickerItem? CurrentPet { get => _currentPet; private set => SetField(ref _currentPet, value); }
    public string LibrarySummary => $"{Pets.Count} 位桌面伙伴";
    public bool IsLibraryVisible => !IsCatalogVisible;
    public bool CanInteract => !IsBusy;
    public bool CanInstall => !IsBusy && !string.IsNullOrWhiteSpace(InstallValue);
    public bool HasPreviousCatalog => !IsBusy && _page > 0;
    public bool HasNextCatalog => !IsBusy && (_page + 1) * PageSize < _matches;
    public bool IsCatalogEmpty => _catalogLoaded && !IsBusy && FilteredCatalog.Count == 0;
    public bool IsCatalogNotLoaded => !_catalogLoaded && !IsBusy;
    public string CatalogPageLabel => _matches == 0 ? "暂无结果" : $"{_page + 1} / {(_matches + PageSize - 1) / PageSize}";
    public string CatalogSummary => _catalogLoaded ? $"共 {_matches:N0} 个角色" : "";
    public string RefreshTooltip => IsCatalogVisible ? "重新加载在线目录" : "刷新已下载桌宠";
    public string CatalogErrorMessage { get => _catalogErrorMessage; private set => SetField(ref _catalogErrorMessage, value); }
    public Brush FeedbackBrush => HasError ? Brushes.Firebrick : (Brush)FindResource("MutedBrush");
    public bool HasError { get => _hasError; private set { SetField(ref _hasError, value); OnPropertyChanged(nameof(FeedbackBrush)); } }
    public string StatusMessage { get => _statusMessage; private set => SetField(ref _statusMessage, value); }
    public string InstallValue { get => _installValue; set { SetField(ref _installValue, value); OnPropertyChanged(nameof(CanInstall)); } }
    public string SearchText { get => _searchText; set { if (SetField(ref _searchText, value)) { _page = 0; ApplyFilter(); } } }
    public bool IsCatalogVisible
    {
        get => _isCatalogVisible;
        set
        {
            if (SetField(ref _isCatalogVisible, value))
            {
                if (!value) Previews.CancelPending();
                OnPropertyChanged(nameof(IsLibraryVisible));
                OnPropertyChanged(nameof(RefreshTooltip));
            }
        }
    }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            SetField(ref _isBusy, value);
            foreach (var property in new[] { nameof(CanInstall), nameof(CanInteract), nameof(HasPreviousCatalog), nameof(HasNextCatalog), nameof(IsCatalogEmpty), nameof(IsCatalogNotLoaded) }) OnPropertyChanged(property);
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnLibraryClick(object sender, RoutedEventArgs e) => IsCatalogVisible = false;
    private void OnClearSearchClick(object sender, RoutedEventArgs e) => SearchText = "";
    private async void OnDiscoverClick(object sender, RoutedEventArgs e)
    {
        IsCatalogVisible = true;
        if (!_catalogLoaded && !IsBusy) await LoadCatalogAsync();
    }
    private async void OnReloadCatalogClick(object sender, RoutedEventArgs e) => await LoadCatalogAsync();
    private async Task LoadCatalogAsync()
    {
        if (IsBusy) return;
        var token = BeginOperation("正在连接 Petdex…");
        try
        {
            var entries = await _client.GetCatalogAsync(token);
            Catalog.Clear();
            foreach (var entry in entries) Catalog.Add(new PetdexCatalogItem(entry, Pets.Any(p => p.Package.Id == entry.Slug)));
            _catalogLoaded = true;
            CatalogErrorMessage = "";
            ApplyFilter();
            Feedback("角色列表已更新");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            CatalogErrorMessage = "加载已取消，已安装的桌宠仍可正常使用。";
            Feedback("已取消加载，可重新刷新");
        }
        catch (Exception error)
        {
            CatalogErrorMessage = DescribeCatalogFailure(error);
            Feedback(_catalogLoaded ? $"{CatalogErrorMessage}；已保留上次加载的列表" : CatalogErrorMessage, true);
            await AppLog.WriteAsync("WARN", "Petdex catalog could not be loaded", error);
        }
        finally { EndOperation(); }
    }

    private async void OnRefreshPetsClick(object sender, RoutedEventArgs e)
    {
        if (IsCatalogVisible) { await LoadCatalogAsync(); return; }
        if (IsBusy) return;
        BeginOperation("正在刷新我的桌宠…");
        try
        {
            var active = CurrentPet?.Package.Id;
            await ReloadPetsAsync(active);
            Feedback($"已刷新，共 {Pets.Count} 个桌宠");
        }
        catch (Exception) { Feedback("读取桌宠失败，请重新刷新", true); }
        finally { EndOperation(); }
    }

    private async Task ReloadPetsAsync(string? selectedId)
    {
        var packages = await Task.Run(() => PetPackageRegistry.LoadAllAsync(_store));
        if (_closed) return;
        Pets.Clear();
        foreach (var package in packages) Pets.Add(new PetPickerItem(package, package.Id == selectedId));
        CurrentPet = Pets.FirstOrDefault(p => p.IsSelected) ?? Pets.FirstOrDefault();
        foreach (var item in Catalog) item.IsInstalled = Pets.Any(p => p.Package.Id == item.Entry.Slug);
        OnPropertyChanged(nameof(LibrarySummary));
    }

    private async void OnInstallClick(object sender, RoutedEventArgs e)
    {
        if (!CanInstall) return;
        string slug;
        try { slug = PetdexCatalogClient.NormalizeSlugOrUrl(InstallValue); }
        catch { Feedback("请输入角色名、Petdex 角色链接，或粘贴安装命令", true); return; }
        var existing = Pets.FirstOrDefault(p => p.Package.Id == slug);
        if (existing is not null) { await UsePetAsync(existing); return; }
        if (!_catalogLoaded) await LoadCatalogAsync();
        if (!_catalogLoaded || _closed) return;
        var entry = Catalog.FirstOrDefault(p => p.Entry.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
        if (entry is null) { Feedback("没有找到这个角色，请核对名称或链接后重试", true); return; }
        await InstallAsync(entry.Entry);
    }
    private void OnInstallKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && CanInstall) { e.Handled = true; OnInstallClick(sender, e); }
    }
    private async void OnCatalogInstallClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PetdexCatalogItem item }) await InstallAsync(item.Entry);
    }
    private async Task InstallAsync(PetdexCatalogEntry entry)
    {
        if (IsBusy) return;
        var existing = Pets.FirstOrDefault(p => p.Package.Id == entry.Slug);
        if (existing is not null) { await UsePetAsync(existing); return; }
        var token = BeginOperation($"正在下载 {entry.DisplayName}…");
        try
        {
            await Task.Run(async () =>
            {
                var download = await _client.DownloadAsync(entry, token);
                token.ThrowIfCancellationRequested();
                await _store.InstallAsync(download.Package, download.SpriteSheetBytes, token);
            }, token);
            if (_closed) return;
            await ReloadPetsAsync(CurrentPet?.Package.Id);
            var installed = Pets.FirstOrDefault(p => p.Package.Id == entry.Slug)
                ?? throw new InvalidDataException("Installed pet could not be loaded.");
            await ApplyPetAsync(installed);
            IsCatalogVisible = false;
            Feedback($"{installed.Package.Name} 已下载，正在桌面陪你工作");
            InstallValue = "";
        }
        catch (OperationCanceledException) { Feedback("已取消下载"); }
        catch (Exception) { Feedback($"{entry.DisplayName} 未能完成下载或应用，请重试", true); }
        finally { EndOperation(); }
    }

    private async void OnPetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PetPickerItem item }) await UsePetAsync(item);
    }
    private async Task UsePetAsync(PetPickerItem item)
    {
        if (IsBusy) return;
        BeginOperation($"正在切换到 {item.Package.Name}…");
        try { await ApplyPetAsync(item); Feedback($"已切换为 {item.Package.Name}"); }
        catch (Exception) { Feedback("桌宠切换失败，请重新选择", true); }
        finally { EndOperation(); }
    }
    private async Task ApplyPetAsync(PetPickerItem item)
    {
        if (_applyPet is not null) await _applyPet(item.Package);
        Result = item.Package;
        foreach (var pet in Pets) pet.IsSelected = pet == item;
        CurrentPet = item;
    }
    private async void OnDeletePetClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy || sender is not FrameworkElement { DataContext: PetPickerItem item } || !item.CanDelete) return;
        if (MessageBox.Show(this, $"删除 {item.Package.Name} 的全部本地素材？以后可以重新下载。", "删除桌宠", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        BeginOperation("正在删除…");
        try
        {
            if (item.IsSelected) await ApplyPetAsync(Pets.First(p => !p.CanDelete));
            await _store.DeleteAsync(item.Package.Id);
            Pets.Remove(item);
            foreach (var entry in Catalog.Where(p => p.Entry.Slug == item.Package.Id)) entry.IsInstalled = false;
            OnPropertyChanged(nameof(LibrarySummary));
            Feedback($"已删除 {item.Package.Name} 的本地素材");
        }
        catch (Exception) { Feedback("未能删除素材，请稍后重试", true); }
        finally { EndOperation(); }
    }
    private void OnPreviousCatalogPageClick(object sender, RoutedEventArgs e) { if (HasPreviousCatalog) { _page--; ApplyFilter(); } }
    private void OnNextCatalogPageClick(object sender, RoutedEventArgs e) { if (HasNextCatalog) { _page++; ApplyFilter(); } }
    private void ApplyFilter()
    {
        Previews.CancelPending();
        var matches = Catalog.Where(p => string.IsNullOrWhiteSpace(SearchText) || p.Matches(SearchText.Trim())).ToArray();
        _matches = matches.Length;
        _page = Math.Min(_page, Math.Max(0, (_matches - 1) / PageSize));
        FilteredCatalog.Clear();
        foreach (var item in matches.Skip(_page * PageSize).Take(PageSize)) FilteredCatalog.Add(item);
        CatalogScroll?.ScrollToTop();
        foreach (var property in new[] { nameof(CatalogSummary), nameof(CatalogPageLabel), nameof(HasPreviousCatalog), nameof(HasNextCatalog), nameof(IsCatalogEmpty) }) OnPropertyChanged(property);
    }
    private void OnOpenPetdexWebsiteClick(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://petdex.dev/") { UseShellExecute = true }); }
        catch { Feedback("未能打开浏览器，请手动访问 petdex.dev", true); }
    }
    private static string DescribeCatalogFailure(Exception error) => error switch
    {
        TimeoutException => "连接 Petdex 超时，请稍后重试",
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden } => "Petdex 拒绝了本次连接（HTTP 403），请稍后重试或打开官方浏览",
        HttpRequestException { StatusCode: { } status } => $"Petdex 服务暂时无法访问（HTTP {(int)status}），请稍后重试",
        HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } => "无法找到 Petdex 服务器地址，请检查网络后重试",
        HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } => "无法建立与 Petdex 的安全连接，请稍后重试或打开官方浏览",
        HttpRequestException => "暂时无法连接 Petdex，请重试或打开官方浏览",
        InvalidDataException => "Petdex 返回的目录数据无法读取，请稍后重试",
        _ => "在线桌宠目录暂时无法加载，请重试或打开官方浏览",
    };
    private CancellationToken BeginOperation(string text)
    {
        _operation?.Dispose();
        _operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        Feedback(text);
        IsBusy = true;
        return _operation.Token;
    }
    private void EndOperation() { IsBusy = false; _operation?.Dispose(); _operation = null; }
    private void OnCancelOperationClick(object sender, RoutedEventArgs e) => _operation?.Cancel();
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    private void Feedback(string message, bool error = false) { StatusMessage = message; HasError = error; }
    private bool SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; OnPropertyChanged(name); return true;
    }
    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed partial class PetPickerItem : ObservableObject
{
    public PetPickerItem(PetPackage package, bool selected) { Package = package; IsSelected = selected; }
    public PetPackage Package { get; }
    public int PackagePreviewRow => Package.GetAnimationRow(TaskDisplayCategory.InProgress);
    public bool CanDelete => !string.IsNullOrWhiteSpace(Package.SourcePageUrl);
    public string SourceLabel => CanDelete ? "Petdex" : "内置";
    [ObservableProperty] private bool _isSelected;
}

public sealed partial class PetdexCatalogItem : ObservableObject
{
    public PetdexCatalogItem(PetdexCatalogEntry entry, bool installed = false) { Entry = entry; IsInstalled = installed; }
    public PetdexCatalogEntry Entry { get; }
    public string Subtitle => string.IsNullOrWhiteSpace(Entry.SubmittedBy) ? "Petdex" : Entry.SubmittedBy;
    public string ActionText => IsInstalled ? "立即使用" : "下载并使用";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ActionText))] private bool _isInstalled;
    public bool Matches(string text) => string.Join(" ", Entry.Slug, Entry.DisplayName, Entry.Description, Entry.Kind, Entry.SubmittedBy).Contains(text, StringComparison.CurrentCultureIgnoreCase);
}
