using System.Net;
using System.Net.Sockets;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Operations;
using StructuredLogCategory = TerraRuntime.Contracts.Diagnostics.RuntimeLogCategory;
using StructuredLogEventIds = TerraRuntime.Contracts.Diagnostics.RuntimeLogEventIds;

namespace TerraRuntime;

/// <summary>
/// Owns replaceable public listening endpoints independently from accepted client sockets. A listener generation
/// moves only <see cref="ListenerLifecycleState.Active"/> -> <see cref="ListenerLifecycleState.Draining"/> ->
/// <see cref="ListenerLifecycleState.Closed"/>. Draining stops new accepts from that generation but deliberately
/// does not cancel connections already transferred to <see cref="ServerConnectionAcceptor"/>.
/// </summary>
internal sealed class ListenerManager : IDisposable
{
    private readonly object sync = new();
    private readonly Action<Socket, CancellationToken> acceptSocket;
    private readonly RuntimeHostLog hostLog;
    private readonly int backlog;
    private readonly List<ListenerRegistration> draining = [];
    private ListenerRegistration? active;
    private CancellationToken serverShutdownToken;
    private long nextGeneration;
    private long successfulRebinds;
    private ListenerLifecycleState lastState = ListenerLifecycleState.Closed;
    private string lastBindAddress = IPAddress.Any.ToString();
    private int lastPort;
    private long lastGeneration;
    private int started;
    private int disposed;

    public ListenerManager(
        ServerConnectionAcceptor connections,
        int maxPlayers,
        RuntimeHostLog hostLog)
        : this(
            (socket, cancellationToken) => connections.Accept(socket, cancellationToken),
            maxPlayers,
            hostLog)
    {
        ArgumentNullException.ThrowIfNull(connections);
    }

