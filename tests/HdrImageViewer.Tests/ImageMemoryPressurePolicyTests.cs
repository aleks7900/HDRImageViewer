using HdrImageViewer.Services;
using Xunit;

namespace HdrImageViewer.Tests;

public sealed class ImageMemoryPressurePolicyTests
{
    [Fact]
    public void ShouldCollect_RequiresEvictionsAndHighMemoryLoad()
    {
        var highPressure = new ImageMemoryPressureSnapshot(
            MemoryLoadBytes: 900,
            HighMemoryLoadThresholdBytes: 1000,
            HeapSizeBytes: 500,
            FragmentedBytes: 0);

        Assert.False(ImageMemoryPressurePolicy.ShouldCollect(
            ImageMemoryPressurePolicy.MinimumEvictedBytes - 1,
            highPressure));
        Assert.True(ImageMemoryPressurePolicy.ShouldCollect(
            ImageMemoryPressurePolicy.MinimumEvictedBytes,
            highPressure));
        Assert.False(ImageMemoryPressurePolicy.ShouldCollect(
            ImageMemoryPressurePolicy.MinimumEvictedBytes,
            highPressure with { MemoryLoadBytes = 800 }));
    }

    [Theory]
    [InlineData(512L * 1024 * 1024, 128L * 1024 * 1024, true)]
    [InlineData(512L * 1024 * 1024, 60L * 1024 * 1024, false)]
    [InlineData(1024L * 1024 * 1024, 128L * 1024 * 1024, false)]
    public void ShouldCompactLargeObjectHeap_RequiresAbsoluteAndRelativeFragmentation(
        long heapSizeBytes,
        long fragmentedBytes,
        bool expected)
    {
        var snapshot = new ImageMemoryPressureSnapshot(
            MemoryLoadBytes: 0,
            HighMemoryLoadThresholdBytes: 0,
            heapSizeBytes,
            fragmentedBytes);

        Assert.Equal(expected, ImageMemoryPressurePolicy.ShouldCompactLargeObjectHeap(snapshot));
    }
}
