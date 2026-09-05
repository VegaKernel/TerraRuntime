using TerraRuntime.Gameplay.Items;
using System.Collections.Concurrent;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// Tracks live connection outbound queues independently from socket ownership and fans out
/// authoritative player events only to clients that have completed the spawn transition.
/// Recipient selection always passes through the runtime-owned interest router before enqueue.
/// </summary>
internal sealed partial class RuntimeConnectionRegistry : IRuntimePlayerEventSink, IRuntimeServerPlayerEventSink
{
    private const int ProtocolPlayerSlotCount = byte.MaxValue + 1;
    private readonly ConcurrentDictionary<GameCommandSourceId, RuntimeConnectionEndpoint> _endpoints = new();
    private readonly RuntimeConnectionEndpoint?[] _playingEndpoints = new RuntimeConnectionEndpoint?[ProtocolPlayerSlotCount];
    private readonly ServerPlayerReplicaStore _serverPlayers = new();
    private readonly RuntimeInterestRouter _interestRouter;
    private readonly RuntimePlayerMovementVisibilityReadiness _movementVisibilityReadiness = new();
    private long _relayedAppearanceFrames;
    private long _appearanceBaselineFrames;
    private long _relayedEquipmentFrames;
    private long _equipmentBaselineFrames;
    private long _droppedEquipmentSnapshotUpdates;
    private long _playerActiveBaselineFrames;
    private long _playerDeactivationFrames;
    private long _relayedMovementFrames;
    private long _movementResyncFrames;
    private long _serverPlayerHealthFrames;
    private long _serverPlayerManaFrames;

    public RuntimeConnectionRegistry(
        IInterestManagementControl? interestManagement = null,
        WorldDimensions? dimensions = null)
    {
        _interestRouter = new RuntimeInterestRouter(
            interestManagement ?? new InterestManagementControl(),
            dimensions);
    }

    public int Count => _endpoints.Count;

    public long RelayedAppearanceFrames => Interlocked.Read(ref _relayedAppearanceFrames);

    public long AppearanceBaselineFrames => Interlocked.Read(ref _appearanceBaselineFrames);

    public long RelayedEquipmentFrames => Interlocked.Read(ref _relayedEquipmentFrames);

    public long EquipmentBaselineFrames => Interlocked.Read(ref _equipmentBaselineFrames);

    public long DroppedEquipmentSnapshotUpdates => Interlocked.Read(ref _droppedEquipmentSnapshotUpdates);

    public long PlayerActiveBaselineFrames => Interlocked.Read(ref _playerActiveBaselineFrames);

    public long PlayerDeactivationFrames => Interlocked.Read(ref _playerDeactivationFrames);

    public long RelayedMovementFrames => Interlocked.Read(ref _relayedMovementFrames);

    public long MovementResyncFrames => Interlocked.Read(ref _movementResyncFrames);

    internal RuntimePlayerSpatialIndexSnapshot? PlayerSpatialSnapshot =>
        _interestRouter.PlayerSpatialSnapshot;

    internal RuntimePlayerVisibilitySnapshot? PlayerVisibilitySnapshot =>
        _interestRouter.PlayerVisibilitySnapshot;

    internal RuntimePlayerMovementVisibilityReadinessSnapshot PlayerMovementVisibilityReadinessSnapshot =>
        _movementVisibilityReadiness.Snapshot;

    internal int CollectNearbyPlayers(
        PlayerSlotId subject,
        int radiusSections,
        Span<PlayerSlotId> destination,
        bool includeSubject = false) =>
        _interestRouter.CollectNearbyPlayers(subject, radiusSections, destination, includeSubject);

    internal bool IsPlayerMovementVisibilityReady(PlayerSlotId observer, PlayerSlotId subject) =>
        _movementVisibilityReadiness.IsReady(observer, subject);


    public bool TryRegister(GameCommandSourceId source, TerrariaConnectionOutboundQueue outbound)
    {
        ArgumentNullException.ThrowIfNull(outbound);
        if (source.IsSystem)
            return false;

        return _endpoints.TryAdd(source, new RuntimeConnectionEndpoint(outbound));
    }

    public bool TryUnregister(GameCommandSourceId source, out PlayerHandle? playingPlayer)
    {
        playingPlayer = null;
        if (!_endpoints.TryRemove(source, out RuntimeConnectionEndpoint? endpoint))
            return false;

        if (endpoint.TryGetPlayingPlayer(out PlayerHandle player))
        {
            playingPlayer = player;
            Interlocked.CompareExchange(ref _playingEndpoints[player.Slot.Value], null, endpoint);
            _movementVisibilityReadiness.ClearPlayer(player.Slot);
        }

        return true;
    }


    private bool TryGetPlayingEndpoint(PlayerHandle player, out RuntimeConnectionEndpoint endpoint)
    {
        if (!TryGetPlayingEndpoint(player.Slot, out endpoint) ||
            !endpoint.TryGetPlayingPlayer(out PlayerHandle current) ||
            current != player)
        {
            endpoint = null!;
            return false;
        }

        return true;
    }

    private bool TryGetPlayingEndpoint(PlayerSlotId slot, out RuntimeConnectionEndpoint endpoint)
    {
        RuntimeConnectionEndpoint? candidate = Volatile.Read(ref _playingEndpoints[slot.Value]);
        if (candidate is null ||
            !candidate.TryGetPlayingSlot(out PlayerSlotId currentSlot) ||
            currentSlot != slot)
        {
            endpoint = null!;
            return false;
        }

        endpoint = candidate;
        return true;
    }

}
