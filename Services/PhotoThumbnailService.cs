using System.Runtime.InteropServices.WindowsRuntime;
using HdrImageViewer.Models;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace HdrImageViewer.Services;

public static class PhotoThumbnailService
{
    public static async Task<ImageSource?> CreateAsync(
        string path,
        uint maxPixelSize = 256,
        CancellationToken cancellationToken = default)
    {
        return await CreateHdrToneMappedAsync(path, maxPixelSize, cancellationToken)
            ?? await CreateQuickAsync(path, maxPixelSize, cancellationToken);
    }

    public static async Task<ImageSource?> CreateHdrToneMappedAsync(
        string path,
        uint maxPixelSize = 256,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var loadResult = await ImagePreloadCache.GetLoadResultAsync(
                path,
                cancellationToken);
            return await CreateHdrToneMappedAsync(
                loadResult,
                maxPixelSize,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    internal static async Task<ImageSource?> CreateHdrToneMappedAsync(
        ImageLoadResult loadResult,
        uint maxPixelSize,
        CancellationToken cancellationToken)
    {
        var document = loadResult.Document;
        if (document.HasRenderableGainMap)
        {
            var inputs = await DecodeGainMapThumbnailInputsAsync(
                document,
                checked((int)Math.Clamp(maxPixelSize, 1u, int.MaxValue)),
                cancellationToken);
            return await CreateToneMappedThumbnailAsync(inputs, cancellationToken);
        }

        var bitmap = await DecodeDocumentThumbnailAsync(document, maxPixelSize, cancellationToken);
        if (bitmap is not null)
        {
            if (bitmap.IsHdrEncoded)
            {
                return await CreateToneMappedThumbnailAsync(bitmap, cancellationToken);
            }

            // Native decoders are also required for SDR JPEG XL. Returning null
            // here would discard its decoded pixels and retry the unsupported
            // shell/WinRT path instead.
            return await CreateSdrThumbnailAsync(bitmap, cancellationToken);
        }

        return null;
    }

    internal static async Task<DecodedBitmap?> DecodeDocumentThumbnailAsync(
        HdrImageDocument document, uint maxPixelSize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ThumbnailDecodePolicy.RequiresPixelDecode(document)
            ? await BitmapDecodeService.DecodeDocumentForThumbnailAsync(
                document, checked((int)Math.Clamp(maxPixelSize, 1u, int.MaxValue)), cancellationToken)
            : null;
    }

    public static async Task<ImageSource?> CreateQuickAsync(
        string path,
        uint maxPixelSize = 256,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // SDR images (and quick placeholders for HDR images) use the Windows
        // shell thumbnail: it comes from the OS thumbnail cache, so repeat visits
        // avoid decoding the original file at all. Decode an explicit stream
        // when the shell cannot produce a thumbnail; arbitrary file:// URIs
        // can fail silently in a packaged app.
        return await TryCreateShellThumbnailAsync(path, maxPixelSize, cancellationToken)
            ?? await TryCreateStreamThumbnailAsync(path, maxPixelSize, cancellationToken);
    }

    private static async Task<ImageSource?> TryCreateShellThumbnailAsync(
        string path,
        uint maxPixelSize,
        CancellationToken cancellationToken)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            cancellationToken.ThrowIfCancellationRequested();
            using var thumbnail = await file.GetThumbnailAsync(
                ThumbnailMode.PicturesView,
                maxPixelSize,
                ThumbnailOptions.UseCurrentScale);
            cancellationToken.ThrowIfCancellationRequested();
            if (thumbnail is null || thumbnail.Size == 0 || thumbnail.Type != ThumbnailType.Image)
            {
                return null;
            }

            var source = new BitmapImage();
            await source.SetSourceAsync(thumbnail);
            cancellationToken.ThrowIfCancellationRequested();
            return source;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static Task<GainMapRenderInputs> DecodeGainMapThumbnailInputsAsync(
        HdrImageDocument document,
        int maxPixelSize,
        CancellationToken cancellationToken)
    {
        return GainMapRenderInputDecoder.DecodeRenderInputsAsync(document, maxPixelSize, cancellationToken);
    }

    private static async Task<ImageSource?> CreateToneMappedThumbnailAsync(
        GainMapRenderInputs inputs,
        CancellationToken cancellationToken)
    {
        var pixels = await Task.Run(() => ThumbnailPixelConverter.ConvertGainMapBaseToBgra8(inputs, cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var orientation = new ExifOrientationTransform(
            inputs.Primary.PixelWidth, inputs.Primary.PixelHeight, (int)inputs.Constants.Orientation.X);
        var source = new WriteableBitmap(orientation.Width, orientation.Height);
        using (var stream = source.PixelBuffer.AsStream())
        {
            await stream.WriteAsync(pixels.AsMemory(0, pixels.Length), cancellationToken);
        }

        source.Invalidate();
        return source;
    }

    private static async Task<ImageSource?> CreateToneMappedThumbnailAsync(
        DecodedBitmap bitmap,
        CancellationToken cancellationToken)
    {
        var pixels = await Task.Run(() => ThumbnailPixelConverter.ConvertHdrPreviewToBgra8(bitmap, cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var source = new WriteableBitmap(bitmap.PixelWidth, bitmap.PixelHeight);
        using (var stream = source.PixelBuffer.AsStream())
        {
            await stream.WriteAsync(pixels.AsMemory(0, pixels.Length), cancellationToken);
        }

        source.Invalidate();
        return source;
    }

    private static async Task<ImageSource?> CreateSdrThumbnailAsync(
        DecodedBitmap bitmap,
        CancellationToken cancellationToken)
    {
        var pixels = await Task.Run(() => ThumbnailPixelConverter.ConvertSdrToBgra8(bitmap, cancellationToken, premultiplyAlpha: true), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var source = new WriteableBitmap(bitmap.PixelWidth, bitmap.PixelHeight);
        using (var stream = source.PixelBuffer.AsStream())
        {
            await stream.WriteAsync(pixels.AsMemory(0, pixels.Length), cancellationToken);
        }

        source.Invalidate();
        return source;
    }

    private static async Task<ImageSource?> TryCreateStreamThumbnailAsync(
        string path, uint maxPixelSize, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            cancellationToken.ThrowIfCancellationRequested();
            var width = decoder.OrientedPixelWidth;
            var height = decoder.OrientedPixelHeight;
            var scale = Math.Min(1.0, Math.Max(1u, maxPixelSize) / (double)Math.Max(width, height));
            var source = new BitmapImage
            {
                DecodePixelWidth = Math.Max(1, checked((int)Math.Round(width * scale))),
                DecodePixelHeight = Math.Max(1, checked((int)Math.Round(height * scale))),
            };
            stream.Seek(0);
            await source.SetSourceAsync(stream);
            cancellationToken.ThrowIfCancellationRequested();
            return source;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
