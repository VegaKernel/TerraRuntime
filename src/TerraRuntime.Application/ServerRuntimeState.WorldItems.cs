using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime;

internal sealed partial class ServerRuntimeState
{
    private void TickInstancedItemLeases()
    {
        int expired = _instancedItemLeases.Tick(_expiredInstancedItemSlots);
        if (_worldItemReplication is null)
            return;
        for (int index = 0; index < expired; index++)
            _worldItemReplication.TryBroadcastInstancedSlotRelease(_expiredInstancedItemSlots[index]);
    }

    private bool IsCurrentWorldItemTarget(WorldItemHandle target) =>
        target.IsAssigned &&
        _worldItems.TryGetActive(target.Slot, out WorldItemSnapshot snapshot) &&
        snapshot.Handle == target;

    private void ApplyWorldItemAllocate(WorldItemAllocateRuntimeCommand command)
    {
        if (!_players.IsCurrent(command.Connection))
        {
            rejectedWorldItemAllocations++;
            command.Completion?.TrySetResult(null);
            return;
        }

        WorldItemDropStateUpdate state = command.State;
        if (_worldItems.TryAllocateDrop(in state, out WorldItemSnapshot snapshot))
        {
            appliedWorldItemAllocations++;
            command.Completion?.TrySetResult(snapshot);
            return;
        }

        rejectedWorldItemAllocations++;
        command.Completion?.TrySetResult(null);
    }

    private void ApplyWorldItemDrop(WorldItemDropRuntimeCommand command)
    {
        if (!_players.IsCurrent(command.Connection) ||
            !IsCurrentWorldItemTarget(command.Target))
        {
            RejectedWorldItemDrops++;
            return;
        }

        WorldItemDropStateUpdate state = command.State;
        if (_worldItems.TryApplyDrop(command.Target.Slot, in state, out _))
        {
            AppliedWorldItemDrops++;
            return;
        }

        RejectedWorldItemDrops++;
    }

    private void ApplyWorldItemRemove(WorldItemRemoveRuntimeCommand command)
    {
        if (!_players.IsCurrent(command.Connection) ||
            !IsCurrentWorldItemTarget(command.Target))
        {
            RejectedWorldItemRemovals++;
            return;
        }

        if (_worldItems.TryRemove(command.Target.Slot, out _))
        {
            AppliedWorldItemRemovals++;
            return;
        }

        RejectedWorldItemRemovals++;
    }

    private void ApplyWorldItemOwner(WorldItemOwnerRuntimeCommand command)
    {
        if (!_players.IsCurrent(command.Connection) ||
            !IsCurrentWorldItemTarget(command.Target))
        {
            RejectedWorldItemOwners++;
            return;
        }

        WorldItemOwnerStateUpdate state = command.State;
        if (_worldItems.TryApplyOwner(command.Target.Slot, in state, out _))
        {
            AppliedWorldItemOwners++;
            return;
        }

        RejectedWorldItemOwners++;
    }
}
