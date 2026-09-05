using System.Collections.Concurrent;
using System.Globalization;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

/// <summary>
/// Process-owned routing state for one accepted TCP connection. Exactly one WorldRuntime binding receives inbound
/// gameplay frames at a time; the primary binding is retained so the connection's client-visible player slot remains
/// reserved while the player visits Level 1 sandboxes.
/// </summary>
internal sealed class RuntimeConnectionRoute : ITerrariaFrameSink, IDisposable
{
    private static readonly TimeSpan DefaultTransferTimeout = TimeSpan.FromSeconds(3);

    private readonly object gate = new();
    private readonly GameCommandSourceId source;
    private readonly TerrariaConnectionOutboundQueue outbound;
    private readonly RuntimeConnectionWorldBinding primary;
    private RuntimeConnectionWorldBinding active;
    private int disposed;

    public RuntimeConnectionRoute(
        GameCommandSourceId source,
        TerrariaConnectionOutboundQueue outbound,
        RuntimeConnectionWorldBinding primary)
    {
        if (source.IsSystem)
            throw new ArgumentException("A connection route requires a connection source.", nameof(source));
        this.outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
        this.primary = primary ?? throw new ArgumentNullException(nameof(primary));
        this.source = source;
        active = primary;
    }

    public WorldRuntime ActiveRuntime
    {
        get { lock (gate) return active.Runtime; }
    }

    public PlayerHandle? ActivePlayer
    {
        get { lock (gate) return active.Player; }
    }

    public string? ActivePlayerName
    {
        get { lock (gate) return active.PlayerName; }
    }

    public PlayerBootstrapStopReason ActiveBootstrapStopReason
    {
        get { lock (gate) return active.Bootstrap.StopReason; }
    }

    public TerraRuntime.Core.Players.PlayerJoinState? ActiveJoinState
    {
        get { lock (gate) return active.Bootstrap.JoinState; }
    }

