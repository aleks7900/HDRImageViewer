namespace HdrImageViewer.Rendering;

public sealed record HdrRendererSettings(
    GainmapViewMode ViewMode,
    HdrHeadroomMode HeadroomMode,
    float? DisplayCapacityOverrideLog2,
    bool AdaptiveToneMappingEnabled,
    float ReferenceWhiteExposureScale,
    ColorGamutMappingMode ColorGamutMappingMode,
    HdrDisplayConfiguration DisplayConfiguration)
{
    public static HdrRendererSettings Default { get; } = new(
        GainmapViewMode.Adaptive,
        HdrHeadroomMode.SystemAdaptive,
        null,
        false,
        1.0f,
        ColorGamutMappingMode.Managed,
        HdrDisplayConfiguration.Unknown);
}
