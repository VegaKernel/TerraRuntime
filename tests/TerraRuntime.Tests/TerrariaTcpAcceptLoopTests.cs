using System.Net;
using System.Net.Sockets;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class TerrariaTcpAcceptLoopTests
{
    [Fact]
    public async Task Admission_happens_before_the_connection_handler_and_the_lease_is_released()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(4);
        var endpoint = (IPEndPoint)listener.LocalEndPoint!;

        var gate = new TerrariaConnectionAdmissionGate(maxConnections: 1);
        var handlerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int handlerCalls = 0;

        var acceptLoop = new TerrariaTcpAcceptLoop(
            gate,
            async (_, cancellationToken) =>
            {
                Interlocked.Increment(ref handlerCalls);
                handlerStarted.TrySetResult(true);
                await releaseHandler.Task.WaitAsync(cancellationToken);
            });

        using var loopCancellation = CancellationTokenSource.CreateLinkedTokenSource(testCancellation);
        Task loopTask = acceptLoop.RunAsync(listener, loopCancellation.Token).AsTask();

        using var firstClient = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await firstClient.ConnectAsync(endpoint, testCancellation);
        await handlerStarted.Task.WaitAsync(testCancellation);
        Assert.Equal(1, gate.ActiveConnections);
        Assert.Equal(1, Volatile.Read(ref handlerCalls));

        using var secondClient = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await secondClient.ConnectAsync(endpoint, testCancellation);
        await WaitUntilAsync(() => gate.RejectedConnections == 1, testCancellation);
        Assert.Equal(1, Volatile.Read(ref handlerCalls));

        releaseHandler.TrySetResult(true);
        await WaitUntilAsync(() => gate.ActiveConnections == 0, testCancellation);

        loopCancellation.Cancel();
        await loopTask;

        Assert.Equal(1, gate.AcceptedConnections);
        Assert.Equal(1, gate.RejectedConnections);
        Assert.Equal(0, acceptLoop.HandlerFailures);
    }

    [Fact]
    public async Task Handler_failure_is_isolated_and_releases_the_admission_lease()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        var endpoint = (IPEndPoint)listener.LocalEndPoint!;

        var gate = new TerrariaConnectionAdmissionGate(maxConnections: 1);
        var handlerCalled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var acceptLoop = new TerrariaTcpAcceptLoop(
            gate,
            (_, _) =>
            {
                handlerCalled.TrySetResult(true);
                return ValueTask.FromException(new InvalidOperationException("handler failed"));
            });

        using var loopCancellation = CancellationTokenSource.CreateLinkedTokenSource(testCancellation);
        Task loopTask = acceptLoop.RunAsync(listener, loopCancellation.Token).AsTask();

        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await client.ConnectAsync(endpoint, testCancellation);
        await handlerCalled.Task.WaitAsync(testCancellation);
        await WaitUntilAsync(() => acceptLoop.HandlerFailures == 1 && gate.ActiveConnections == 0, testCancellation);

        loopCancellation.Cancel();
        await loopTask;

        Assert.Equal(1, gate.AcceptedConnections);
        Assert.Equal(1, acceptLoop.HandlerFailures);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }
}
