using System.Buffers;
using System.Numerics;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HdrImageViewer.Models;
using HdrImageViewer.Services;
using Microsoft.UI.Xaml.Controls;
using SharpGen.Runtime;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Vortice.WIC;
using D2D = Vortice.Direct2D1.D2D1;
using D2DColorManagement = Vortice.Direct2D1.Effects.ColorManagement;
using D2DHdrToneMap = Vortice.Direct2D1.Effects.HdrToneMap;
using D2DWhiteLevelAdjustment = Vortice.Direct2D1.Effects.WhiteLevelAdjustment;
using D3DFeatureLevel = Vortice.Direct3D.FeatureLevel;
using DCommonAlphaMode = Vortice.DCommon.AlphaMode;
using DCommonPixelFormat = Vortice.DCommon.PixelFormat;
using WicPixelFormat = Vortice.WIC.PixelFormat;

namespace HdrImageViewer.Rendering;

public sealed partial class D3D11HdrRenderPipeline
{
    private void EnsureDevice()
    {
        if (_device is not null)
        {
            return;
        }

        _device = D3D11.D3D11CreateDevice(
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            FeatureLevels);
        _deviceGeneration++;
        _context = _device.ImmediateContext;

        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();
        _factory = adapter.GetParent<IDXGIFactory2>();
        EnsureDirect2DDevice(dxgiDevice);
    }

    private void EnsureDirect2DDevice(IDXGIDevice dxgiDevice)
    {
        if (_d2dContext is not null)
        {
            return;
        }

        _d2dFactory = D2D.D2D1CreateFactory<ID2D1Factory1>(FactoryType.MultiThreaded, DebugLevel.None);
        _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
        _d2dContext = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
        _d2dContext.UnitMode = UnitMode.Pixels;
        _d2dContext2 = _d2dContext.QueryInterfaceOrNull<ID2D1DeviceContext2>();
        _d2dContext5 = _d2dContext.QueryInterfaceOrNull<ID2D1DeviceContext5>();
        _wicFactory = new IWICImagingFactory();
    }

    private void CreateSwapChain(int pixelWidth, int pixelHeight)
    {
        if (_panel is null || _device is null || _factory is null)
        {
            return;
        }

        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;

        var description = new SwapChainDescription1(
            (uint)pixelWidth,
            (uint)pixelHeight,
            Format.R16G16B16A16_Float,
            stereo: false,
            Usage.RenderTargetOutput,
            bufferCount: 2,
            Scaling.Stretch,
            SwapEffect.FlipSequential,
            AlphaMode.Ignore,
            SwapChainFlags.None);

        _swapChain = _factory.CreateSwapChainForComposition(_device, description, null);
        _swapChain2 = _swapChain.QueryInterfaceOrNull<IDXGISwapChain2>();
        _swapChain3 = _swapChain.QueryInterfaceOrNull<IDXGISwapChain3>();
        _frameVerificationPending = true;
        ConfigureScRgbColorSpace();
        ConfigureSwapChainPanelScale();

        if (!TryBindSwapChainToPanel())
        {
            return;
        }

        CreateRenderTargetView();
    }

    private void ResizeSwapChain(int pixelWidth, int pixelHeight)
    {
        if (_swapChain is null)
        {
            return;
        }

        ReleaseD2DTargetBitmap();
        _renderTargetView?.Dispose();
        _renderTargetView = null;

        _swapChain.ResizeBuffers(
            bufferCount: 2,
            width: (uint)pixelWidth,
            height: (uint)pixelHeight,
            newFormat: Format.R16G16B16A16_Float,
            swapChainFlags: SwapChainFlags.None).CheckError();

        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;
        _frameVerificationPending = true;
        ConfigureScRgbColorSpace();
        ConfigureSwapChainPanelScale();
        if (!TryBindSwapChainToPanel())
        {
            return;
        }

        CreateRenderTargetView();
    }

    private void ConfigureScRgbColorSpace()
    {
        _scRgbColorSpaceAvailable = false;
        _scRgbColorSpaceApplied = false;

        if (_swapChain3 is null)
        {
            return;
        }

        var support = _swapChain3.CheckColorSpaceSupport(ColorSpaceType.RgbFullG10NoneP709);
        _scRgbColorSpaceAvailable = (support & SwapChainColorSpaceSupportFlags.Present) == SwapChainColorSpaceSupportFlags.Present;
        if (_scRgbColorSpaceAvailable)
        {
            _swapChain3.SetColorSpace1(ColorSpaceType.RgbFullG10NoneP709);
            _scRgbColorSpaceApplied = true;
        }
    }

