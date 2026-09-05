using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Application;

/// <summary>
/// Connection-authenticated world-item ingress. Validation happens before bounded queue admission so malformed
/// packet state never becomes an authoritative command. The exact connection/player generation is retained in the
/// command. Explicit packet-21/22 slot operations additionally snapshot the exact active world-item generation so
/// delayed work cannot jump to a later logical item after Terraria reuses the same numeric slot.
/// </summary>
internal sealed class RuntimeWorldItemIngress : IWorldItemIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> _ingress;
    private readonly RuntimeWorldItemStore _worldItems;

    public RuntimeWorldItemIngress(
        IGameCommandIngress<RuntimeCommand> ingress,
        RuntimeWorldItemStore worldItems)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        ArgumentNullException.ThrowIfNull(worldItems);
        _ingress = ingress;
        _worldItems = worldItems;
    }

    public bool TryPostAllocate(ConnectionHandle connection, in WorldItemDropStateUpdate state)
    {
        if (!connection.IsAssigned || !IsValidDrop(in state))
  return false;

        return _ingress.TryPost(connection.Source, new WorldItemAllocateRuntimeCommand(connection, state));
    }

    public bool TryPostDrop(ConnectionHandle connection, short slot, in WorldItemDropStateUpdate state)
    {
        if (!connection.IsAssigned || !IsValidSlot(slot) || !IsValidDrop(in state))
  return false;

        WorldItemHandle target = CaptureActiveTarget(slot);
        return _ingress.TryPost(connection.Source, new WorldItemDropRuntimeCommand(connection, target, state));
    }

    public bool TryPostRemove(ConnectionHandle connection, short slot)
    {
        if (!connection.IsAssigned || !IsValidSlot(slot))
  return false;

        WorldItemHandle target = CaptureActiveTarget(slot);
        return _ingress.TryPost(connection.Source, new WorldItemRemoveRuntimeCommand(connection, target));
    }

    public bool TryPostOwner(ConnectionHandle connection, short slot, in WorldItemOwnerStateUpdate state)
    {
        if (!connection.IsAssigned || !IsValidSlot(slot) || !IsValidOwner(in state))
  return false;

        WorldItemHandle target = CaptureActiveTarget(slot);
        return _ingress.TryPost(connection.Source, new WorldItemOwnerRuntimeCommand(connection, target, state));
    }

    private WorldItemHandle CaptureActiveTarget(short slot) =>
        _worldItems.TryGetActive(slot, out WorldItemSnapshot snapshot)
  ? snapshot.Handle
  : default;

    private static bool IsValidSlot(short slot) =>
        (ushort)slot < RuntimeWorldItemStore.VanillaCapacity;

    private static bool IsValidDrop(in WorldItemDropStateUpdate state) =>
        float.IsFinite(state.PositionX) &&
        float.IsFinite(state.PositionY) &&
        float.IsFinite(state.VelocityX) &&
        float.IsFinite(state.VelocityY) &&
        state.Stack > 0 &&
        state.TryGetItemType(out _) &&
        (byte)state.Ownership <= (byte)WorldItemOwnershipMode.GrabDelayForAllPlayers &&
        float.IsFinite(state.ShimmerTime) &&
        state.ShimmerTime >= 0f;

    private static bool IsValidOwner(in WorldItemOwnerStateUpdate state) =>
        state.TimeToKeepReservation >= 0 &&
        state.GrabDelayTime >= 0 &&
        float.IsFinite(state.PositionX) &&
        float.IsFinite(state.PositionY);
}
