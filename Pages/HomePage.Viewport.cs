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
        if (!ViewModel.HasImage || HdrSwapChainHost.Visibility != Visibility.Visible)
        {
            return;
        }

        ScheduleViewportPresent();
    }

    private void ScheduleViewportPresent()
    {
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
                if (!TryCreateRenderViewport(out var viewport))
                {
                    return;
                }

                effectiveCancellationToken.ThrowIfCancellationRequested();
                await _renderer.RedrawAsync(viewport, effectiveCancellationToken);
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

    private bool TryCreateRenderViewport(out HdrRenderViewport viewport)
    {
        viewport = default;
        if (HdrSwapChainHost.Visibility != Visibility.Visible
            || PreviewSurface.ActualWidth < MinimumSwapChainPixels
            || PreviewSurface.ActualHeight < MinimumSwapChainPixels)
        {
            return false;
        }

        var (pixelWidth, pixelHeight) = ViewerViewportMath.CalculateSwapChainPixelSize(
            PreviewSurface.ActualWidth,
            PreviewSurface.ActualHeight,
            HdrSwapChainHost.CompositionScaleX,
            HdrSwapChainHost.CompositionScaleY);
        if (pixelWidth < MinimumSwapChainPixels || pixelHeight < MinimumSwapChainPixels)
        {
            return false;
        }

        var imageWidth = ImageSurface.Width > 0.0 ? ImageSurface.Width : PreviewSurface.ActualWidth;
        var imageHeight = ImageSurface.Height > 0.0 ? ImageSurface.Height : PreviewSurface.ActualHeight;
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
            ImageScroller?.HorizontalOffset ?? 0.0,
            ImageScroller?.VerticalOffset ?? 0.0);
        if (!layout.IsValid)
        {
            return false;
        }

        viewport = new HdrRenderViewport(
            pixelWidth,
            pixelHeight,
            layout.ScaleX,
            layout.ScaleY,
            layout.OffsetX,
            layout.OffsetY);
        return viewport.IsValid;
    }

    private void ShowHdrSwapChainHost()
    {
        HdrSwapChainHost.Visibility = Visibility.Visible;
        PreviewSurface.UpdateLayout();
        HdrSwapChainHost.UpdateLayout();
    }
}
