using HdrImageViewer.Presentation;
using Xunit;

namespace HdrImageViewer.Tests;

public sealed class ImageLoadControllerTests
{
    [Fact]
    public void Stop_RejectsPublicationEvenIfProducerIgnoresCancellation()
    {
        using var controller = new ImageLoadController(CancellationToken.None);
        var operation = controller.Begin();
        controller.CancelCurrent();
        Assert.False(controller.IsCurrent(operation));
        controller.Complete(operation);
    }

    [Fact]
    public async Task OlderCompletionCannotReplaceANewerImage()
    {
        using var controller = new ImageLoadController(CancellationToken.None);
        var first = controller.Begin();
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var displayed = "";
        var oldLoad = PublishOldAsync();
        var second = controller.Begin();
        if (controller.IsCurrent(second)) displayed = "new image";
        releaseFirst.SetResult();
        await oldLoad;
        Assert.Equal("new image", displayed);
        controller.Complete(second);

        async Task PublishOldAsync()
        {
            await releaseFirst.Task;
            if (controller.IsCurrent(first)) displayed = "old image";
            controller.Complete(first);
        }
    }

    [Fact]
    public void Begin_CancelsPreviousOperationAndKeepsNewestCurrent()
    {
        using var lifetime = new CancellationTokenSource();
        using var controller = new ImageLoadController(lifetime.Token);
        var first = controller.Begin();
        var second = controller.Begin();

        Assert.True(first.Token.IsCancellationRequested);
        Assert.False(controller.IsCurrent(first));
        Assert.True(controller.IsCurrent(second));

        controller.Complete(first);
        Assert.True(controller.IsCurrent(second));
        controller.Complete(second);
    }

    [Fact]
    public void Dispose_CancelsCurrentOperation()
    {
        using var controller = new ImageLoadController(CancellationToken.None);
        var operation = controller.Begin();
        var token = operation.Token;

        controller.Dispose();

        Assert.True(token.IsCancellationRequested);
        operation.Dispose();
    }
}
