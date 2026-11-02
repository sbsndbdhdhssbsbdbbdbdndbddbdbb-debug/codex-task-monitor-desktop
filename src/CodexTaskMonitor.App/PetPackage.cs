using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexTaskMonitor.Core;

namespace CodexTaskMonitor.App;

public sealed class PetPackage
{
    private readonly IReadOnlyDictionary<TaskDisplayCategory, PetSpriteState> _stateMap;
    private readonly IReadOnlyDictionary<TaskDisplayCategory, int> _animationRows;

    public PetPackage(
        string id,
        string name,
        string description,
        string spriteSheetUri,
        Color backgroundColor,
        Color borderColor,
        Color accentColor,
        IReadOnlyDictionary<TaskDisplayCategory, PetSpriteState> stateMap,
        int columns = 8,
        int rows = 6,
        IReadOnlyDictionary<TaskDisplayCategory, int>? animationRows = null,
        string? sourcePageUrl = null)
    {
        Id = id;
        Name = name;
        Description = description;
        SpriteSheet = LoadSpriteSheet(spriteSheetUri);
        BackgroundBrush = new SolidColorBrush(backgroundColor);
        BorderBrush = new SolidColorBrush(borderColor);
        AccentBrush = new SolidColorBrush(accentColor);
        BackgroundBrush.Freeze();
        BorderBrush.Freeze();
        AccentBrush.Freeze();
        _stateMap = stateMap;
        Columns = columns;
        Rows = rows;
        _animationRows = animationRows ?? stateMap.ToDictionary(pair => pair.Key, pair => (int)pair.Value);
        SourcePageUrl = sourcePageUrl;
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public BitmapImage SpriteSheet { get; }
    public int Columns { get; }
    public int Rows { get; }
    public string? SourcePageUrl { get; }
    public Brush BackgroundBrush { get; }
    public Brush BorderBrush { get; }
    public Brush AccentBrush { get; }

    public PetSpriteState GetSpriteState(TaskDisplayCategory category) =>
        _stateMap.TryGetValue(category, out var state) ? state : PetSpriteState.Idle;

    public int GetAnimationRow(TaskDisplayCategory category) =>
        _animationRows.TryGetValue(category, out var row) ? row : 0;

    private static BitmapImage LoadSpriteSheet(string spriteSheetUri)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(spriteSheetUri, UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }
}

public static class PetPackageRegistry
{
    private const string MintSheet = "pack://application:,,,/CodexTaskMonitor;component/Assets/PixelPetSpriteSheet.png";
    private const string OceanSheet = "pack://application:,,,/CodexTaskMonitor;component/Assets/PixelPetOceanSpriteSheet.png";
    private const string CoralSheet = "pack://application:,,,/CodexTaskMonitor;component/Assets/PixelPetCoralSpriteSheet.png";

    private static readonly IReadOnlyDictionary<TaskDisplayCategory, PetSpriteState> DefaultStateMap =
        new Dictionary<TaskDisplayCategory, PetSpriteState>
        {
            [TaskDisplayCategory.InProgress] = PetSpriteState.Working,
            [TaskDisplayCategory.NeedsAttention] = PetSpriteState.Alert,
            [TaskDisplayCategory.RecentlyCompleted] = PetSpriteState.Celebrating,
        };

    public static IReadOnlyList<PetPackage> All { get; } =
    [
        new PetPackage("mint", "薄荷终端", "清爽的默认像素助手", MintSheet,
            Color.FromRgb(220, 240, 232), Color.FromRgb(185, 214, 201), Color.FromRgb(26, 119, 90), DefaultStateMap),
        new PetPackage("ocean", "深海终端", "冷静的蓝色像素助手", OceanSheet,
            Color.FromRgb(222, 237, 248), Color.FromRgb(171, 202, 224), Color.FromRgb(36, 104, 155), DefaultStateMap),
        new PetPackage("coral", "珊瑚终端", "温暖的珊瑚色像素助手", CoralSheet,
            Color.FromRgb(250, 235, 227), Color.FromRgb(229, 191, 174), Color.FromRgb(184, 85, 62), DefaultStateMap),
    ];

    public static PetPackage Get(string? id) => All.FirstOrDefault(pet =>
        string.Equals(pet.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    public static async Task<IReadOnlyList<PetPackage>> LoadAllAsync(PetLibraryStore store)
    {
        var installed = await store.ListAsync();
        var packages = new List<PetPackage>(All);
        foreach (var pet in installed)
        {
            try
            {
                packages.Add(CreateInstalled(pet));
            }
            catch (Exception)
            {
                // A corrupt optional pet must never prevent the built-in pet from loading.
            }
        }

        return packages;
    }

    public static PetPackage Get(IReadOnlyList<PetPackage> packages, string? id) => packages.FirstOrDefault(pet =>
        string.Equals(pet.Id, id, StringComparison.OrdinalIgnoreCase)) ?? packages[0];

    private static PetPackage CreateInstalled(InstalledPetPackage pet)
    {
        var rows = BuildPetdexRows(pet.Actions, pet.Layout.Rows);
        return new PetPackage(
            pet.Id,
            pet.Name,
            pet.Description,
            new Uri(pet.SpriteSheetPath, UriKind.Absolute).AbsoluteUri,
            Color.FromRgb(234, 241, 239),
            Color.FromRgb(173, 201, 192),
            Color.FromRgb(29, 118, 96),
            DefaultStateMap,
            pet.Layout.Columns,
            pet.Layout.Rows,
            rows,
            pet.SourcePageUrl);
    }

    private static IReadOnlyDictionary<TaskDisplayCategory, int> BuildPetdexRows(IReadOnlyList<string> actions, int rowCount)
    {
        var rowsByAction = actions
            .Select((action, index) => new { Action = action.Trim().ToLowerInvariant(), Index = index })
            .Where(item => item.Index < rowCount)
            .GroupBy(item => item.Action, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);

        var idle = FindRow(rowsByAction, 0, "idle");
        return new Dictionary<TaskDisplayCategory, int>
        {
            [TaskDisplayCategory.InProgress] = FindRow(rowsByAction, idle, "running", "run", "run-right", "runright"),
            [TaskDisplayCategory.NeedsAttention] = FindRow(rowsByAction, idle, "waiting", "failed", "review"),
            [TaskDisplayCategory.RecentlyCompleted] = FindRow(rowsByAction, idle, "waving", "wave", "review"),
        };
    }

    private static int FindRow(IReadOnlyDictionary<string, int> rows, int fallback, params string[] actions) => actions
        .Select(action => rows.TryGetValue(action, out var row) ? row : (int?)null)
        .FirstOrDefault(row => row.HasValue) ?? fallback;
}
