using HdrImageViewer.Services;
using Microsoft.UI.Xaml;
using Microsoft.Windows.Storage.Pickers;
using Windows.Graphics;

namespace HdrImageViewer.Pages;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "RunAsync disposes the task cancellation source in finally; closing the window cancels it.")]
public sealed partial class BatchExportWindow : Window
{
    private readonly BatchExportController _queue = new();
    private CancellationTokenSource? _cancellation;
    private string? _directory;
    private bool _closed;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    public BatchExportWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(BatchTitleBar);
        var scale = GetDpiForWindow(Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(880 * scale), (int)(720 * scale)));
        QueueList.ItemsSource = _queue.Items;
        Closed += (_, _) => { _closed = true; _cancellation?.Cancel(); };
    }

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker(AppWindow.Id);
            picker.FileTypeFilter.Add("*");
            var results = await picker.PickMultipleFilesAsync();
            _queue.Add(results.Select(file => file.Path));
            UpdateSummary();
        }
        catch (Exception ex) { SummaryText.Text = ex.Message; }
    }

    private async void Folder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await new FolderPicker(AppWindow.Id).PickSingleFolderAsync();
            if (result is not null) { _directory = result.Path; FolderText.Text = Path.GetFileName(result.Path.TrimEnd(Path.DirectorySeparatorChar));
                Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(FolderButton, result.Path);
                UpdateSummary(); }
        }
        catch (Exception ex) { SummaryText.Text = ex.Message; }
    }

    private void Format_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        if (FormatNoteText is null) return;
        FormatNoteText.Text = FormatSelector.SelectedIndex switch
        {
            0 or 1 => "适合分享。HDR 将转换为 SDR。",
            2 or 8 or 9 => "兼容 SDR 查看，同时保留 HDR 增益图。",
            3 => "16 位 PQ，BT.2020 色彩。",
            4 => "16 位 HLG，BT.2020 色彩。",
            5 or 6 => "保留线性浮点数据，适合后期处理。",
            _ => "复制完整原文件，不重新编码。"
        };
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in QueueList.SelectedItems.Cast<BatchExportItem>().ToArray()) _queue.Items.Remove(item);
        UpdateSummary();
    }
    private void Clear_Click(object sender, RoutedEventArgs e) { _queue.Items.Clear(); UpdateSummary(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { _cancellation?.Cancel(); CancelButton.IsEnabled = false; }
    private async void Start_Click(object sender, RoutedEventArgs e) => await RunAsync();
    private async void Retry_Click(object sender, RoutedEventArgs e) { _queue.RetryUnfinished(); await RunAsync(); }

    private async Task RunAsync()
    {
        if (_queue.IsRunning) return;
        if (_directory is null) { SummaryText.Text = "请先选择输出文件夹。"; return; }
        if (!_queue.Items.Any(item => item.State == BatchExportState.Pending)) { UpdateSummary(); return; }
        var format = FormatSelector.SelectedIndex;
        var directory = _directory;
        _cancellation = new CancellationTokenSource();
        SetRunning(true);
        foreach (var item in _queue.Items) item.PropertyChanged += Item_PropertyChanged;
        try
        {
            await _queue.RunAsync((item, token) => BatchImageExportService.ExportAsync(item, directory, format, token), _cancellation.Token);
        }
        finally
        {
            foreach (var item in _queue.Items) item.PropertyChanged -= Item_PropertyChanged;
            _cancellation.Dispose(); _cancellation = null;
            if (!_closed) { SetRunning(false); UpdateSummary(); }
        }
    }

    private void Item_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    { if (!_closed) UpdateSummary(); }

    private void SetRunning(bool running)
    {
        AddFilesButton.IsEnabled = RemoveButton.IsEnabled = ClearButton.IsEnabled = FolderButton.IsEnabled =
            FormatSelector.IsEnabled = StartButton.IsEnabled = RetryButton.IsEnabled = !running;
        CancelButton.IsEnabled = running;
        CancelButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        StartButton.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        QueueProgress.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateSummary()
    {
        var completed = _queue.Items.Count(item => item.State == BatchExportState.Completed);
        var failed = _queue.Items.Count(item => item.State == BatchExportState.Failed);
        var canceled = _queue.Items.Count(item => item.State == BatchExportState.Canceled);
        FileCountText.Text = $"{_queue.Items.Count} 张图片";
        QueueEmptyState.Visibility = _queue.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SummaryText.Text = _queue.Items.Count == 0 ? "准备就绪" : $"完成 {completed} / {_queue.Items.Count}" + (failed > 0 ? $" · {failed} 项失败" : "") + (canceled > 0 ? $" · {canceled} 项取消" : "");
        RetryButton.Visibility = !_queue.IsRunning && failed + canceled > 0 ? Visibility.Visible : Visibility.Collapsed;
        StartButton.IsEnabled = !_queue.IsRunning && _directory is not null && _queue.Items.Any(item => item.State == BatchExportState.Pending);
        QueueProgress.Maximum = Math.Max(1, _queue.Items.Count);
        QueueProgress.Value = completed + failed + canceled;
    }

}
