using HdrImageViewer.Presentation;
using HdrImageViewer.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HdrImageViewer.Pages;

public sealed partial class HomePage
{
    private const int ViewportPresentCoalesceMilliseconds = 16;
    private const int MinimumSwapChainPixels = 2;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _viewportPresentTimer;
    private bool _viewportPresentInFlight;
    private bool _viewportPresentDirty;
    private bool _isZoomPreviewActive;
    private bool _isUpdatingSwapChainHostLayout;
    private double _presentedImageWidth;
    private double _presentedImageHeight;
    private double? _layoutScrollX;
    private double? _layoutScrollY;

    private void EnsureViewportPresentTimer()
    {
        if (_viewportPresentTimer is not null)
        {
            return;
        }

        _viewportPresentTimer = DispatcherQueue.CreateTimer();
        _viewportPresentTimer.Interval = TimeSpan.FromMilliseconds(ViewportPresentCoalesceMilliseconds);
        _viewportPresentTimer.IsRepeating = false;
        _viewportPresentTimer.Tick += ViewportPresentTimer_Tick;
    }

    private void ViewportPresentTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        _ = PresentViewportAsync(_lifetime.Token);
    }

    private void ImageScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (!_isZoomPreviewActive && !e.IsIntermediate)
        {
            ClearLayoutScrollOverride();
        }

        if (_isZoomPreviewActive
            || !ViewModel.HasImage
            || HdrSwapChainHost.Visibility != Visibility.Visible)
        {
            return;
        }

        ScheduleViewportPresent();
    }

    private void ScheduleViewportPresent()
    {
        if (_isZoomPreviewActive)
        {
            return;
        }

        _viewportPresentDirty = true;
        if (_viewportPresentInFlight)
        {
            return;
        }

        EnsureViewportPresentTimer();
        _viewportPresentTimer!.Start();
    }

    private async Task PresentViewportAsync(CancellationToken cancellationToken = default)
    {
        var effectiveCancellationToken = cancellationToken == default
            ? _lifetime.Token
            : cancellationToken;
        if (_viewportPresentInFlight)
        {
            _viewportPresentDirty = true;
            return;
        }

        _viewportPresentInFlight = true;
        try
        {
            do
            {
                _viewportPresentDirty = false;
                if (!TryGetCurrentImageSize(out var imageWidth, out var imageHeight))
                {
                    return;
                }

                ApplySwapChainHostPlacement(imageWidth, imageHeight);
                if (!TryCreateRenderViewport(imageWidth, imageHeight, out var viewport))
                {
                    return;
                }

                effectiveCancellationToken.ThrowIfCancellationRequested();
                await _renderer.RedrawAsync(viewport, effectiveCancellationToken);
                RememberPresentedImageSize();
            }
            while (_viewportPresentDirty && !effectiveCancellationToken.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.UpdateRenderStatus($"渲染器调整失败: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _viewportPresentInFlight = false;
            if (_viewportPresentDirty && !effectiveCancellationToken.IsCancellationRequested)
            {
                ScheduleViewportPresent();
            }
        }
    }

    private bool TryGetCurrentImageSize(out double imageWidth, out double imageHeight)
    {
        imageWidth = ImageSurface.Width > 0.0 ? ImageSurface.Width : 0.0;
        imageHeight = ImageSurface.Height > 0.0 ? ImageSurface.Height : 0.0;
        if (imageWidth >= MinimumSwapChainPixels && imageHeight >= MinimumSwapChainPixels)
        {
            return true;
        }

        imageWidth = PreviewSurface.ActualWidth;
        imageHeight = PreviewSurface.ActualHeight;
        return imageWidth >= MinimumSwapChainPixels && imageHeight >= MinimumSwapChainPixels;
    }

    private bool TryCreateRenderViewport(
        double imageWidth,
        double imageHeight,
        out HdrRenderViewport viewport)
    {
        viewport = default;
        if (HdrSwapChainHost.Visibility != Visibility.Visible
            || PreviewSurface.ActualWidth < MinimumSwapChainPixels
            || PreviewSurface.ActualHeight < MinimumSwapChainPixels)
        {
            return false;
        }

        var compositionScaleX = HdrSwapChainHost.CompositionScaleX;
        var compositionScaleY = HdrSwapChainHost.CompositionScaleY;
        if (ViewerViewportMath.ImageFitsInPreview(
            imageWidth,
            imageHeight,
            PreviewSurface.ActualWidth,
            PreviewSurface.ActualHeight))
        {
            var (pixelWidth, pixelHeight) = ViewerViewportMath.CalculateSwapChainPixelSize(
                imageWidth,
                imageHeight,
                compositionScaleX,
                compositionScaleY);
            if (pixelWidth < MinimumSwapChainPixels || pixelHeight < MinimumSwapChainPixels)
            {
                return false;
            }

            viewport = new HdrRenderViewport(pixelWidth, pixelHeight, 1.0f, 1.0f, 0.0f, 0.0f);
            return viewport.IsValid;
        }

        var (coverWidth, coverHeight) = ViewerViewportMath.CalculateSwapChainPixelSize(
            PreviewSurface.ActualWidth,
            PreviewSurface.ActualHeight,
            compositionScaleX,
            compositionScaleY);
        if (coverWidth < MinimumSwapChainPixels || coverHeight < MinimumSwapChainPixels)
        {
            return false;
        }

        var contentWidth = ImageViewport.Width > 0.0
            ? ImageViewport.Width
            : Math.Max(PreviewSurface.ActualWidth, imageWidth);
        var contentHeight = ImageViewport.Height > 0.0
            ? ImageViewport.Height
            : Math.Max(PreviewSurface.ActualHeight, imageHeight);
        var layout = ViewerViewportMath.CalculateVisibleImageLayout(
            PreviewSurface.ActualWidth,
            PreviewSurface.ActualHeight,
            imageWidth,
            imageHeight,
            contentWidth,
            contentHeight,
            _layoutScrollX ?? ImageScroller?.HorizontalOffset ?? 0.0,
            _layoutScrollY ?? ImageScroller?.VerticalOffset ?? 0.0);
        if (!layout.IsValid)
        {
            return false;
        }

        viewport = new HdrRenderViewport(
            coverWidth,
            coverHeight,
            layout.ScaleX,
            layout.ScaleY,
            layout.OffsetX,
            layout.OffsetY);
        return viewport.IsValid;
    }

    private void ApplySwapChainHostPlacement(double imageWidth, double imageHeight)
    {
        if (imageWidth < MinimumSwapChainPixels || imageHeight < MinimumSwapChainPixels)
        {
            return;
        }

        _isUpdatingSwapChainHostLayout = true;
        try
        {
            if (ViewerViewportMath.ImageFitsInPreview(
                imageWidth,
                imageHeight,
                PreviewSurface.ActualWidth,
                PreviewSurface.ActualHeight))
            {
                HdrSwapChainHost.HorizontalAlignment = HorizontalAlignment.Center;
                HdrSwapChainHost.VerticalAlignment = VerticalAlignment.Center;
                HdrSwapChainHost.Width = imageWidth;
                HdrSwapChainHost.Height = imageHeight;
            }
            else
            {
                HdrSwapChainHost.HorizontalAlignment = HorizontalAlignment.Stretch;
                HdrSwapChainHost.VerticalAlignment = VerticalAlignment.Stretch;
                HdrSwapChainHost.ClearValue(FrameworkElement.WidthProperty);
                HdrSwapChainHost.ClearValue(FrameworkElement.HeightProperty);
            }

            HdrSwapChainHost.UpdateLayout();
        }
        finally
        {
            _isUpdatingSwapChainHostLayout = false;
        }
    }

    private void ShowHdrSwapChainHost()
    {
        EndSwapChainZoomPreview();
        HdrSwapChainHost.Visibility = Visibility.Visible;
        PreviewSurface.UpdateLayout();
        HdrSwapChainHost.UpdateLayout();
    }

    private void RememberPresentedImageSize()
    {
        if (ImageSurface.Width > 0.0 && ImageSurface.Height > 0.0)
        {
            _presentedImageWidth = ImageSurface.Width;
            _presentedImageHeight = ImageSurface.Height;
        }
    }

    private void BeginSwapChainZoomPreview()
    {
        _isZoomPreviewActive = true;
        _zoomRenderCts?.Cancel();
    }

    private void ApplySwapChainZoomPreview(
        double targetImageWidth,
        double targetImageHeight,
        double anchorViewportX,
        double anchorViewportY)
    {
        if (HdrSwapChainHost.Visibility != Visibility.Visible)
        {
            return;
        }

        _ = anchorViewportX;
        _ = anchorViewportY;
        ClearSwapChainZoomPreviewTransform();
        ApplySwapChainHostPlacement(targetImageWidth, targetImageHeight);
        if (!ViewerViewportMath.ImageFitsInPreview(
            targetImageWidth,
            targetImageHeight,
            PreviewSurface.ActualWidth,
            PreviewSurface.ActualHeight))
        {
            _ = PresentViewportAsync(_lifetime.Token);
        }
    }

    private void SetLayoutScrollOverride(double scrollX, double scrollY)
    {
        _layoutScrollX = scrollX;
        _layoutScrollY = scrollY;
    }

    private void ClearLayoutScrollOverride()
    {
        _layoutScrollX = null;
        _layoutScrollY = null;
    }

    private void ClearSwapChainZoomPreviewTransform()
    {
        if (SwapChainZoomPreviewTransform is null)
        {
            return;
        }

        SwapChainZoomPreviewTransform.ScaleX = 1.0;
        SwapChainZoomPreviewTransform.ScaleY = 1.0;
        HdrSwapChainHost.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
    }

    private void EndSwapChainZoomPreview()
    {
        _isZoomPreviewActive = false;
        ClearSwapChainZoomPreviewTransform();
    }
}
