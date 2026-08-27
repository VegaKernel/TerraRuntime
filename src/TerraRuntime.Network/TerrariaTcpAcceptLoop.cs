using System.Net.Sockets;

namespace TerraRuntime.Network;

public sealed class TerrariaTcpAcceptLoop
{
    private readonly TerrariaConnectionAdmissionGate _admissionGate;
    private readonly Func<Socket, CancellationToken, ValueTask> _connectionHandler;
    private long _handlerFailures;

    public TerrariaTcpAcceptLoop(
        TerrariaConnectionAdmissionGate admissionGate,
        Func<Socket, CancellationToken, ValueTask> connectionHandler)
    {
        ArgumentNullException.ThrowIfNull(admissionGate);
        ArgumentNullException.ThrowIfNull(connectionHandler);
        _admissionGate = admissionGate;
        _connectionHandler = connectionHandler;
    }

    public long HandlerFailures => Interlocked.Read(ref _handlerFailures);

    public async ValueTask RunAsync(Socket listener, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(listener);

        var activeConnections = new HashSet<Task>();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                activeConnections.RemoveWhere(static task => task.IsCompleted);

                Socket socket;
                try
                {
                    socket = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (!_admissionGate.TryAcquire(out TerrariaConnectionAdmissionGate.Lease? lease))
                {
                    socket.Dispose();
                    continue;
                }

                activeConnections.Add(RunAcceptedConnectionAsync(socket, lease, cancellationToken));
            }
        }
        finally
        {
            if (activeConnections.Count != 0)
            {
                await Task.WhenAll(activeConnections).ConfigureAwait(false);
            }
        }
    }

    private async Task RunAcceptedConnectionAsync(
        Socket socket,
        TerrariaConnectionAdmissionGate.Lease lease,
        CancellationToken cancellationToken)
    {
        try
        {
            await _connectionHandler(socket, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            Interlocked.Increment(ref _handlerFailures);
        }
        finally
        {
            socket.Dispose();
            lease.Dispose();
        }
    }
}
