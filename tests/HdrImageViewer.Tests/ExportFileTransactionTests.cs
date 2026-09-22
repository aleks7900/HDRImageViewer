using HdrImageViewer.Services;
using Xunit;

namespace HdrImageViewer.Tests;

public sealed class ExportFileTransactionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "HdrExportTests", Guid.NewGuid().ToString("N"));

    public ExportFileTransactionTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CopyCommitsCompleteOutput(bool existing)
    {
        var source = Path.Combine(_directory, "source.png");
        var destination = Path.Combine(_directory, "destination.png");
        await File.WriteAllTextAsync(source, "new image");
        if (existing) await File.WriteAllTextAsync(destination, "old image");
        await ExportFileTransaction.CopyAsync(source, destination, CancellationToken.None);
        Assert.Equal("new image", await File.ReadAllTextAsync(destination));
        Assert.Empty(Directory.GetFiles(_directory, ".hdr-export-*"));
    }

    [Fact]
    public void CancellationPreservesExistingFileAndCleansStaging()
    {
        var destination = Path.Combine(_directory, "existing.png");
        File.WriteAllText(destination, "old image");
        string staging;
        using (var transaction = new ExportFileTransaction(destination))
        {
            staging = transaction.TemporaryPath;
            File.WriteAllText(staging, "new image");
            Assert.Throws<OperationCanceledException>(() => transaction.Commit(new CancellationToken(true)));
            Assert.Equal("old image", File.ReadAllText(destination));
        }
        Assert.False(File.Exists(staging));
    }

    [Fact]
    public void EmptyOutputDoesNotReplaceDestination()
    {
        var destination = Path.Combine(_directory, "existing.png");
        File.WriteAllText(destination, "old image");
        using var transaction = new ExportFileTransaction(destination);
        File.WriteAllBytes(transaction.TemporaryPath, []);
        Assert.Throws<InvalidDataException>(() => transaction.Commit(CancellationToken.None));
        Assert.Equal("old image", File.ReadAllText(destination));
    }

    [WindowsFact]
    public void LockedDestinationSurvivesFailedReplacement()
    {
        var destination = Path.Combine(_directory, "existing.png");
        File.WriteAllText(destination, "old image");
        using var transaction = new ExportFileTransaction(destination);
        File.WriteAllText(transaction.TemporaryPath, "new image");
        using (var held = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.ThrowsAny<IOException>(() => transaction.Commit(CancellationToken.None));
            Assert.Equal("old image", File.ReadAllText(destination));
        }
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
