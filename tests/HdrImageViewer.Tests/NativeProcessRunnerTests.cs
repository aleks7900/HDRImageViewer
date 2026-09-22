using HdrImageViewer.Services;
using Xunit;

namespace HdrImageViewer.Tests;

public sealed class NativeProcessRunnerTests
{
    [WindowsFact]
    public async Task TimeoutTerminatesProcess()
    {
        using var process = NativeProcessRunner.Create("powershell.exe");
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add("Start-Sleep -Seconds 30");
        await Assert.ThrowsAsync<TimeoutException>(() => NativeProcessRunner.RunAsync(
            process, "test", CancellationToken.None, TimeSpan.FromMilliseconds(300)));
        Assert.True(process.HasExited);
    }

    [WindowsFact]
    public async Task CancellationTerminatesProcessWithoutReportingTimeout()
    {
        using var process = NativeProcessRunner.Create("powershell.exe");
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add("Start-Sleep -Seconds 30");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NativeProcessRunner.RunAsync(
            process, "test", cancellation.Token));
        Assert.True(process.HasExited);
    }
}
