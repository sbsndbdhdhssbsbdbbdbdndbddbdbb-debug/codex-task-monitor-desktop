using System.IO;
using System.Windows.Media.Imaging;
using CodexTaskMonitor.Core;

namespace CodexTaskMonitor.App;

// One window owns a bounded thumbnail cache; closing it cancels all pending work.
public sealed class PetPreviewSession : IDisposable
{
    private readonly PetdexCatalogClient _client;
    private readonly SemaphoreSlim _slots = new(3);
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource _page;
    private readonly Dictionary<Uri, Task<BitmapSource>> _cache = [];
    private readonly Queue<Uri> _order = [];

    public PetPreviewSession(PetdexCatalogClient client)
    {
        _client = client;
        _page = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
    }

    public void CancelPending()
    {
        _page.Cancel();
        _page.Dispose();
        _page = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        foreach (var key in _cache.Where(pair => !pair.Value.IsCompletedSuccessfully).Select(pair => pair.Key).ToArray()) _cache.Remove(key);
    }

    public Task<BitmapSource> GetAsync(Uri uri)
    {
        if (_cache.TryGetValue(uri, out var cached) && !cached.IsFaulted && !cached.IsCanceled) return cached;
        var task = LoadAsync(uri, _page.Token);
        _cache[uri] = task;
        _order.Enqueue(uri);
        while (_order.Count > 48) _cache.Remove(_order.Dequeue());
        return task;
    }

    private async Task<BitmapSource> LoadAsync(Uri uri, CancellationToken token)
    {
        await _slots.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var bytes = await _client.DownloadPreviewSpriteAsync(uri, token).ConfigureAwait(false);
            return await Task.Run<BitmapSource>(() =>
            {
                token.ThrowIfCancellationRequested();
                using var stream = new MemoryStream(PetSpriteSheetNormalizer.CreatePreview(bytes));
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }, token).ConfigureAwait(false);
        }
        finally { _slots.Release(); }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _page.Dispose();
        _cache.Clear();
        _order.Clear();
        _lifetime.Dispose();
    }
}
