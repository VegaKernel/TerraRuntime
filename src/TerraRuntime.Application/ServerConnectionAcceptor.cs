using System.Collections.Concurrent;
using System.Net.Sockets;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Network;
using TerraRuntime.Operations;
using TerraRuntime.Protocol;
using StructuredLogCategory = TerraRuntime.Contracts.Diagnostics.RuntimeLogCategory;
using StructuredLogContext = TerraRuntime.Contracts.Diagnostics.RuntimeLogContext;
using StructuredLogEventIds = TerraRuntime.Contracts.Diagnostics.RuntimeLogEventIds;

namespace TerraRuntime;

/// <summary>
/// Owns accepted Terraria connection lifetime independently from the public listening endpoint. Listener generations
/// may therefore be replaced without canceling already accepted client sockets.
/// </summary>
internal sealed class ServerConnectionAcceptor : IDisposable
{
    private readonly WorldRuntime primaryRuntime;
    private readonly RuntimeHostLog hostLog;
    private readonly TerrariaConnectionAdmissionGate admission;
    private readonly RuntimeConnectionQueueTelemetry queueTelemetry = new();
    private readonly RuntimeConnectionRateTelemetry rateTelemetry = new();
    private readonly RuntimeConnectionStopTelemetry stopTelemetry = new();
    private readonly ConcurrentDictionary<long, Task> connectionTasks = new();
    private readonly RuntimeConnectionDirectory connectionDirectory = new();
    private long nextConnectionId;
    private int disposed;