    private void ConfigureSwapChainPanelScale()
    {
        if (_panel is null || _swapChain2 is null)
        {
            _swapChainTransformStatus = "swap chain DPI transform unavailable";
            return;
        }

        var scaleX = Math.Max(0.0001f, _panel.CompositionScaleX);
        var scaleY = Math.Max(0.0001f, _panel.CompositionScaleY);
        var transform = Matrix3x2.CreateScale(1.0f / scaleX, 1.0f / scaleY);
        _swapChain2.MatrixTransform = transform;
        _swapChainTransformStatus = $"DPI transform {1.0f / scaleX:0.###}x{1.0f / scaleY:0.###} for scale {scaleX:0.###}x{scaleY:0.###}";
    }

    private void CreateRenderTargetView()
    {
        if (_device is null || _swapChain is null)
        {
            LastRenderStatus = "Create render target skipped: device or swap chain missing";
            return;
        }

        using var backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _renderTargetView = _device.CreateRenderTargetView(backBuffer, null);
        CreateD2DTargetBitmap();
    }

    private void CreateD2DTargetBitmap()
    {
        if (_d2dContext is null || _swapChain is null)
        {
            return;
        }

        ReleaseD2DTargetBitmap();
        using var backBuffer = _swapChain.GetBuffer<IDXGISurface>(0);
        var properties = new BitmapProperties1(
            new DCommonPixelFormat(Format.R16G16B16A16_Float, DCommonAlphaMode.Ignore),
            96.0f,
            96.0f,
            BitmapOptions.Target | BitmapOptions.CannotDraw);
        _d2dTargetBitmap = _d2dContext.CreateBitmapFromDxgiSurface(backBuffer, properties);
        _d2dContext.Target = _d2dTargetBitmap;
    }

    private void ReleaseD2DTargetBitmap()
    {
        if (_d2dContext is not null)
        {
            _d2dContext.Target = null;
        }

        _d2dTargetBitmap?.Dispose();
        _d2dTargetBitmap = null;
    }

    private void PresentProbeFrame()
    {
        EnsureRenderTargetView();

        if (_context is null || _swapChain is null || _renderTargetView is null)
        {
            LastRenderStatus = "Probe frame skipped: D3D resources missing";
            return;
        }

        var color = new Color4(1.35f, 1.35f, 1.35f, 1.0f);
        _context.ClearRenderTargetView(_renderTargetView, color);
        _context.Flush();
        _swapChain.Present(1, PresentFlags.None).CheckError();
        LastRenderStatus = $"Probe frame presented at {_pixelWidth}x{_pixelHeight}; {BuildOutputSummary()}";
    }


    private bool TryBindSwapChainToPanel()
    {
        if (_panel is null || _swapChain is null)
        {
            _panelBindingStatus = "WinUI swap chain bind skipped";
            LastRenderStatus = "Swap chain panel bind skipped: panel or swap chain missing";
            return false;
        }

        try
        {
            SetSwapChainOnPanel(_panel, _swapChain.NativePointer);
            _panelBindingStatus = "WinUI swap chain bound";
            IsSwapChainPanelBound = true;
            return true;
        }
        catch (Exception ex)
        {
            _panelBindingStatus = $"WinUI swap chain bind failed: {ex.GetType().Name}";
            IsSwapChainPanelBound = false;
            LastRenderStatus = $"Swap chain panel bind failed: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private void DetachSwapChainFromPanel()
    {
        if (_panel is null)
        {
            return;
        }

        try
        {
            SetSwapChainOnPanel(_panel, IntPtr.Zero);
            IsSwapChainPanelBound = false;
        }
        catch
        {
            // Best-effort cleanup; the panel may already be leaving the XAML tree.
        }
    }

    private static void SetSwapChainOnPanel(SwapChainPanel panel, IntPtr swapChain)
    {
        var unknown = Marshal.GetIUnknownForObject(panel);
        var panelNative = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in WinUiSwapChainPanelNativeGuid, out panelNative));
            var vtable = Marshal.ReadIntPtr(panelNative);
            var setSwapChainPointer = Marshal.ReadIntPtr(vtable, IntPtr.Size * 3);
            var setSwapChain = Marshal.GetDelegateForFunctionPointer<SetSwapChainDelegate>(setSwapChainPointer);
            Marshal.ThrowExceptionForHR(setSwapChain(panelNative, swapChain));
        }
        finally
        {
            if (panelNative != IntPtr.Zero)
            {
                Marshal.Release(panelNative);
            }

            Marshal.Release(unknown);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetSwapChainDelegate(IntPtr panelNative, IntPtr swapChain);
}
