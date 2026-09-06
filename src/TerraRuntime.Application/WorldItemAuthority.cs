using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

/// <summary>
/// Owns authoritative world-item command application and instanced-item lease expiry for one live world.
/// The authoritative world loop remains the only caller; this type does not introduce a second writer.
/// </summary>
internal sealed class WorldItemAuthority
{
    private readonly PlayerAuthority players;
    private readonly RuntimeWorldItemStore worldItems;
    private readonly RuntimeWorldItemReplicationRegistry? replication;
    private readonly RuntimeWorldItemInstancedLeaseStore instancedItemLeases;
    private readonly short[] expiredInstancedItemSlots = new short[RuntimeWorldItemStore.VanillaCapacity];
    private readonly WorldItemSnapshot[] reservationScan = new WorldItemSnapshot[RuntimeWorldItemStore.VanillaCapacity];

    // TerrariaServer 1.4.5.8 Main.UpdateServer calls FindOwner for unowned items on a 5-tick cadence.
    private const int OwnerDiscoveryCadenceTicks1458 = 5;
    private const int OwnerSearchManhattanRange1458 = 1920; // NPC.sWidth
    private const int DefaultOwnerReservationTicks1458 = 15; // WorldItem.ReserveFor default parameter

    public WorldItemAuthority(
        PlayerAuthority players,
        RuntimeWorldItemStore worldItems,
        IWorldItemSpawnRandom spawnRandom,
        RuntimeWorldItemReplicationRegistry? replication)
    {
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.worldItems = worldItems ?? throw new ArgumentNullException(nameof(worldItems));
        SpawnRandom = spawnRandom ?? throw new ArgumentNullException(nameof(spawnRandom));
        this.replication = replication;
        instancedItemLeases = new RuntimeWorldItemInstancedLeaseStore(worldItems);
    }

    public IWorldItemSpawnRandom SpawnRandom { get; }

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
            default:
                return false;
        }
    }

    public void TickPlayerReservations(long tick)
    {
        if (tick < 0 || tick % OwnerDiscoveryCadenceTicks1458 != 1)
            return;

        int count = worldItems.CopyActive(reservationScan);
        for (int index = 0; index < count; index++)
        {
            WorldItemSnapshot item = reservationScan[index];
            if (!item.Handle.IsAssigned || item.ShimmerTime > 0f)
                continue;

            RuntimePlayerMember? currentOwner = null;
            if (item.OwnerPlayerId != byte.MaxValue)
            {
                foreach (RuntimePlayerMember player in players.Members)
                {
                    if (player.Slot.Value == item.OwnerPlayerId && !player.IsDead)
                    {
                        currentOwner = player;
                        break;
                    }
                }
            }

            if (currentOwner is not null)
                continue;

            RuntimePlayerMember? selected = FindNearestEligibleOwner(in item);
            byte owner = selected?.Slot.Value ?? byte.MaxValue;
            if (owner == item.OwnerPlayerId)
                continue;

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

    private RuntimePlayerMember? FindNearestEligibleOwner(in WorldItemSnapshot item)
    {
        RuntimePlayerMember? selected = null;
        float bestDistance = OwnerSearchManhattanRange1458;
        foreach (RuntimePlayerMember player in players.Members)
        {
            if (player.IsDead || !HasConservativeOrdinaryItemSpace(player.Connection))
                continue;
            if (item.GrabDelayTime > 0 && (item.GrabDelayPlayer == player.Slot.Value || item.GrabDelayPlayer == byte.MaxValue))
                continue;

            float playerCenterX = player.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
            float playerCenterY = player.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
            float distance = Math.Abs(playerCenterX - item.PositionX) + Math.Abs(playerCenterY - item.PositionY);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            selected = player;
        }
        return selected;
    }

    private bool HasConservativeOrdinaryItemSpace(ConnectionHandle connection)
    {
        // Player.ItemSpace checks stacking, ammo slots, void bag and special pickups. We do not have complete
        // max-stack/special-pickup facts for every item yet, so reserve only when an ordinary 0..49 slot is empty.
        // This is a safe subset: the vanilla client performs the real GetItem/PickupItem mutation after packet 22.
        for (short slot = 0; slot < 50; slot++)
        {
            if (players.TryGetInventoryItem(connection, slot, out RuntimePlayerInventoryItem item) && item.IsEmpty)
                return true;
        }
        return false;
    }

    public void TickInstancedLeases()
    {
        int expired = instancedItemLeases.Tick(expiredInstancedItemSlots);
        if (replication is null)
            return;

        for (int index = 0; index < expired; index++)
            replication.TryBroadcastInstancedSlotRelease(expiredInstancedItemSlots[index]);
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
        if (worldItems.TryAllocateDrop(in state, out WorldItemSnapshot snapshot))
        {
            AppliedAllocations++;
            command.Completion?.TrySetResult(snapshot);
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
        // A pickup completion is just packet 21 with an air/zero-stack state. Vanilla accepts that mutation only
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
}
