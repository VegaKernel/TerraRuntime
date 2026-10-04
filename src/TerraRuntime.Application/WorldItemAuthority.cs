using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

/// <summary>
/// Owns authoritative world-item command application and instanced-item lease expiry for one live world.
/// The authoritative world loop remains the only caller; this type does not introduce a second writer.
/// </summary>
internal sealed partial class WorldItemAuthority
{
    private readonly PlayerAuthority players;
    private readonly RuntimeWorldItemStore worldItems;
    private readonly RuntimeWorldItemReplicationRegistry? replication;
    private readonly RuntimeWorldItemInstancedLeaseStore instancedItemLeases;
    private readonly RuntimeWorldItemOwnerFactsProvider1458 ownerFacts;
    private readonly Func<VanillaSeasonalItemDropContext1458>? seasonalContext;
    private readonly short[] expiredInstancedItemSlots = new short[RuntimeWorldItemStore.VanillaCapacity];
    private readonly WorldItemSnapshot[] reservationScan = new WorldItemSnapshot[RuntimeWorldItemStore.VanillaCapacity];

    // TerrariaServer 1.4.5.8 Main.UpdateServer calls FindOwner for unowned items on a 5-tick cadence.
    private const int OwnerDiscoveryCadenceTicks1458 = 5;
    private const int OwnedOwnerDiscoveryCadenceTicks1458 = 300;
    private const int DefaultOwnerReservationTicks1458 = 15; // WorldItem.ReserveFor default parameter

    public WorldItemAuthority(
        PlayerAuthority players,
        RuntimeWorldItemStore worldItems,
        IWorldItemSpawnRandom spawnRandom,
        RuntimeWorldItemReplicationRegistry? replication,
        TerraRuntime.World.WorldTileStore? worldTiles = null,
        bool expertMode = false,
        bool masterMode = false,
        Func<VanillaSeasonalItemDropContext1458>? seasonalContext = null)
    {
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.worldItems = worldItems ?? throw new ArgumentNullException(nameof(worldItems));
        SpawnRandom = spawnRandom ?? throw new ArgumentNullException(nameof(spawnRandom));
        this.replication = replication;
        instancedItemLeases = new RuntimeWorldItemInstancedLeaseStore(worldItems);
        // Pickup equipment uses Main.expertMode/masterMode from source effective difficulty,
        // including GoodWorld. Composition supplies those flags separately from base combat mode.
        ownerFacts = new(players, worldTiles, expertMode, masterMode);
        this.seasonalContext = seasonalContext;
    }

    public IWorldItemSpawnRandom SpawnRandom { get; }
    internal RuntimeWorldItemOwnerFactsProvider1458 SourceOwnerFacts => ownerFacts;

    public long AppliedAllocations { get; private set; }
    public long RejectedAllocations { get; private set; }
    public long AppliedDrops { get; private set; }
    public long RejectedDrops { get; private set; }
    public long AppliedRemovals { get; private set; }
    public long RejectedRemovals { get; private set; }
    public long AppliedOwners { get; private set; }
    public long RejectedOwners { get; private set; }

    public RuntimeWorldItemInstancedLeaseStore InstancedLeases => instancedItemLeases;

    public bool TryApply(RuntimeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        switch (command)
        {
            case WorldItemAllocateRuntimeCommand allocate:
                ApplyAllocate(allocate);
                return true;
            case WorldItemDropRuntimeCommand drop:
                ApplyDrop(drop);
                return true;
            case WorldItemRemoveRuntimeCommand remove:
                ApplyRemove(remove);
                return true;
            case WorldItemOwnerRuntimeCommand owner:
                ApplyOwner(owner);
                return true;
            case WorldItemReleaseRuntimeCommand release:
                ApplyRelease(release);
                return true;
            default:
                return false;
        }
    }

