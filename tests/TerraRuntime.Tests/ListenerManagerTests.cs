using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using TerraRuntime.Application.Diagnostics;
using TerraRuntime.Application.Operations;

namespace TerraRuntime.Tests;

public sealed class ListenerManagerTests
{
    [Fact]
    public async Task Rebind_keeps_already_accepted_client_socket_alive()
    {
        int firstPort = GetFreeTcpPort();
        int secondPort = GetFreeTcpPort(except: firstPort);
        var accepted = new ConcurrentQueue<Socket>();
        var logs = new RuntimeLogBuffer();
        await using var hostLog = CreateSilentHostLog(logs);
        using var shutdown = new CancellationTokenSource();
        using var manager = new ListenerManager(
            (socket, _) => accepted.Enqueue(socket),
            maxPlayers: 8,
            hostLog);

        manager.Start(IPAddress.Loopback.ToString(), firstPort, shutdown.Token);
        using var firstClient = new TcpClient(AddressFamily.InterNetwork);
        await firstClient.ConnectAsync(IPAddress.Loopback, firstPort);
        Assert.True(SpinWait.SpinUntil(() => accepted.Count == 1, TimeSpan.FromSeconds(2)));
        Assert.True(accepted.TryPeek(out Socket? firstServerSocket));
        Assert.NotNull(firstServerSocket);

        ListenerChangeResult changed = manager.TryChangeEndpoint(IPAddress.Loopback.ToString(), secondPort);

        Assert.True(changed.Success, changed.Message);
        ListenerManagerSnapshot rebound = manager.CaptureSnapshot();
        Assert.Equal(ListenerLifecycleState.Active, rebound.State);
        Assert.Equal(secondPort, rebound.Port);
        Assert.Equal(2, rebound.Generation);
        Assert.Equal(1, rebound.SuccessfulRebinds);

        byte[] payload = [0x5A];
        await firstClient.GetStream().WriteAsync(payload);
        var received = new byte[1];
        int read = await firstServerSocket!.ReceiveAsync(received, SocketFlags.None);
        Assert.Equal(1, read);
        Assert.Equal(payload[0], received[0]);

        using var secondClient = new TcpClient(AddressFamily.InterNetwork);
        await secondClient.ConnectAsync(IPAddress.Loopback, secondPort);
        Assert.True(SpinWait.SpinUntil(() => accepted.Count == 2, TimeSpan.FromSeconds(2)));

        await manager.CloseAsync();
        Assert.Equal(ListenerLifecycleState.Closed, manager.CaptureSnapshot().State);

        while (accepted.TryDequeue(out Socket? socket))
            socket.Dispose();
    }

    [Fact]
    public async Task Same_port_bind_address_change_preserves_existing_client()
    {
        int port = GetFreeTcpPort();
        var accepted = new ConcurrentQueue<Socket>();
        var logs = new RuntimeLogBuffer();
        await using var hostLog = CreateSilentHostLog(logs);
        using var shutdown = new CancellationTokenSource();
        using var manager = new ListenerManager(
            (socket, _) => accepted.Enqueue(socket),
            maxPlayers: 8,
            hostLog);

        manager.Start(IPAddress.Loopback.ToString(), port, shutdown.Token);
        using var firstClient = new TcpClient(AddressFamily.InterNetwork);
        await firstClient.ConnectAsync(IPAddress.Loopback, port);
        Assert.True(SpinWait.SpinUntil(() => accepted.Count == 1, TimeSpan.FromSeconds(2)));
        Assert.True(accepted.TryPeek(out Socket? firstServerSocket));
        Assert.NotNull(firstServerSocket);

        ListenerChangeResult changed = manager.TryChangeEndpoint(IPAddress.Any.ToString(), port);

        Assert.True(changed.Success, changed.Message);
        ListenerManagerSnapshot rebound = manager.CaptureSnapshot();
        Assert.Equal(ListenerLifecycleState.Active, rebound.State);
        Assert.Equal(IPAddress.Any.ToString(), rebound.BindAddress);
        Assert.Equal(port, rebound.Port);
        Assert.Equal(1, rebound.SuccessfulRebinds);

        await firstClient.GetStream().WriteAsync(new byte[] { 0x66 });
        var received = new byte[1];
        int read = await firstServerSocket!.ReceiveAsync(received, SocketFlags.None);
        Assert.Equal(1, read);
        Assert.Equal(0x66, received[0]);

        using var secondClient = new TcpClient(AddressFamily.InterNetwork);
        await secondClient.ConnectAsync(IPAddress.Loopback, port);
        Assert.True(SpinWait.SpinUntil(() => accepted.Count == 2, TimeSpan.FromSeconds(2)));

        await manager.CloseAsync();
        while (accepted.TryDequeue(out Socket? socket))
            socket.Dispose();
    }

    [Fact]
    public async Task Failed_rebind_leaves_previous_listener_active()
    {
        int activePort = GetFreeTcpPort();
        int blockedPort = GetFreeTcpPort(except: activePort);
        using var blocker = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        blocker.Bind(new IPEndPoint(IPAddress.Loopback, blockedPort));
        blocker.Listen(1);

        var accepted = new ConcurrentQueue<Socket>();
        var logs = new RuntimeLogBuffer();
        await using var hostLog = CreateSilentHostLog(logs);
        using var shutdown = new CancellationTokenSource();
        using var manager = new ListenerManager(
            (socket, _) => accepted.Enqueue(socket),
            maxPlayers: 8,
            hostLog);
        manager.Start(IPAddress.Loopback.ToString(), activePort, shutdown.Token);

        ListenerChangeResult changed = manager.TryChangeEndpoint(IPAddress.Loopback.ToString(), blockedPort);

        Assert.False(changed.Success);
        ListenerManagerSnapshot snapshot = manager.CaptureSnapshot();
        Assert.Equal(ListenerLifecycleState.Active, snapshot.State);
        Assert.Equal(activePort, snapshot.Port);
        Assert.Equal(1, snapshot.Generation);
        Assert.Equal(0, snapshot.SuccessfulRebinds);

        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, activePort);
        Assert.True(SpinWait.SpinUntil(() => accepted.Count == 1, TimeSpan.FromSeconds(2)));

        await manager.CloseAsync();
        while (accepted.TryDequeue(out Socket? socket))
            socket.Dispose();
    }

    private static RuntimeHostLog CreateSilentHostLog(RuntimeLogBuffer logs) =>
        new(
            logs,
            TextWriter.Null,
            TextWriter.Null,
            new RuntimeHostLoggingOptions
            {
                ConsoleEnabled = false,
                JsonLinesEnabled = false
            });

    private static int GetFreeTcpPort(int except = -1)
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            if (port != except)
                return port;
        }

        throw new InvalidOperationException("Could not reserve two distinct loopback TCP ports for the test.");
    }
}