    internal ListenerManager(
        Action<Socket, CancellationToken> acceptSocket,
        int maxPlayers,
        RuntimeHostLog hostLog)
    {
        this.acceptSocket = acceptSocket ?? throw new ArgumentNullException(nameof(acceptSocket));
        this.hostLog = hostLog ?? throw new ArgumentNullException(nameof(hostLog));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPlayers, 1);
        backlog = Math.Max(32, maxPlayers * 2);
    }

    public void Start(string bindAddress, int port, CancellationToken shutdownToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.CompareExchange(ref started, 1, 0) != 0)
            throw new InvalidOperationException("ListenerManager has already been started.");

        if (!TryParseEndpoint(bindAddress, port, out IPAddress? address, out string normalized, out string? error) || address is null)
        {
            Interlocked.Exchange(ref started, 0);
            throw new ArgumentException(error ?? "Invalid listener endpoint.", nameof(bindAddress));
        }

        ListenerRegistration registration;
        try
        {
            registration = CreateBoundRegistration(address, normalized, port, shutdownToken);
        }
        catch
        {
            Interlocked.Exchange(ref started, 0);
            throw;
        }

        lock (sync)
        {
            serverShutdownToken = shutdownToken;
            active = registration;
            Remember(registration, ListenerLifecycleState.Active);
        }
        registration.StartAcceptLoop();
    }

    /// <summary>
    /// Replaces the active public endpoint. The normal path binds the replacement before draining the old listener,
    /// so a failed bind leaves the active endpoint untouched. When the old and new endpoints overlap on the same port,
    /// the old listening socket may have to drain first; if the replacement then fails, the manager attempts to restore
    /// the previous endpoint. Accepted clients are never canceled by either path.
    /// </summary>
    public ListenerChangeResult TryChangeEndpoint(string bindAddress, int port)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Volatile.Read(ref started) == 0)
            return ListenerChangeResult.Rejected("Listener manager is not running.");

        if (!TryParseEndpoint(bindAddress, port, out IPAddress? address, out string normalized, out string? error) || address is null)
            return ListenerChangeResult.Rejected(error ?? "Invalid listener endpoint.");

        lock (sync)
        {
            CleanupClosedListenersLocked();
            ListenerRegistration? previous = active;
            if (previous is null)
                return ListenerChangeResult.Rejected("No active listener is available to replace.");

            if (previous.Port == port && previous.Address.Equals(address))
            {
                return ListenerChangeResult.Accepted(
                    $"Listener is already active on {FormatEndpoint(normalized, port)}.");
            }

            ListenerRegistration? replacement = null;
            Exception? firstFailure = null;
            try
            {
                replacement = CreateBoundRegistration(address, normalized, port, serverShutdownToken);
            }
            catch (Exception exception) when (exception is SocketException or InvalidOperationException)
            {
                firstFailure = exception;
            }

            if (replacement is null)
            {
                // Binding a different local address on the same port can overlap an ANY listener on several OSes.
                // Draining the old listening socket does not affect accepted connection sockets, so retry once after
                // closing only the accept endpoint and restore the previous endpoint if the requested bind still fails.
                if (previous.Port != port || firstFailure is not SocketException socketFailure ||
                    socketFailure.SocketErrorCode != SocketError.AddressAlreadyInUse)
                {
                    return RejectBind(normalized, port, firstFailure);
                }

                active = null;
                BeginDrainLocked(previous);
                try
                {
                    replacement = CreateBoundRegistration(address, normalized, port, serverShutdownToken);
                }
                catch (Exception retryFailure) when (retryFailure is SocketException or InvalidOperationException)
                {
                    ListenerRegistration? restored = TryRestorePreviousEndpointLocked(previous);
                    string restoration = restored is null
                        ? " Previous listener could not be restored; existing clients remain connected but no public listener is active."
                        : $" Previous endpoint {FormatEndpoint(restored.BindAddress, restored.Port)} was restored.";
                    return ListenerChangeResult.Rejected(
                        $"Could not bind {FormatEndpoint(normalized, port)}: {retryFailure.Message}.{restoration}");
                }
            }

            ListenerRegistration old = previous;
            active = replacement;
            Remember(replacement, ListenerLifecycleState.Active);
            if (old.State == ListenerLifecycleState.Active)
                BeginDrainLocked(old);
            Interlocked.Increment(ref successfulRebinds);
            replacement.StartAcceptLoop();

            hostLog.Log(
                RuntimeLogLevel.Information,
                StructuredLogEventIds.NetworkListenerRebound,
                StructuredLogCategory.Network,
                "Network",
                $"Listener generation {replacement.Generation} active on {FormatEndpoint(replacement.BindAddress, replacement.Port)}; " +
                $"previous generation {old.Generation} is {old.State}. Existing connections were preserved.");

            return ListenerChangeResult.Accepted(
                $"Listening on {FormatEndpoint(replacement.BindAddress, replacement.Port)}; existing clients preserved.");
        }
    }

    public ListenerManagerSnapshot CaptureSnapshot()
    {
        lock (sync)
        {
            CleanupClosedListenersLocked();
            ListenerRegistration? current = active;
            return new ListenerManagerSnapshot(
                BindAddress: current?.BindAddress ?? lastBindAddress,
                Port: current?.Port ?? lastPort,
                State: current?.State ?? lastState,
                Generation: current?.Generation ?? lastGeneration,
                DrainingListeners: draining.Count(static listener => listener.State == ListenerLifecycleState.Draining),
                SuccessfulRebinds: Interlocked.Read(ref successfulRebinds),
                CapturedAtUtc: DateTimeOffset.UtcNow);
        }
    }

    public async Task CloseAsync()
    {
        ListenerRegistration[] pending;
        lock (sync)
        {
            if (active is not null)
            {
                ListenerRegistration current = active;
                active = null;
                BeginDrainLocked(current);
                Remember(current, ListenerLifecycleState.Draining);
            }

            pending = draining.ToArray();
        }

        if (pending.Length != 0)
            await Task.WhenAll(pending.Select(static listener => listener.WaitClosedAsync())).ConfigureAwait(false);

        lock (sync)
        {
            CleanupClosedListenersLocked();
            lastState = ListenerLifecycleState.Closed;
        }
    }

    private ListenerRegistration CreateBoundRegistration(
        IPAddress address,
        string normalizedBindAddress,
        int port,
        CancellationToken shutdownToken)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.Equals(IPAddress.IPv6Any))
                socket.DualMode = true;
            socket.Bind(new IPEndPoint(address, port));
            socket.Listen(backlog);
            long generation = Interlocked.Increment(ref nextGeneration);
            return new ListenerRegistration(
                generation,
                normalizedBindAddress,
                address,
                port,
                socket,
                acceptSocket,
                hostLog,
                shutdownToken,
                OnListenerClosed);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private ListenerRegistration? TryRestorePreviousEndpointLocked(ListenerRegistration previous)
    {
        try
        {
            ListenerRegistration restored = CreateBoundRegistration(
                previous.Address,
                previous.BindAddress,
                previous.Port,
                serverShutdownToken);
            active = restored;
            Remember(restored, ListenerLifecycleState.Active);
            restored.StartAcceptLoop();
            return restored;
        }
        catch (Exception exception) when (exception is SocketException or InvalidOperationException)
        {
            hostLog.Log(
                RuntimeLogLevel.Error,
                StructuredLogEventIds.NetworkListenerStartFailed,
                StructuredLogCategory.Network,
                "Network",
                $"Listener rollback to {FormatEndpoint(previous.BindAddress, previous.Port)} failed: {exception.Message}",
                useStandardError: true);
            return null;
        }
    }

    private ListenerChangeResult RejectBind(string bindAddress, int port, Exception? failure)
    {
        string detail = failure?.Message ?? "unknown listener error";
        hostLog.Log(
            RuntimeLogLevel.Warning,
            StructuredLogEventIds.NetworkListenerStartFailed,
            StructuredLogCategory.Network,
            "Network",
            $"Listener change to {FormatEndpoint(bindAddress, port)} rejected: {detail}",
            useStandardError: true);
        return ListenerChangeResult.Rejected($"Could not bind {FormatEndpoint(bindAddress, port)}: {detail}");
    }

    private void BeginDrainLocked(ListenerRegistration listener)
    {
        if (listener.BeginDrain())
        {
            draining.Add(listener);
            Remember(listener, ListenerLifecycleState.Draining);
        }
    }

    private void OnListenerClosed(ListenerRegistration listener)
    {
        hostLog.Log(
            RuntimeLogLevel.Debug,
            StructuredLogEventIds.NetworkListenerDrained,
            StructuredLogCategory.Network,
            "Network",
            $"Listener generation {listener.Generation} closed on {FormatEndpoint(listener.BindAddress, listener.Port)}.");
    }

    private void CleanupClosedListenersLocked()
    {
        draining.RemoveAll(static listener => listener.State == ListenerLifecycleState.Closed);
        if (active is null && draining.Count == 0 && Volatile.Read(ref started) != 0)
            lastState = ListenerLifecycleState.Closed;
    }

    private void Remember(ListenerRegistration listener, ListenerLifecycleState state)
    {
        lastState = state;
        lastBindAddress = listener.BindAddress;
        lastPort = listener.Port;
        lastGeneration = listener.Generation;
    }

    private static bool TryParseEndpoint(
        string bindAddress,
        int port,
        out IPAddress? address,
        out string normalized,
        out string? error)
    {
        address = null;
        normalized = string.Empty;
        if (port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            error = $"Port must be between {IPEndPoint.MinPort} and {IPEndPoint.MaxPort}.";
            return false;
        }

        string candidate = bindAddress?.Trim() ?? string.Empty;
        if (candidate is "*" or "any" or "ANY")
            candidate = IPAddress.Any.ToString();
        else if (candidate.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            candidate = IPAddress.Loopback.ToString();

        if (!IPAddress.TryParse(candidate, out address))
        {
            error = "Bind address must be a numeric IPv4/IPv6 address, '*', 'any', or 'localhost'.";
            return false;
        }

        normalized = address.ToString();
        error = null;
        return true;
    }

    private static string FormatEndpoint(string bindAddress, int port) =>
        bindAddress.Contains(':', StringComparison.Ordinal) ? $"[{bindAddress}]:{port}" : $"{bindAddress}:{port}";

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        lock (sync)
        {
            if (active is not null)
            {
                BeginDrainLocked(active);
                active = null;
            }

            foreach (ListenerRegistration listener in draining)
                listener.Dispose();
            lastState = ListenerLifecycleState.Closed;
        }
    }

    private sealed class ListenerRegistration : IDisposable
    {
        private readonly Socket socket;
        private readonly Action<Socket, CancellationToken> acceptSocket;
        private readonly RuntimeHostLog hostLog;
        private readonly CancellationToken serverShutdownToken;
        private readonly CancellationTokenSource acceptCancellation = new();
        private readonly Action<ListenerRegistration> onClosed;
        private Task? acceptLoop;
        private int state = (int)ListenerLifecycleState.Active;
        private int disposed;

        public ListenerRegistration(
            long generation,
            string bindAddress,
            IPAddress address,
            int port,
            Socket socket,
            Action<Socket, CancellationToken> acceptSocket,
            RuntimeHostLog hostLog,
            CancellationToken serverShutdownToken,
            Action<ListenerRegistration> onClosed)
        {
            Generation = generation;
            BindAddress = bindAddress;
            Address = address;
            Port = port;
            this.socket = socket;
            this.acceptSocket = acceptSocket;
            this.hostLog = hostLog;
            this.serverShutdownToken = serverShutdownToken;
            this.onClosed = onClosed;
        }

        public long Generation { get; }
        public string BindAddress { get; }
        public IPAddress Address { get; }
        public int Port { get; }
        public ListenerLifecycleState State => (ListenerLifecycleState)Volatile.Read(ref state);

        public void StartAcceptLoop()
        {
            if (acceptLoop is not null)
                throw new InvalidOperationException("Listener accept loop has already been started.");
            acceptLoop = AcceptLoopAsync();
        }

        public bool BeginDrain()
        {
            if (Interlocked.CompareExchange(
                    ref state,
                    (int)ListenerLifecycleState.Draining,
                    (int)ListenerLifecycleState.Active) != (int)ListenerLifecycleState.Active)
            {
                return false;
            }

            acceptCancellation.Cancel();
            socket.Dispose();
            if (acceptLoop is null)
                Close();
            return true;
        }

        public Task WaitClosedAsync() => acceptLoop ?? Task.CompletedTask;

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!acceptCancellation.IsCancellationRequested && !serverShutdownToken.IsCancellationRequested)
                {
                    Socket accepted;
                    try
                    {
                        accepted = await socket.AcceptAsync(acceptCancellation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (acceptCancellation.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (ObjectDisposedException) when (State != ListenerLifecycleState.Active)
                    {
                        break;
                    }
                    catch (SocketException) when (State != ListenerLifecycleState.Active || acceptCancellation.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (SocketException exception)
                    {
                        hostLog.Log(
                            RuntimeLogLevel.Warning,
                            StructuredLogEventIds.NetworkAcceptFailed,
                            StructuredLogCategory.Network,
                            "Network",
                            $"Accept failed on listener generation {Generation}: {exception.SocketErrorCode}.",
                            useStandardError: true);
                        continue;
                    }

                    acceptSocket(accepted, serverShutdownToken);
                }
            }
            finally
            {
                Close();
            }
        }

        private void Close()
        {
            // The accept loop can stop because the process-shutdown token was canceled without BeginDrain being
            // called first. Always release the public listening socket at the Closed transition; accepted sockets
            // have already been transferred to ServerConnectionAcceptor and are unaffected.
            socket.Dispose();
            ListenerLifecycleState previous = (ListenerLifecycleState)Interlocked.Exchange(
                ref state,
                (int)ListenerLifecycleState.Closed);
            if (previous != ListenerLifecycleState.Closed)
                onClosed(this);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;

            // Do not dispose the CTS while AcceptAsync may still be unwinding from cancellation. The registration is
            // short-lived and the token source becomes collectible with it after the accept loop completes.
            BeginDrain();
            socket.Dispose();
        }
    }
}
