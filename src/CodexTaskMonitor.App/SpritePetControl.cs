using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CodexTaskMonitor.App;

public sealed class SpritePetControl : FrameworkElement
{
    public static readonly DependencyProperty SpriteSheetProperty = DependencyProperty.Register(
        nameof(SpriteSheet), typeof(BitmapSource), typeof(SpritePetControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSpriteLayoutChanged));

    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(SpritePetControl),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsRender, OnSpriteLayoutChanged));

    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(int), typeof(SpritePetControl),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsRender, OnSpriteLayoutChanged));

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(PetSpriteState), typeof(SpritePetControl),
        new FrameworkPropertyMetadata(PetSpriteState.Idle, FrameworkPropertyMetadataOptions.AffectsRender, OnStateChanged));

    public static readonly DependencyProperty AnimationRowProperty = DependencyProperty.Register(
        nameof(AnimationRow), typeof(int), typeof(SpritePetControl),
        new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender, OnStateChanged));

    public static readonly DependencyProperty IsAnimationEnabledProperty = DependencyProperty.Register(
        nameof(IsAnimationEnabled), typeof(bool), typeof(SpritePetControl),
        new FrameworkPropertyMetadata(true, OnStateChanged));

    private readonly DispatcherTimer _timer;
    private BitmapSource? _cachedSheet;
    private int _cachedColumns;
    private int _cachedRows;
    private IReadOnlyList<BitmapSource> _frames = Array.Empty<BitmapSource>();
    private int _frameIndex;

    public SpritePetControl()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(140),
        };
        _timer.Tick += (_, _) => AdvanceFrame();
        Loaded += (_, _) => ResetPlayback();
        IsVisibleChanged += (_, _) => ResetPlayback();
        Unloaded += (_, _) => _timer.Stop();
    }

    public BitmapSource? SpriteSheet
    {
        get => (BitmapSource?)GetValue(SpriteSheetProperty);
        set => SetValue(SpriteSheetProperty, value);
    }

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public int Rows
    {
        get => (int)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public PetSpriteState State
    {
        get => (PetSpriteState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public int AnimationRow
    {
        get => (int)GetValue(AnimationRowProperty);
        set => SetValue(AnimationRowProperty, value);
    }

    public bool IsAnimationEnabled
    {
        get => (bool)GetValue(IsAnimationEnabledProperty);
        set => SetValue(IsAnimationEnabledProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var side = double.IsInfinity(availableSize.Width) || double.IsInfinity(availableSize.Height)
            ? 96
            : Math.Min(availableSize.Width, availableSize.Height);
        return new Size(side, side);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        EnsureFrames();
        if (_frames.Count == 0)
        {
            return;
        }

        var frame = _frames[_frameIndex];
        var scale = Math.Min(RenderSize.Width / frame.PixelWidth, RenderSize.Height / frame.PixelHeight);
        var size = new Size(frame.PixelWidth * scale, frame.PixelHeight * scale);
        drawingContext.DrawImage(frame, new Rect(
            (RenderSize.Width - size.Width) / 2,
            (RenderSize.Height - size.Height) / 2,
            size.Width,
            size.Height));
    }

    private static void OnSpriteLayoutChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
        => ((SpritePetControl)source).ResetPlayback();

    private static void OnStateChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
        => ((SpritePetControl)source).ResetPlayback();

    private void ResetPlayback()
    {
        _frameIndex = 0;
        EnsureFrames(force: true);
        _timer.Stop();
        if (IsLoaded && IsVisible && IsAnimationEnabled && _frames.Count > 1)
        {
            _timer.Interval = FrameDuration(State);
            _timer.Start();
        }

        InvalidateVisual();
    }

    private void AdvanceFrame()
    {
        if (_frames.Count <= 1)
        {
            return;
        }

        _frameIndex = (_frameIndex + 1) % _frames.Count;
        InvalidateVisual();
    }

    private void EnsureFrames(bool force = false)
    {
        if (!force && ReferenceEquals(_cachedSheet, SpriteSheet)
            && _cachedColumns == Columns && _cachedRows == Rows)
        {
            return;
        }

        _cachedSheet = SpriteSheet;
        _cachedColumns = Columns;
        _cachedRows = Rows;
        _frameIndex = 0;
        if (SpriteSheet is null || Columns < 1 || Rows < 1)
        {
            _frames = Array.Empty<BitmapSource>();
            return;
        }

        var row = AnimationRow is >= 0 ? Math.Min(AnimationRow, Rows - 1) : Math.Min((int)State, Rows - 1);
        var frameWidth = SpriteSheet.PixelWidth / Columns;
        var frameHeight = SpriteSheet.PixelHeight / Rows;
        if (frameWidth < 1 || frameHeight < 1)
        {
            _frames = Array.Empty<BitmapSource>();
            return;
        }

        var insetX = Columns == 1 ? 0 : Math.Min(18, frameWidth / 4);
        var insetY = Rows == 1 ? 0 : Math.Min(14, frameHeight / 6);
        var croppedWidth = frameWidth - (insetX * 2);
        var croppedHeight = frameHeight - (insetY * 2);
        _frames = Enumerable.Range(0, Columns)
            .Select(column =>
            {
                var frame = new CroppedBitmap(SpriteSheet, new Int32Rect(
                    (column * frameWidth) + insetX,
                    (row * frameHeight) + insetY,
                    croppedWidth,
                    croppedHeight));
                frame.Freeze();
                return (BitmapSource)frame;
            })
            .ToArray();
    }

    private static TimeSpan FrameDuration(PetSpriteState state) => state switch
    {
        PetSpriteState.Working => TimeSpan.FromMilliseconds(110),
        PetSpriteState.Alert => TimeSpan.FromMilliseconds(125),
        PetSpriteState.Celebrating => TimeSpan.FromMilliseconds(120),
        PetSpriteState.Waiting => TimeSpan.FromMilliseconds(165),
        _ => TimeSpan.FromMilliseconds(180),
    };
}
