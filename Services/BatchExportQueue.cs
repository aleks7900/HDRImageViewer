using System.Collections.ObjectModel;
using HdrImageViewer.Infrastructure;

namespace HdrImageViewer.Services;

public enum BatchExportState { Pending, Running, Completed, Failed, Canceled }

public sealed class BatchExportItem(string sourcePath) : ObservableObject
{
    private BatchExportState _state;
    private string _detail = "等待导出";
    public string SourcePath { get; } = Path.GetFullPath(sourcePath);
    public string FileName => Path.GetFileName(SourcePath);
    public BatchExportState State
    {
        get => _state;
        internal set { if (SetProperty(ref _state, value)) OnPropertyChanged(nameof(StateLabel)); }
    }
    public string StateLabel => State switch
    {
        BatchExportState.Running => "导出中",
        BatchExportState.Completed => "已完成",
        BatchExportState.Failed => "失败",
        BatchExportState.Canceled => "已取消",
        _ => "等待"
    };
    public string Detail { get => _detail; internal set => SetProperty(ref _detail, value); }
}

public sealed class BatchExportController
{
    public ObservableCollection<BatchExportItem> Items { get; } = [];
    public bool IsRunning { get; private set; }

    public void Add(IEnumerable<string> paths)
    {
        if (IsRunning) throw new InvalidOperationException("队列运行中，暂不能添加文件。");
        var existing = Items.Select(item => item.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.Select(Path.GetFullPath))
            if (existing.Add(path)) Items.Add(new BatchExportItem(path));
    }

    public void RetryUnfinished()
    {
        if (IsRunning) throw new InvalidOperationException("队列仍在运行。");
        foreach (var item in Items.Where(item => item.State is BatchExportState.Failed or BatchExportState.Canceled))
        { item.State = BatchExportState.Pending; item.Detail = "等待重试"; }
    }

    public async Task RunAsync(Func<BatchExportItem, CancellationToken, Task<string>> export, CancellationToken cancellationToken)
    {
        if (IsRunning) throw new InvalidOperationException("队列已经在运行。");
        IsRunning = true;
        try
        {
            foreach (var item in Items.Where(item => item.State == BatchExportState.Pending).ToArray())
            {
                if (cancellationToken.IsCancellationRequested)
                { item.State = BatchExportState.Canceled; item.Detail = "已取消，未写入"; continue; }
                item.State = BatchExportState.Running; item.Detail = "正在解码与导出…";
                try
                {
                    var output = await export(item, cancellationToken);
                    item.State = BatchExportState.Completed; item.Detail = $"完成 · {output}";
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                { item.State = BatchExportState.Canceled; item.Detail = "已取消"; }
                catch (Exception ex)
                { item.State = BatchExportState.Failed; item.Detail = $"失败 · {ex.Message}"; }
            }
        }
        finally { IsRunning = false; }
    }

    public static string ChooseOutputPath(string directory, string source, string extension)
    {
        var name = Path.GetFileNameWithoutExtension(source) + "-export";
        for (var index = 1; ; index++)
        {
            var suffix = index == 1 ? string.Empty : $"-{index}";
            var path = Path.Combine(directory, name + suffix + extension);
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
        }
    }
}