    internal RuntimeConnectionRouteSnapshot CaptureSnapshot()
    {
        lock (gate)
        {
            return new RuntimeConnectionRouteSnapshot(
                source,
                active.Runtime.Identity,
                active.Player,
                active.PlayerName,
                active.Bootstrap.JoinState);
        }
    }

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0)
                return TerrariaFrameSinkResult.Stop;
            return active.Root.OnFrame(in frame);
        }
    }

    public bool TryTransfer(
        WorldRuntime destination,
        bool forceRespawn,
        out string? error,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            if (destination.Lifecycle != WorldRuntimeLifecycle.Running)
            {
                error = $"destination runtime {destination.Identity} is not running";
                return false;
            }

            RuntimeConnectionWorldBinding sourceBinding = active;
            if (sourceBinding.Player is not PlayerHandle sourcePlayer ||
                sourceBinding.Bootstrap.JoinState != TerraRuntime.Core.Players.PlayerJoinState.Playing)
            {
                error = "player connection has not completed the authoritative spawn transition";
                return false;
            }

            if (ReferenceEquals(sourceBinding.Runtime, destination) && !forceRespawn)
            {
                error = null;
                return true;
            }

            using var cancellation = new CancellationTokenSource(timeout ?? DefaultTransferTimeout);
            ConnectionHandle sourceConnection = new(source, sourcePlayer);
            if (ReferenceEquals(sourceBinding.Runtime, destination))
                return TryRespawnInPlace(sourceBinding, sourceConnection, cancellation.Token, out error);

            RuntimeConnectionWorldBinding? destinationBinding = null;
            bool destinationIsPrimary = ReferenceEquals(primary.Runtime, destination);
            if (destinationIsPrimary)
            {
                destinationBinding = primary;
            }
            else if (!RuntimeConnectionWorldBinding.TryCreateTransferred(
                         destination,
                         source,
                         outbound,
                         sourcePlayer.Slot,
                         sourceBinding.PlayerName,
                         out destinationBinding) ||
                     destinationBinding is null)
            {
                error = $"destination runtime cannot reserve player slot {sourcePlayer.Slot.Value}";
                return false;
            }

            RuntimePlayerTransferTransaction? transfer;
            try
            {
                transfer = RuntimePlayerTransferTransaction.Detach(
                    sourceBinding.Runtime,
                    sourceConnection,
                    cancellation.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException)
            {
                ReleaseUnusedDestination(destinationBinding, destinationIsPrimary);
                error = $"source transfer barrier failed: {exception.Message}";
                return false;
            }

            if (transfer is null)
            {
                ReleaseUnusedDestination(destinationBinding, destinationIsPrimary);
                error = "source runtime no longer owns that player generation";
                return false;
            }

            sourceBinding.Unregister();
            if (!destinationBinding.TryRegister())
            {
                RollBackWithoutClientWorldChange(sourceBinding, transfer, cancellation.Token);
                ReleaseUnusedDestination(destinationBinding, destinationIsPrimary);
                error = "destination runtime could not register the connection after source detach";
                return false;
            }

            destinationBinding.SetPlayerName(transfer.PlayerName);
            OutboundEnqueueResult bootstrapResult = destinationBinding.TryQueueWorldBootstrap();
            if (bootstrapResult != OutboundEnqueueResult.Enqueued)
            {
                RollBackWithoutClientWorldChange(sourceBinding, transfer, cancellation.Token);
                ReleaseUnusedDestination(destinationBinding, destinationIsPrimary);
                error = $"destination bootstrap rejected by outbound queue: {bootstrapResult}";
                return false;
            }

            ConnectionHandle destinationConnection = new(source, destinationBinding.Player!.Value);
            bool attached;
            error = null;
            try
            {
                // World-space coordinates are runtime-local. Crossing a world boundary always lands at
                // the destination world spawn; only rollback to the original runtime restores old coordinates.
                attached = transfer.TryAttach(
                    destination,
                    destinationConnection,
                    preserveWorldPosition: false,
                    forceRespawn,
                    cancellation.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException)
            {
                attached = false;
                error = $"destination transfer barrier failed: {exception.Message}";
            }

            if (!attached)
            {
                RollBackAfterClientBootstrap(sourceBinding, transfer, cancellation.Token);
                ReleaseUnusedDestination(destinationBinding, destinationIsPrimary);
                error ??= "destination runtime rejected the transferred player state";
                return false;
            }

            destinationBinding.MarkPlaying();
            active = destinationBinding;
            if (!ReferenceEquals(sourceBinding, primary))
                sourceBinding.Dispose();

            error = null;
            return true;
        }
    }

    public bool TryRequestDisconnect(out string? error)
    {
        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                error = "player connection is already closed";
                return false;
            }

            if (!outbound.Complete())
            {
                error = "player connection is already stopping";
                return false;
            }

            error = null;
            return true;
        }
    }

    public void DisconnectActive(TimeSpan? timeout = null)
    {
        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0)
                return;

            if (active.Player is PlayerHandle player && active.Bootstrap.JoinState == TerraRuntime.Core.Players.PlayerJoinState.Playing)
            {
                using var cancellation = new CancellationTokenSource(timeout ?? DefaultTransferTimeout);
                try
                {
                    RuntimePlayerTransferTransaction? detached = RuntimePlayerTransferTransaction.Detach(
                        active.Runtime,
                        new ConnectionHandle(source, player),
                        cancellation.Token);
                    detached?.Discard();
                }
                catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException)
                {
                    // Socket teardown still releases routing/replication ownership. The runtime loop may already be
                    // stopping; its normal shutdown owns any remaining world state.
                }
            }
            active.Unregister();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;

            RuntimeConnectionWorldBinding current = active;
            current.Unregister();
            if (!ReferenceEquals(current, primary))
                current.Dispose();
            primary.Dispose();
        }
    }

    private bool TryRespawnInPlace(
        RuntimeConnectionWorldBinding binding,
        ConnectionHandle connection,
        CancellationToken cancellationToken,
        out string? error)
    {
        RuntimePlayerTransferTransaction? transfer;
        try
        {
            transfer = RuntimePlayerTransferTransaction.Detach(binding.Runtime, connection, cancellationToken);
        }
        catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException)
        {
            error = $"respawn detach barrier failed: {exception.Message}";
            return false;
        }
        if (transfer is null)
        {
            error = "runtime no longer owns that player generation";
            return false;
        }

        // The runtime does not change, so keep the existing socket/replication binding registered. The
        // authoritative player membership still crosses a detach/attach barrier, but there is no reason to create a
        // second failure point by tearing down and rebuilding the same endpoint registrations.
        OutboundEnqueueResult bootstrap = binding.TryQueueWorldBootstrap();
        if (bootstrap != OutboundEnqueueResult.Enqueued)
        {
            transfer.RestoreSource(cancellationToken);
            binding.MarkPlaying();
            error = $"respawn bootstrap rejected by outbound queue: {bootstrap}";
            return false;
        }

        bool attached;
        error = null;
        try
        {
            attached = transfer.TryAttach(
                binding.Runtime,
                connection,
                preserveWorldPosition: false,
                forceRespawn: true,
                cancellationToken);
        }
        catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException)
        {
            attached = false;
            error = $"respawn attach barrier failed: {exception.Message}";
        }

        if (!attached)
        {
            // Client already has a bootstrap queued. Re-queue the same world and restore the exact pre-respawn state.
            _ = binding.TryQueueWorldBootstrap();
            transfer.RestoreSource(CancellationToken.None);
            binding.MarkPlaying();
            error ??= "runtime rejected the respawn state";
            return false;
        }

        binding.MarkPlaying();
        error = null;
        return true;
    }

    private static void RollBackWithoutClientWorldChange(
        RuntimeConnectionWorldBinding sourceBinding,
        RuntimePlayerTransferTransaction transfer,
        CancellationToken cancellationToken)
    {
        if (!sourceBinding.TryRegister())
            throw new InvalidOperationException("Source runtime could not restore connection registrations after failed transfer.");
        transfer.RestoreSource(cancellationToken);
        sourceBinding.MarkPlaying();
    }

    private static void RollBackAfterClientBootstrap(
        RuntimeConnectionWorldBinding sourceBinding,
        RuntimePlayerTransferTransaction transfer,
        CancellationToken cancellationToken)
    {
        if (sourceBinding.TryQueueWorldBootstrap() != OutboundEnqueueResult.Enqueued)
            throw new InvalidOperationException("Source runtime could not queue rollback bootstrap after failed transfer.");
        RollBackWithoutClientWorldChange(sourceBinding, transfer, cancellationToken);
    }

    private static void ReleaseUnusedDestination(
        RuntimeConnectionWorldBinding destination,
        bool destinationIsPrimary)
    {
        if (destinationIsPrimary)
            destination.Unregister();
        else
            destination.Dispose();
    }
}

