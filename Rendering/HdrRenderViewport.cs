using System.Numerics;

namespace HdrImageViewer.Rendering;

internal readonly record struct HdrRenderViewport(
    int PixelWidth,
    int PixelHeight,
    float ImageScaleX,
    float ImageScaleY,
    float ImageOffsetX,
    float ImageOffsetY)
{
    public bool IsValid =>
        PixelWidth > 0
        && PixelHeight > 0
        && float.IsFinite(ImageScaleX)
        && float.IsFinite(ImageScaleY)
        && ImageScaleX > 0.0f
        && ImageScaleY > 0.0f
        && float.IsFinite(ImageOffsetX)
        && float.IsFinite(ImageOffsetY);

    public Vector4 ImageLayout => new(
        ImageScaleX,
        ImageScaleY,
        ImageOffsetX,
        ImageOffsetY);
}
