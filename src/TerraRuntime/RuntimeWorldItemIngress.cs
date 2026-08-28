using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

/// <summary>
/// Connection-authenticated world-item ingress. Validation happens before bounded queue admission so malformed
/// packet state never becomes an authoritative command.
/// </summary>
internal sealed class RuntimeWorldItemIngress : IWorldItemIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> _ingress;

    public RuntimeWorldItemIngress(IGameCommandIngress<RuntimeCommand> ingress)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        _ingress = ingress;
    }

    public bool TryPostAllocate(ConnectionHandle connection, in WorldItemDropStateUpdate state)
    {
        if (!connection.IsAssigned || !IsValidDrop(in state))
            return false;

        return _ingress.TryPost(connection.Source, new WorldItemAllocateRuntimeCommand(state));
    }

    public bool TryPostDrop(ConnectionHandle connection, short slot, in WorldItemDropStateUpdate state)
    {
        if (!connection.IsAssigned || !IsValidSlot(slot) || !IsValidDrop(in state))
            return false;

        return _ingress.TryPost(connection.Source, new WorldItemDropRuntimeCommand(slot, state));
    }

    public bool TryPostRemove(ConnectionHandle connection, short slot)
    {
        if (!connection.IsAssigned || !IsValidSlot(slot))
            return false;

        return _ingress.TryPost(connection.Source, new WorldItemRemoveRuntimeCommand(slot));
    }

    public bool TryPostOwner(ConnectionHandle connection, short slot, in WorldItemOwnerStateUpdate state)
    {
        if (!connection.IsAssigned || !IsValidSlot(slot) || !IsValidOwner(in state))
            return false;

        return _ingress.TryPost(connection.Source, new WorldItemOwnerRuntimeCommand(slot, state));
    }

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