    public ServerConnectionAcceptor(
        int maxPlayers,
        WorldRuntime primaryRuntime,
        RuntimeHostLog hostLog)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPlayers, 1);
        this.primaryRuntime = primaryRuntime ?? throw new ArgumentNullException(nameof(primaryRuntime));
        this.hostLog = hostLog ?? throw new ArgumentNullException(nameof(hostLog));

        admission = new TerrariaConnectionAdmissionGate(maxPlayers);
        Operations = new LocalRuntimeNetworkOperations(
            admission,
            primaryRuntime.RuntimeConnections,
            queueTelemetry,
            rateTelemetry,
            primaryRuntime.NpcReplication,
            primaryRuntime.ProjectileReplication,
            primaryRuntime.WorldItemReplication,
            stopTelemetry);
    }

    public TerrariaConnectionAdmissionGate Admission => admission;
    public RuntimeConnectionDirectory Directory => connectionDirectory;
    public LocalRuntimeNetworkOperations Operations { get; }

    /// <summary>
    /// Transfers ownership of one already accepted socket into the shared connection lifecycle. The listener-specific
    /// cancellation token is deliberately absent: endpoint draining must stop only future accepts, never live clients.
    /// </summary>
    public void Accept(Socket socket, CancellationToken serverShutdownToken)
    {
        ArgumentNullException.ThrowIfNull(socket);
        if (Volatile.Read(ref disposed) != 0 || serverShutdownToken.IsCancellationRequested)
        {
            socket.Dispose();
            return;
        }

        if (!admission.TryAcquire(out TerrariaConnectionAdmissionGate.Lease? admissionLease) || admissionLease is null)
        {
            socket.Dispose();
            return;
        }

        long connectionId = Interlocked.Increment(ref nextConnectionId);
        Task connectionTask = RunConnectionAsync(
            connectionId,
            socket,
            admissionLease,
            serverShutdownToken);
        connectionTasks[connectionId] = connectionTask;
        _ = connectionTask.ContinueWith(
            completed => connectionTasks.TryRemove(connectionId, out _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public async Task DrainAsync()
    {
        Task[] activeConnections = connectionTasks.Values.ToArray();
        if (activeConnections.Length == 0)
            return;

        try
        {
            await Task.WhenAll(activeConnections).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            string message = $"Connection shutdown observed a fault: {exception.Message}";
            hostLog.Log(
                RuntimeLogLevel.Error,
                StructuredLogEventIds.NetworkShutdownFault,
                StructuredLogCategory.Network,
                "Network",
                message,
                useStandardError: true);
        }
    }

    private async Task RunConnectionAsync(
        long connectionId,
        Socket socket,
        TerrariaConnectionAdmissionGate.Lease admissionLease,
        CancellationToken cancellationToken)
    {
        string remote = socket.RemoteEndPoint?.ToString() ?? "unknown";
        GameCommandSourceId source = GameCommandSourceId.FromConnection(connectionId);
        var connectionContext = new StructuredLogContext(
            CorrelationId: $"connection-{connectionId}",
            ConnectionId: connectionId.ToString());
        hostLog.Log(
            RuntimeLogLevel.Information,
            StructuredLogEventIds.NetworkConnectionAccepted,
            StructuredLogCategory.Network,
            "Network",
            $"Connection {connectionId} accepted from {remote}.",
            connectionContext,
            bufferedOnly: !hostLog.IsPlainConsoleActive);

        using (admissionLease)
        {
            var outbound = new TerrariaConnectionOutboundQueue(
                ConnectionOutboundQueueSizing.Create(primaryRuntime.Slots.Capacity));
            TerrariaConnectionPolicyOptions policyOptions = TerrariaConnectionPolicyOptions.Default;
            var rateAccountant = new TerrariaConnectionRateAccountant(policyOptions.RateBudget);

            if (!RuntimeConnectionWorldBinding.TryCreateInitial(
                    primaryRuntime,
                    source,
                    outbound,
                    out RuntimeConnectionWorldBinding? primaryBinding) ||
                primaryBinding is null)
            {
                socket.Dispose();
                return;
            }

            using var route = new RuntimeConnectionRoute(source, outbound, primaryBinding);
            if (!connectionDirectory.TryRegister(source, route))
            {
                socket.Dispose();
                return;
            }

            if (!queueTelemetry.TryRegister(connectionId, outbound))
            {
                connectionDirectory.TryUnregister(source, out _);
                socket.Dispose();
                return;
            }
            if (!rateTelemetry.TryRegister(connectionId, rateAccountant))
            {
                queueTelemetry.TryUnregister(connectionId);
                connectionDirectory.TryUnregister(source, out _);
                socket.Dispose();
                return;
            }

            try
            {
                try
                {
                    TerrariaSocketRunResult result = await TerrariaSocketConnection.RunAsync(
                        socket,
                        route,
                        outbound,
                        TerrariaFrameDecoderOptions.Default,
                        policyOptions,
                        rateAccountant,
                        cancellationToken).ConfigureAwait(false);
                    stopTelemetry.Record(result.StopReason);
                    WorldRuntime activeRuntime = route.ActiveRuntime;
                    string message =
                        $"Connection {connectionId} ({remote}) stopped: {result.StopReason}; " +
                        $"runtime={activeRuntime.Identity}, bootstrap={route.ActiveBootstrapStopReason}, state={route.ActiveJoinState}; " +
                        $"inbound={result.Inbound}; rate={result.Rate}; outbound={result.Outbound.Reason}.";
                    hostLog.Log(
                        RuntimeLogLevel.Information,
                        StructuredLogEventIds.NetworkConnectionStopped,
                        StructuredLogCategory.Network,
                        "Network",
                        message,
                        connectionContext);
                }
                catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        hostLog.Log(
                            RuntimeLogLevel.Warning,
                            StructuredLogEventIds.NetworkConnectionFailed,
                            StructuredLogCategory.Network,
                            "Network",
                            $"Connection {connectionId} ({remote}) failed: {exception.Message}",
                            connectionContext,
                            useStandardError: true);
                    }
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
            finally
            {
                rateTelemetry.TryUnregister(connectionId);
                queueTelemetry.TryUnregister(connectionId);
                connectionDirectory.TryUnregister(source, out _);
                try
                {
                    route.DisconnectActive();
                }
                catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        hostLog.Log(
                            RuntimeLogLevel.Warning,
                            StructuredLogEventIds.NetworkDisconnectEnqueueFailed,
                            StructuredLogCategory.Network,
                            "Network",
                            $"Connection {connectionId} ({remote}) could not complete authoritative route detach: {exception.Message}",
                            connectionContext,
                            useStandardError: true);
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref disposed, 1);
    }
}
