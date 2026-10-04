using HdrImageViewer.Infrastructure;
using Microsoft.UI.Xaml.Media;

namespace HdrImageViewer.Presentation;

public sealed class FilmstripImageItem(string path) : ObservableObject, IFilmstripThumbnailItem<ImageSource>
{
    private bool _isCurrent;
    private ImageSource? _thumbnail;
    private bool _isLoading;
    private bool _hasLoadError;

    public string Path { get; } = path;

    public string FileName { get; } = System.IO.Path.GetFileName(path);

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (SetProperty(ref _thumbnail, value))
                OnPropertyChanged(nameof(HasThumbnail));
        }
    }

    public bool HasThumbnail => Thumbnail is not null;

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool HasLoadError
    {
        get => _hasLoadError;
        set
        {
            if (SetProperty(ref _hasLoadError, value))
                OnPropertyChanged(nameof(PreviewDescription));
        }
    }

    public string PreviewDescription => HasLoadError ? $"{FileName} · 预览不可用，点击打开" : FileName;

    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }
}