/// <summary>Concurrent lookup for live socket routes used by operator transfer commands.</summary>
internal sealed class RuntimeConnectionDirectory
{
    private readonly ConcurrentDictionary<GameCommandSourceId, RuntimeConnectionRoute> routes = new();

    public bool TryRegister(GameCommandSourceId source, RuntimeConnectionRoute route) =>
        !source.IsSystem && routes.TryAdd(source, route);

    public bool TryUnregister(GameCommandSourceId source, out RuntimeConnectionRoute? route) =>
        routes.TryRemove(source, out route);

    public RuntimeConnectionRouteSnapshot[] Capture() =>
        routes.Values
            .Select(static route => route.CaptureSnapshot())
            .OrderBy(static route => route.Player?.Slot.Value ?? byte.MaxValue)
            .ThenBy(static route => route.Source.Value)
            .ToArray();

    public bool TryResolve(PlayerHandle player, out RuntimeConnectionRoute? route)
    {
        if (!player.IsAssigned)
        {
            route = null;
            return false;
        }

        foreach (RuntimeConnectionRoute candidate in routes.Values)
        {
            if (candidate.ActivePlayer == player)
            {
                route = candidate;
                return true;
            }
        }

        route = null;
        return false;
    }

    public bool TryResolve(string selector, out RuntimeConnectionRoute? route, out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        string normalized = selector[0] == '#' ? selector[1..] : selector;
        if (byte.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out byte slot))
        {
            RuntimeConnectionRoute? match = null;
            foreach (RuntimeConnectionRoute candidate in routes.Values)
            {
                if (candidate.ActivePlayer is not PlayerHandle player || player.Slot.Value != slot)
                    continue;
                if (match is not null)
                {
                    route = null;
                    error = $"player slot {slot} is ambiguous across active connections";
                    return false;
                }
                match = candidate;
            }
            route = match;
            error = match is null ? $"player slot {slot} is not connected" : null;
            return match is not null;
        }

        RuntimeConnectionRoute? named = null;
        foreach (RuntimeConnectionRoute candidate in routes.Values)
        {
            if (!string.Equals(candidate.ActivePlayerName, selector, StringComparison.OrdinalIgnoreCase))
                continue;
            if (named is not null)
            {
                route = null;
                error = $"player name '{selector}' is ambiguous";
                return false;
            }
            named = candidate;
        }
        route = named;
        error = named is null ? $"player '{selector}' is not connected" : null;
        return named is not null;
    }
}

internal readonly record struct RuntimeConnectionRouteSnapshot(
    GameCommandSourceId Source,
    WorldRuntimeIdentity Runtime,
    PlayerHandle? Player,
    string? PlayerName,
    TerraRuntime.Core.Players.PlayerJoinState? JoinState)
{
    public long ConnectionId => Source.Value;
}
