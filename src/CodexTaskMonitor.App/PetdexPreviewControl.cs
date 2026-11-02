using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexTaskMonitor.App;

public sealed class PetdexPreviewControl : FrameworkElement
{
    public static readonly DependencyProperty SpriteSheetUriProperty = DependencyProperty.Register(
        nameof(SpriteSheetUri), typeof(Uri), typeof(PetdexPreviewControl), new PropertyMetadata(null, OnSourceChanged));
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(PetPreviewSession), typeof(PetdexPreviewControl), new PropertyMetadata(null, OnSourceChanged));
    private BitmapSource? _preview;
    private int _version;
    private bool _failed;

    public PetdexPreviewControl()
    {
        Loaded += (_, _) => LoadPreview();
        IsVisibleChanged += (_, _) => { if (IsVisible && _preview is null) LoadPreview(); };
        Unloaded += (_, _) => _version++;
        MouseLeftButtonUp += (_, e) => { if (_failed) { e.Handled = true; LoadPreview(); } };
        Focusable = true;
        KeyDown += (_, e) => { if (_failed && e.Key is Key.Enter or Key.Space) { e.Handled = true; LoadPreview(); } };
        System.Windows.Automation.AutomationProperties.SetName(this, "桌宠预览；加载失败时可点击重试");
    }

    public Uri? SpriteSheetUri { get => (Uri?)GetValue(SpriteSheetUriProperty); set => SetValue(SpriteSheetUriProperty, value); }
    public PetPreviewSession? Session { get => (PetPreviewSession?)GetValue(SessionProperty); set => SetValue(SessionProperty, value); }
    protected override Size MeasureOverride(Size availableSize) => new(104, 104);

    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (_preview is not null)
        {
            var scale = Math.Min(RenderSize.Width / _preview.PixelWidth, RenderSize.Height / _preview.PixelHeight);
            var size = new Size(_preview.PixelWidth * scale, _preview.PixelHeight * scale);
            drawingContext.DrawImage(_preview, new Rect((RenderSize.Width - size.Width) / 2, (RenderSize.Height - size.Height) / 2, size.Width, size.Height));
            return;
        }
        var text = new FormattedText(_failed ? "点击重试" : "正在加载…", CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 12, Brushes.DimGray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawingContext.DrawText(text, new Point(Math.Max(0, (RenderSize.Width - text.Width) / 2), (RenderSize.Height - text.Height) / 2));
    }

    private static void OnSourceChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
    {
        var control = (PetdexPreviewControl)source;
        if (control.IsLoaded) control.LoadPreview();
    }

    private async void LoadPreview()
    {
        var version = ++_version;
        _preview = null;
        _failed = false;
        Cursor = Cursors.Arrow;
        InvalidateVisual();
        if (SpriteSheetUri is not { Scheme: "https" } uri || Session is null) return;
        try
        {
            var preview = await Session.GetAsync(uri);
            if (version != _version) return;
            _preview = preview;
        }
        catch (OperationCanceledException) { return; }
        catch
        {
            if (version != _version) return;
            _failed = true;
            Cursor = Cursors.Hand;
        }
        InvalidateVisual();
    }
}