    public void TickPlayerReservations(long tick)
    {
        if (tick < 0) return;

        int count = worldItems.CopyActive(reservationScan);
        for (int index = 0; index < count; index++)
        {
            WorldItemSnapshot item = reservationScan[index];
            if (!item.Handle.IsAssigned || !worldItems.TryGetSourceOwnerMetadata(item.Handle, out int age, out _) ||
                item.TimeToKeepReservation > 0) continue;

            RuntimePlayerMember? currentOwner = null;
            if (item.OwnerPlayerId != byte.MaxValue)
            {
                foreach (RuntimePlayerMember player in players.Members)
                {
                    if (player.Slot.Value == item.OwnerPlayerId)
                    {
                        currentOwner = player;
                        break;
                    }
                }
            }

            if (item.OwnerPlayerId == byte.MaxValue)
            { if (age % OwnerDiscoveryCadenceTicks1458 != 1) continue; }
            else if (currentOwner is not null && age % OwnedOwnerDiscoveryCadenceTicks1458 != 0) continue;

            byte owner = byte.MaxValue;
            if (item.ShimmerTime <= 0f && !worldItems.HasPendingSourceTransfer(item.Handle) &&
                !(item.GrabDelayTime > 0 && item.GrabDelayPlayer == byte.MaxValue) &&
                !TryFindSourceOwner(in item, out owner)) continue;
            if (owner == item.OwnerPlayerId)
                continue;
            if (currentOwner is not null)
            { _ = worldItems.TryRequestSourceOwnerRelease(item.Handle); continue; }

            var update = new WorldItemOwnerStateUpdate(
                OwnerPlayerId: owner,
                TimeToKeepReservation: owner == byte.MaxValue ? 0 : DefaultOwnerReservationTicks1458,
                GrabDelayPlayer: item.GrabDelayPlayer,
                GrabDelayTime: item.GrabDelayTime,
                PositionX: item.PositionX,
                PositionY: item.PositionY);
            _ = worldItems.TryApplyOwner(item.Handle.Slot, in update, out _);
        }
    }

    private bool TryFindSourceOwner(in WorldItemSnapshot item, out byte selected)
    {
        var context = ownerFacts.Capture();
        var drop = new WorldItemDropStateUpdate(item.PositionX, item.PositionY, item.VelocityX, item.VelocityY,
            item.Stack, item.Prefix, item.Ownership, item.ItemNetId, item.Shimmered, item.ShimmerTime, item.EnemyGrabDelayTime);
        return context.TrySelectOwner(in drop, item.GrabDelayTime, item.GrabDelayPlayer, out selected) && context.IsCurrent;
    }

    public void TickInstancedLeases()
    {
        int expired = instancedItemLeases.Tick(expiredInstancedItemSlots);
        if (replication is null)
            return;

        for (int index = 0; index < expired; index++)
            replication.TryBroadcastInstancedSlotRelease(expiredInstancedItemSlots[index]);
    }

    internal void TickReservationTimers()
    {
        worldItems.TickReservationTimers();
        _ = worldItems.TryProcessPendingSourceTransfers();
    }

    public bool TryCapture(short slot, out WorldItemSnapshot snapshot) =>
        worldItems.TryGetActive(slot, out snapshot);

    internal int CopyActive(Span<WorldItemSnapshot> destination) => worldItems.CopyActive(destination);

    /// <summary>
    /// Exact-generation removal for a trusted server-owned actor. The caller has already decided that the item is
    /// eligible for pickup; this boundary owns the authoritative world-item mutation and rejects stale handles.
    /// </summary>
    internal bool TryTakeTrusted(WorldItemHandle target, out WorldItemSnapshot removed)
    {
        removed = default;
        if (!target.IsAssigned ||
            !worldItems.TryGetActive(target.Slot, out WorldItemSnapshot current) ||
            current.Handle != target ||
            !worldItems.TryRemove(target.Slot, out WorldItemHandle removedHandle) ||
            removedHandle != target)
        {
            return false;
        }

        removed = current;
        return true;
    }

    private bool IsCurrentTarget(WorldItemHandle target) =>
        target.IsAssigned &&
        worldItems.TryGetActive(target.Slot, out WorldItemSnapshot snapshot) &&
        snapshot.Handle == target;

    private bool IsCurrentReservedTarget(ConnectionHandle connection, WorldItemHandle target) =>
        target.IsAssigned &&
        worldItems.TryGetActive(target.Slot, out WorldItemSnapshot snapshot) &&
        snapshot.Handle == target &&
        snapshot.OwnerPlayerId == connection.Player.Slot.Value;

