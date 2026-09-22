using HdrImageViewer.Services;
using Windows.Graphics.Imaging;

// Explicit integration runner: accepts a synthetic HDR PNG and an isolated output directory.
if (args.Length != 2) throw new ArgumentException("Usage: <HDR PNG fixture> <test output directory>");
var source = Path.GetFullPath(args[0]);
var directory = Path.GetFullPath(args[1]);
Directory.CreateDirectory(directory);
var broken = Path.Combine(directory, "broken.png");
await File.WriteAllTextAsync(broken, "invalid integration fixture");
var queue = new BatchExportController();
queue.Add([broken, source]);
await queue.RunAsync((item, token) => BatchImageExportService.ExportAsync(item, directory, 0, token), CancellationToken.None);
if (queue.Items[0].State != BatchExportState.Failed || queue.Items[1].State != BatchExportState.Completed)
    throw new InvalidOperationException(string.Join("\n", queue.Items.Select(item => item.Detail)));
var png = Directory.GetFiles(directory, "*-export.png").Single();
await using (var stream = File.OpenRead(png))
{
    using var random = stream.AsRandomAccessStream();
    var decoder = await BitmapDecoder.CreateAsync(random);
    if (decoder.PixelWidth != 640 || decoder.PixelHeight != 320) throw new InvalidDataException("SDR export dimensions changed.");
    var pixels = (await decoder.GetPixelDataAsync()).DetachPixelData();
    if (pixels.Length == 0 || pixels.Distinct().Count() < 4) throw new InvalidDataException("Export has no useful pixel data.");
}
var hdr = await BatchImageExportService.ExportAsync(new BatchExportItem(source), directory, 3, CancellationToken.None);
await using var hdrStream = File.OpenRead(hdr);
var hdrProbe = await PngColorMetadataReader.ReadAsync(hdrStream);
if (hdrProbe?.CicpTransfer != 16 || hdrProbe.CicpPrimaries != 9 || hdrProbe.BitsPerChannel != 16)
    throw new InvalidDataException("HDR PQ metadata was not preserved.");
Console.WriteLine($"SDR queue verified: failed file did not stop the valid file; HDR exported to {hdr}");
Console.WriteLine($"PNG metadata: {hdrProbe}");
