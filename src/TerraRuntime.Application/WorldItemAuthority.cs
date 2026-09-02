using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime;

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

    private bool IsCurrentTarget(WorldItemHandle target) =>
        target.IsAssigned &&
        worldItems.TryGetActive(target.Slot, out WorldItemSnapshot snapshot) &&
        snapshot.Handle == target;

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
        if (!players.IsCurrent(command.Connection) || !IsCurrentTarget(command.Target))
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
        if (!players.IsCurrent(command.Connection) || !IsCurrentTarget(command.Target))
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
