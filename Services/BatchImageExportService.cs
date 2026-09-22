namespace HdrImageViewer.Services;

public static class BatchImageExportService
{
    public static async Task<string> ExportAsync(BatchExportItem item, string directory, int format, CancellationToken token)
    {
        var extension = format switch { 1 or 2 => ".jpg", 5 => ".tif", 6 => ".exr", 7 => Path.GetExtension(item.SourcePath), _ => ".png" };
        var output = BatchExportController.ChooseOutputPath(directory, item.SourcePath, extension);
        if (format == 7)
        {
            await ExportFileTransaction.CopyAsync(item.SourcePath, output, token, overwrite: false);
            return output;
        }
        var tempDirectory = Path.Combine(Path.GetTempPath(), "HdrImageViewer", "batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var candidate = Path.Combine(tempDirectory, "image" + extension);
        try
        {
            var load = await ImageDocumentLoader.LoadAsync(item.SourcePath, token);
            if (format <= 1) await SdrImageExportService.ExportAsync(load.Document, candidate, format == 1, token);
            else if (format == 2) await GainMapHdrExportService.ExportJpegUltraHdrAsync(load.Document, candidate, token);
            else await SingleLayerHdrExportService.ExportAsync(load.Document, candidate,
                format == 4 ? SingleLayerHdrExportTransfer.Hlg : SingleLayerHdrExportTransfer.Pq, token);
            await ExportFileTransaction.CopyAsync(candidate, output, token, overwrite: false);
            return output;
        }
        finally
        {
            try { Directory.Delete(tempDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