    private void ApplyAllocate(WorldItemAllocateRuntimeCommand command)
    {
        if (!players.IsCurrent(command.Connection))
        {
            RejectedAllocations++;
            command.Completion?.TrySetResult(null);
            return;
        }

        WorldItemDropStateUpdate state = command.State;
        if (!TryPrepareClientCreation(in state, out var created, out var liveRandom, out var beforeRandom, out var afterRandom, out var calendar))
        { RejectedAllocations++; command.Completion?.TrySetResult(null); return; }
        state = created;
        Span<WorldItemAllocationPlayer1458> views = stackalloc WorldItemAllocationPlayer1458[byte.MaxValue];
        int viewCount = 0;
        foreach (var player in players.Members)
        {
            var body = player.HasMount ? TerraRuntime.Gameplay.Players.VanillaPlayerMountHitbox1458.Resolve(player.MountType) : (Width: 20f, Height: 42f);
            views[viewCount++] = new(player.Slot.Value, player.PositionX, player.PositionY, (int)body.Width, (int)body.Height);
        }
        using var allocation = worldItems.CreateAllocationPreview(views[..viewCount]);
        if (allocation.TrySpawnSource(in state, 0, out _, sourceLocalPlayerId: command.Connection.Player.Slot.Value,
                creationSource: WorldItemCreationSource1458.ClientSynchronization) &&
            liveRandom!.HasSameState(beforeRandom!) && (seasonalContext is null || seasonalContext() == calendar) && allocation.TryClaim())
        {
            liveRandom.CopyStateFrom(afterRandom!); // Adopt before any source21/22 publication callback.
            if (!allocation.TryCommitNext(out short selectedSlot, out _)) throw new InvalidOperationException("Accepted client item creation lost its claimed source plan.");
            WorldItemSnapshot snapshot = default;
            if (selectedSlot < worldItems.Capacity) _ = worldItems.TryGetActive(selectedSlot, out snapshot);
            AppliedAllocations++;
            command.Completion?.TrySetResult(snapshot.Handle.IsAssigned ? snapshot : null);
            return;
        }

        RejectedAllocations++;
        command.Completion?.TrySetResult(null);
    }

    private void ApplyDrop(WorldItemDropRuntimeCommand command)
    {
        // TerrariaServer 1.4.5.8 MessageBuffer case 21 accepts updates to an existing world-item slot only
        // when that slot is currently reserved for whoAmI. New-item allocation (wire slot 400) is the separate
        // allocate command above. Keep the same owner gate for movement/stack updates so a client cannot mutate
        // another player's reserved item after server-side FindOwner selected the pickup recipient.
        if (!players.IsCurrent(command.Connection) || !IsCurrentReservedTarget(command.Connection, command.Target))
        {
            RejectedDrops++;
            return;
        }

        WorldItemDropStateUpdate state = command.State;
        if (worldItems.TryApplyDrop(command.Target.Slot, in state, out _))
        {
            AppliedDrops++;
            return;
        }

        RejectedDrops++;
    }

    private void ApplyRemove(WorldItemRemoveRuntimeCommand command)
    {
        // A complete pickup uses packet 151 (NetMessage rewrites empty packet 21). Both decoded forms share this
        // boundary; vanilla accepts the mutation only
        // from the player for whom the item is reserved; applying the same rule here closes both pickup theft and
        // arbitrary remote item deletion while preserving the normal server-reservation -> client-pickup flow.
        if (!players.IsCurrent(command.Connection) || !IsCurrentReservedTarget(command.Connection, command.Target))
        {
            RejectedRemovals++;
            return;
        }

        if (worldItems.TryRemove(command.Target.Slot, out _))
        {
            AppliedRemovals++;
            return;
        }

        RejectedRemovals++;
    }

    private void ApplyOwner(WorldItemOwnerRuntimeCommand command)
    {
        if (!players.IsCurrent(command.Connection) || !IsCurrentTarget(command.Target))
        {
            RejectedOwners++;
            return;
        }

        WorldItemOwnerStateUpdate state = command.State;
        if (worldItems.TryApplyOwner(command.Target.Slot, in state, out _))
        {
            AppliedOwners++;
            return;
        }

        RejectedOwners++;
    }

    private void ApplyRelease(WorldItemReleaseRuntimeCommand command)
    {
        if (!players.IsCurrent(command.Connection) || !IsCurrentReservedTarget(command.Connection, command.Target) ||
            !worldItems.TryGetActive(command.Target.Slot, out WorldItemSnapshot item))
        {
            RejectedOwners++;
            return;
        }
        // Source case39 relinquishes ownership; a client cannot choose the next recipient or position.
        bool pendingTransfer = worldItems.HasPendingSourceTransfer(item.Handle);
        byte owner = byte.MaxValue;
        if (!command.ForceServer && !pendingTransfer && item.TimeToKeepReservation <= 0 &&
            !TryFindSourceOwner(in item, out owner)) { RejectedOwners++; return; }
        var update = new WorldItemOwnerStateUpdate(owner,
            (command.ForceServer || pendingTransfer) ? 0 : owner == byte.MaxValue ? item.TimeToKeepReservation : DefaultOwnerReservationTicks1458,
            item.GrabDelayPlayer, item.GrabDelayTime, item.PositionX, item.PositionY);
        if (worldItems.TryApplyOwner(item.Handle.Slot, in update, out _)) AppliedOwners++;
        else RejectedOwners++;
    }
}
