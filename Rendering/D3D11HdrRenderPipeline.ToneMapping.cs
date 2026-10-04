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
    private void UpdateGainMapConstantsBuffer()
    {
        if (_context is null || _gainMapConstantsBuffer is null)
        {
            return;
        }

        var constants = _gainMapConstants;
        constants.GainMapControl = new Vector4(
            constants.GainMapControl.X,
            constants.GainMapControl.Y,
            constants.GainMapControl.Z,
            0.0f);
        constants.DisplayMapping = new Vector4(
            EffectiveSceneToSdrWhiteScale,
            EffectiveMaxSceneValue,
            EffectiveDisplayBoostLog2,
            _displayConfiguration.MaxFullFrameSceneValue);
        constants.ImageLayout = GetCurrentImageLayout();
        var toneMapState = _toneMapModeCache.GetOrCreate(EffectiveViewModeForCurrentFrame, () =>
        {
            var input = BuildToneMapConstants(constants);
            var output = BuildToneMapOutputConstants();
            return new ToneMapModeState(input, output, _toneMapAnalysis, _toneMappingEnabledForCurrentFrame);
        });
        _toneMapAnalysis = toneMapState.Analysis;
        _toneMappingEnabledForCurrentFrame = toneMapState.Enabled;

        constants.ViewerTools = _viewerToolsConstants;
        constants.ToneMapInput = toneMapState.Input;
        constants.ToneMapOutput = toneMapState.Output;
        constants.ViewModeParams = new Vector4((float)EffectiveViewModeForCurrentFrame, (float)_headroomMode, _referenceWhiteExposureScale, (float)_colorGamutMappingMode);
        _context.UpdateSubresource(in constants, _gainMapConstantsBuffer, 0, 0, 0, null);
    }

    private Vector4 GetCurrentImageLayout()
    {
        return _renderImageLayout ?? CalculateUniformImageLayout(
            _contentPixelWidth,
            _contentPixelHeight,
            _contentOrientation,
            _pixelWidth,
            _pixelHeight);
    }

    private void InvalidateToneMapAnalysis()
    {
        _toneMapModeCache.Clear();
    }

    private Vector4 BuildToneMapConstants(GainMapShaderConstants constants)
    {
        _toneMapAnalysis = default;
        _toneMappingEnabledForCurrentFrame = false;
        var effectiveViewMode = EffectiveViewModeForCurrentFrame;
        var baseHdrImage = _gainMapAnalysisSource is null
            && _primaryAnalysisSource?.IsHdrEncoded == true;
        var gainMapHdrImage = _gainMapAnalysisSource is not null;
        var alternateImageMode = effectiveViewMode == GainmapViewMode.AlternateImage;
        var baseHdrNeedsToneMap = baseHdrImage && !alternateImageMode;
        var gainMapNeedsDisplayFitToneMap = gainMapHdrImage
            && effectiveViewMode == GainmapViewMode.Adaptive;
        var adaptiveToneMapRequested = _adaptiveToneMappingEnabled && !alternateImageMode;
        var useToneMapping = adaptiveToneMapRequested || baseHdrNeedsToneMap || gainMapNeedsDisplayFitToneMap;
        if (!useToneMapping
            || _primaryAnalysisSource is null
            || _contentPixelWidth <= 0
            || _contentPixelHeight <= 0)
        {
            return Vector4.Zero;
        }

        var analysis = _gainMapAnalysisSource is not null
            ? AnalyzeGainMapToneMapInput(constants)
            : AnalyzeBaseHdrToneMapInput(constants);
        if (analysis.VirtualTargetPeak <= 0.0f)
        {
            return Vector4.Zero;
        }

        _toneMapAnalysis = analysis;
        _toneMappingEnabledForCurrentFrame = true;
        return new Vector4(
            1.0f,
            analysis.VirtualTargetPeak,
            analysis.ToneMapPeak,
            analysis.ContentAverage);
    }

    private Vector4 BuildToneMapOutputConstants()
    {
        if (!_toneMappingEnabledForCurrentFrame || _toneMapAnalysis.VirtualTargetPeak <= 0.0f)
        {
            return Vector4.Zero;
        }

        var mode = ToneModeGainMap;
        if (_gainMapAnalysisSource is null && _primaryAnalysisSource?.IsHdrEncoded == true)
        {
            mode = IsSingleLayerDisplayFitToneMapEnabled()
                ? ToneModeSingleLayerDisplayFit
                : ToneModeSingleLayerSystem;
        }

        return new Vector4(
            _toneMapAnalysis.PhysicalTargetPeak,
            mode,
            _toneMapAnalysis.AdaptiveTargetPeak,
            _toneMapAnalysis.GlobalScale);
    }

    private ToneMapAnalysis AnalyzeGainMapToneMapInput(GainMapShaderConstants constants)
    {
        if (_gainMapAnalysisSource is null)
        {
            return default;
        }

        var weight = CalculateGainMapWeightForStatus();
        var whiteScale = Math.Max(EffectiveSceneToSdrWhiteScale, 1.0f);
        var sceneScale = CalculateGainMapSceneScale(constants);
        var virtualTargetPeak = CalculateBaseHdrVirtualTargetPeak(constants, whiteScale);
        var manualTarget = _displayCapacityOverrideLog2 is not null && !_adaptiveToneMappingEnabled;
        var effectiveMaxSceneValue = EffectiveMaxSceneValue;
        var physicalTargetPeak = manualTarget
            ? virtualTargetPeak
            : effectiveMaxSceneValue > 0.0f
                ? Math.Min(effectiveMaxSceneValue, virtualTargetPeak)
                : virtualTargetPeak;
        var peakSamples = ArrayPool<float>.Shared.Rent(_gainMapAnalysisSource.Samples.Length);
        try
        {
            double luminanceSum = 0.0;
            var contentPeak = 0.0f;
            var validSampleCount = 0;

            foreach (var sample in _gainMapAnalysisSource.Samples)
            {
                var hdr = constants.GainMapControl.Y > 0.5f
                    ? HdrColorMath.ReconstructAppleHdrSample(sample.Sdr, sample.Gain, constants.GainMapMax.X, weight)
                    : HdrColorMath.ReconstructAdobeHdrSample(sample.Sdr, sample.Gain, constants, weight);
                hdr = HdrColorMath.ConvertGainMapBaseToBt709(hdr, constants, _colorGamutMappingMode);
                hdr *= sceneScale;

                if (!float.IsFinite(hdr.X) || !float.IsFinite(hdr.Y) || !float.IsFinite(hdr.Z))
                {
                    continue;
                }

                var samplePeak = Math.Max(hdr.X, Math.Max(hdr.Y, hdr.Z));
                contentPeak = Math.Max(contentPeak, samplePeak);
                peakSamples[validSampleCount++] = samplePeak;
                luminanceSum += Math.Max(0.0f, (0.2126f * hdr.X) + (0.7152f * hdr.Y) + (0.0722f * hdr.Z));
            }

            var average = validSampleCount > 0
                ? (float)(luminanceSum / validSampleCount)
                : 0.0f;
            var highPercentilePeak = CalculatePercentile(peakSamples, validSampleCount, 0.995f);
            var toneMapPeak = Math.Max(highPercentilePeak, contentPeak * 0.55f);
            var fullFrameLimit = _displayConfiguration.MaxFullFrameSceneValue;
            var adaptiveTargetPeak = _adaptiveToneMappingEnabled
                ? CalculateAdaptiveToneMapTarget(
                    whiteScale,
                    virtualTargetPeak,
                    physicalTargetPeak,
                    average,
                    fullFrameLimit)
                : physicalTargetPeak;
            var globalScale = _adaptiveToneMappingEnabled
                ? CalculateGlobalToneMapScale(adaptiveTargetPeak, toneMapPeak, average, fullFrameLimit)
                : 1.0f;
            return new ToneMapAnalysis(
                contentPeak,
                highPercentilePeak,
                toneMapPeak,
                average,
                virtualTargetPeak,
                physicalTargetPeak,
                adaptiveTargetPeak,
                fullFrameLimit,
                globalScale);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(peakSamples);
        }
    }

    private ToneMapAnalysis AnalyzeBaseHdrToneMapInput(GainMapShaderConstants constants)
    {
        if (_primaryAnalysisSource is null || !_primaryAnalysisSource.IsHdrEncoded)
        {
            return default;
        }

        var whiteScale = CalculateBaseHdrToneMapWhiteScale(constants);
        var virtualTargetPeak = CalculateBaseHdrVirtualTargetPeak(constants, whiteScale);
        var decodeTargetPeak = constants.SourceEncoding.X > 1.5f && constants.SourceEncoding.X < 2.5f
            ? HlgReferenceScenePeak
            : virtualTargetPeak;
        var sliderTarget = _displayCapacityOverrideLog2 is not null;
        var displayFitToneMap = _adaptiveToneMappingEnabled || sliderTarget;
        var displayLimitedTargetPeak = EffectiveMaxSceneValue > 0.0f
            ? Math.Min(EffectiveMaxSceneValue, virtualTargetPeak)
            : virtualTargetPeak;
        var physicalTargetPeak = sliderTarget && !_adaptiveToneMappingEnabled
            ? virtualTargetPeak
            : displayLimitedTargetPeak;
        var peakSamples = ArrayPool<float>.Shared.Rent(_primaryAnalysisSource.Samples.Length);
        try
        {
            double luminanceSum = 0.0;
            var contentPeak = 0.0f;
            var validSampleCount = 0;

            foreach (var sample in _primaryAnalysisSource.Samples)
            {
                var hdr = ReconstructBaseHdrSample(sample, constants, decodeTargetPeak);
                if (!float.IsFinite(hdr.X) || !float.IsFinite(hdr.Y) || !float.IsFinite(hdr.Z))
                {
                    continue;
                }

                var samplePeak = Math.Max(0.0f, Math.Max(hdr.X, Math.Max(hdr.Y, hdr.Z)));
                contentPeak = Math.Max(contentPeak, samplePeak);
                peakSamples[validSampleCount++] = samplePeak;
                luminanceSum += Math.Max(0.0f, (0.2126f * hdr.X) + (0.7152f * hdr.Y) + (0.0722f * hdr.Z));
            }

            var average = validSampleCount > 0
                ? (float)(luminanceSum / validSampleCount)
                : 0.0f;
            var highPercentilePeak = CalculatePercentile(peakSamples, validSampleCount, 0.995f);
            var measuredToneMapPeak = Math.Max(highPercentilePeak, contentPeak * 0.55f);
            var toneMapPeak = displayFitToneMap
                ? Math.Clamp(
                    measuredToneMapPeak,
                    Math.Max(whiteScale, physicalTargetPeak),
                    Math.Max(Math.Max(virtualTargetPeak, physicalTargetPeak), whiteScale))
                : measuredToneMapPeak;
            var fullFrameLimit = _displayConfiguration.MaxFullFrameSceneValue;
            var adaptiveTargetPeak = displayFitToneMap
                ? CalculateAdaptiveToneMapTarget(
                    whiteScale,
                    virtualTargetPeak,
                    physicalTargetPeak,
                    average,
                    fullFrameLimit)
                : physicalTargetPeak;
            var globalScale = displayFitToneMap
                ? CalculateSingleLayerDisplayFitMidScale(average, whiteScale, fullFrameLimit)
                : 1.0f;
            return new ToneMapAnalysis(
                contentPeak,
                highPercentilePeak,
                toneMapPeak,
                average,
                virtualTargetPeak,
                physicalTargetPeak,
                adaptiveTargetPeak,
                fullFrameLimit,
                globalScale);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(peakSamples);
        }
    }

    private float CalculateBaseHdrToneMapWhiteScale(GainMapShaderConstants constants)
    {
        return EffectiveViewModeForCurrentFrame == GainmapViewMode.Sdr
            ? Math.Max(EffectiveSceneToSdrWhiteScale, 1.0f)
            : Math.Max(CalculateBaseHdrContentWhiteScale(constants), 1.0f);
    }

    private float CalculateBaseHdrContentWhiteScale(GainMapShaderConstants constants)
    {
        var exposure = Math.Max(_referenceWhiteExposureScale, 0.0f);
        return constants.SourceEncoding.X switch
        {
            > 4.5f => SingleLayerHdrReferenceWhiteScale * exposure,
            > 3.5f => (constants.SourceEncoding.Y <= 0.5f ? Math.Max(constants.DisplayMapping.X, 1.0f) : 1.0f) * exposure,
            > 2.5f => SingleLayerHdrReferenceWhiteScale * exposure,
            > 1.5f => HlgReferenceWhiteScene * exposure,
            _ => Math.Max(constants.DisplayMapping.X, 1.0f),
        };
    }

    private float CalculateBaseHdrSdrPreviewScale(GainMapShaderConstants constants)
    {
        if (EffectiveViewModeForCurrentFrame != GainmapViewMode.Sdr)
        {
            return 1.0f;
        }

        return Math.Max(EffectiveSceneToSdrWhiteScale, 1.0f)
            / Math.Max(CalculateBaseHdrContentWhiteScale(constants), 0.0001f);
    }

    private float CalculateBaseHdrVirtualTargetPeak(GainMapShaderConstants constants, float whiteScale)
    {
        if (EffectiveViewModeForCurrentFrame == GainmapViewMode.Sdr)
        {
            return Math.Max(EffectiveSceneToSdrWhiteScale, 1.0f);
        }

        if (_displayCapacityOverrideLog2 is { } overrideStops)
        {
            var targetNits = _displayConfiguration.SdrWhiteLevelInNits * Math.Pow(2.0, overrideStops);
            return Math.Max((float)(targetNits / HdrColorMath.ReferenceWhiteNits), whiteScale);
        }

        if (EffectiveMaxSceneValue > 0.0f)
        {
            return Math.Max(EffectiveMaxSceneValue, whiteScale);
        }

        if (constants.SourceEncoding.X > 1.5f && constants.SourceEncoding.X < 2.5f)
        {
            return Math.Max(HlgReferenceScenePeak, whiteScale);
        }

        return Math.Max(
            whiteScale * MathF.Pow(2.0f, Math.Max(EffectiveDisplayBoostLog2, 0.0f)),
            whiteScale);
    }

    private Vector3 ReconstructBaseHdrSample(
        Vector3 sample,
        GainMapShaderConstants constants,
        float targetScenePeak)
    {
        var linear = constants.SourceEncoding.X switch
        {
            > 4.5f => sample,
            > 3.5f => sample,
            > 2.5f => HdrColorMath.PqToSceneLinear(sample),
            > 1.5f => HdrColorMath.HlgToSceneLinear(sample, targetScenePeak),
            _ => sample,
        };

        var p709 = constants.SourceEncoding.Y switch
        {
            > 2.5f => HdrColorMath.ConvertProPhotoToBt709(linear, _colorGamutMappingMode),
            > 1.5f => HdrColorMath.ConvertBt2020ToBt709(linear, _colorGamutMappingMode),
            > 0.5f => HdrColorMath.ConvertP3ToBt709(linear, _colorGamutMappingMode),
            _ => linear,
        };

        var mapped = constants.SourceEncoding.X > 3.5f && constants.SourceEncoding.X < 4.5f && constants.SourceEncoding.Y <= 0.5f
            ? p709 * Math.Max(constants.DisplayMapping.X, 1.0f)
            : p709;
        if (constants.SourceEncoding.X > 1.5f)
        {
            mapped *= Math.Max(_referenceWhiteExposureScale, 0.0f);
            mapped *= CalculateBaseHdrSdrPreviewScale(constants);
        }

        return mapped;
    }

    private static Vector3 ReadEncodedRgb(DecodedBitmap bitmap, int x, int y)
    {
        var index = checked(((y * bitmap.PixelWidth) + x) * bitmap.BytesPerPixel);
        if (bitmap.PixelFormat == DecodedBitmapPixelFormat.Rgba16Float)
        {
            return new Vector3(
                ReadHalfLittleEndian(bitmap.RgbaPixels, index),
                ReadHalfLittleEndian(bitmap.RgbaPixels, index + 2),
                ReadHalfLittleEndian(bitmap.RgbaPixels, index + 4));
        }

        if (bitmap.PixelFormat == DecodedBitmapPixelFormat.Rgba16Unorm)
        {
            return new Vector3(
                ReadUInt16LittleEndian(bitmap.RgbaPixels, index) / 65535.0f,
                ReadUInt16LittleEndian(bitmap.RgbaPixels, index + 2) / 65535.0f,
                ReadUInt16LittleEndian(bitmap.RgbaPixels, index + 4) / 65535.0f);
        }

        return new Vector3(
            bitmap.RgbaPixels[index] / 255.0f,
            bitmap.RgbaPixels[index + 1] / 255.0f,
            bitmap.RgbaPixels[index + 2] / 255.0f);
    }

    private static float ReadHalfLittleEndian(byte[] data, int offset)
    {
        var bits = unchecked((ushort)(data[offset] | (data[offset + 1] << 8)));
        return (float)BitConverter.UInt16BitsToHalf(bits);
    }

    private static Vector3 ReadLinearSrgb(DecodedBitmap bitmap, int x, int y)
    {
        var encoded = ReadEncodedRgb(bitmap, x, y);
        return HdrColorMath.SrgbToLinear(encoded);
    }

    private static Vector3 ReadGainMapSample(DecodedBitmap bitmap, int primaryX, int primaryY, int primaryWidth, int primaryHeight)
    {
        var x = Math.Clamp((int)((primaryX + 0.5f) * bitmap.PixelWidth / Math.Max(primaryWidth, 1)), 0, bitmap.PixelWidth - 1);
        var y = Math.Clamp((int)((primaryY + 0.5f) * bitmap.PixelHeight / Math.Max(primaryHeight, 1)), 0, bitmap.PixelHeight - 1);
        return ReadEncodedRgb(bitmap, x, y);
    }

    private static ushort ReadUInt16LittleEndian(byte[] data, int offset)
    {
        return (ushort)(data[offset] | (data[offset + 1] << 8));
    }

    private static float CalculateAdaptiveToneMapTarget(
        float whiteScale,
        float virtualTarget,
        float physicalTarget,
        float contentAverage,
        float fullFrameLimit)
    {
        var outputTarget = Math.Clamp(physicalTarget, whiteScale, Math.Max(virtualTarget, whiteScale));
        var headroom = Math.Max(outputTarget - whiteScale, 0.0f);
        if (headroom <= 0.0f)
        {
            return outputTarget;
        }

        var averageRelativeToWhite = contentAverage / Math.Max(whiteScale, 0.0001f);
        var aplFactor = 1.0f / (1.0f + (1.6f * Math.Max(averageRelativeToWhite - 0.18f, 0.0f)));
        var minimumHeadroomFraction = fullFrameLimit > 0.0f && fullFrameLimit < whiteScale
            ? 0.42f
            : 0.28f;
        var target = whiteScale + (headroom * Math.Max(aplFactor, minimumHeadroomFraction));

        if (fullFrameLimit > 0.0f && outputTarget > whiteScale)
        {
            var fullFrameTarget = fullFrameLimit < whiteScale
                ? whiteScale + (headroom * 0.42f)
                : Math.Clamp(fullFrameLimit * 1.45f, whiteScale + (headroom * minimumHeadroomFraction), outputTarget);
            var pressureStart = Math.Min(whiteScale * 0.55f, Math.Max(fullFrameLimit * 0.85f, whiteScale * 0.25f));
            var fullFramePressure = Math.Clamp(
                (contentAverage - pressureStart) / Math.Max(fullFrameLimit - pressureStart, 0.0001f),
                0.0f,
                1.0f);
            target = Math.Min(target, Lerp(outputTarget, fullFrameTarget, fullFramePressure));
        }

        return Math.Clamp(target, whiteScale + (headroom * minimumHeadroomFraction), outputTarget);
    }

    private static float CalculateGlobalToneMapScale(
        float target,
        float toneMapPeak,
        float contentAverage,
        float fullFrameLimit)
    {
        var scale = toneMapPeak > target && toneMapPeak > 0.0f
            ? target / toneMapPeak
            : 1.0f;

        if (fullFrameLimit > 0.0f && contentAverage > fullFrameLimit)
        {
            scale = Math.Min(scale, fullFrameLimit / contentAverage);
        }

        return Math.Clamp(scale, 0.02f, 1.0f);
    }

    private static float CalculateSingleLayerDisplayFitMidScale(
        float contentAverage,
        float whiteScale,
        float fullFrameLimit)
    {
        var scale = 1.0f;
        if (fullFrameLimit > 0.0f && contentAverage > fullFrameLimit)
        {
            scale = Math.Min(scale, fullFrameLimit / contentAverage);
        }

        var averageRelativeToWhite = contentAverage / Math.Max(whiteScale, 0.0001f);
        if (averageRelativeToWhite > 0.45f)
        {
            scale = Math.Min(scale, 1.0f / (1.0f + (0.70f * (averageRelativeToWhite - 0.45f))));
        }

        return Math.Clamp(scale, 0.25f, 1.0f);
    }

    private static float CalculatePercentile(
        float[] samples,
        int sampleCount,
        float percentile)
    {
        if (sampleCount <= 0)
        {
            return 0.0f;
        }

        Array.Sort(samples, 0, sampleCount);
        var index = (int)MathF.Round((sampleCount - 1) * Math.Clamp(percentile, 0.0f, 1.0f));
        return samples[Math.Clamp(index, 0, sampleCount - 1)];
    }

    private static float Lerp(float start, float end, float amount)
    {
        return start + ((end - start) * amount);
    }

    private static Vector4 CalculateUniformImageLayout(
        int contentPixelWidth,
        int contentPixelHeight,
        float orientation,
        int targetPixelWidth,
        int targetPixelHeight)
    {
        if (contentPixelWidth <= 0 || contentPixelHeight <= 0 || targetPixelWidth <= 0 || targetPixelHeight <= 0)
        {
            return new Vector4(1.0f, 1.0f, 0.0f, 0.0f);
        }

        var displayedWidth = contentPixelWidth;
        var displayedHeight = contentPixelHeight;
        if (OrientationSwapsDimensions(orientation))
        {
            displayedWidth = contentPixelHeight;
            displayedHeight = contentPixelWidth;
        }

        var contentAspect = (float)displayedWidth / displayedHeight;
        var targetAspect = (float)targetPixelWidth / targetPixelHeight;
        var scaleX = 1.0f;
        var scaleY = 1.0f;
        if (targetAspect > contentAspect)
        {
            scaleX = contentAspect / targetAspect;
        }
        else
        {
            scaleY = targetAspect / contentAspect;
        }

        return new Vector4(
            scaleX,
            scaleY,
            (1.0f - scaleX) * 0.5f,
            (1.0f - scaleY) * 0.5f);
    }

    private static bool OrientationSwapsDimensions(float orientation)
    {
        return orientation is >= 4.5f and < 8.5f;
    }
}
