using System.Collections.ObjectModel;
using HdrImageViewer.Services;
using Microsoft.UI.Xaml.Media;

namespace HdrImageViewer.Presentation;

internal sealed class FilmstripThumbnailController(
    ObservableCollection<FilmstripImageItem> items, CancellationToken lifetimeToken) : IDisposable
{
    private readonly FilmstripThumbnailControllerCore<FilmstripImageItem, ImageSource> _core = new(
        items,
        (path, token) => PhotoThumbnailService.CreateQuickAsync(path, 192, token),
        (path, token) => PhotoThumbnailService.CreateHdrToneMappedAsync(path, 192, token), lifetimeToken);

    public bool TryGetCached(string path, out ImageSource? thumbnail) => _core.TryGetCached(path, out thumbnail);
    public void QueueLoads(int focusIndex, int firstVisible = -1, int lastVisible = -1) =>
        _core.QueueLoads(focusIndex, firstVisible, lastVisible);
    public void PruneCache(IReadOnlyList<string> folderPaths, int focusIndex) => _core.PruneCache(folderPaths, focusIndex);
    public void Invalidate(string path) => _core.Invalidate(path);
    public void Cancel() => _core.Cancel();
    public void Dispose() => _core.Dispose();
}
