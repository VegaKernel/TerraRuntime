using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Posts validated, packet-neutral dropped-item mutations from one authenticated connection into the
/// authoritative runtime. Wire sentinels and packet layouts must be resolved before crossing this boundary.
/// </summary>
public interface IWorldItemIngress
{
    bool TryPostAllocate(ConnectionHandle connection, in WorldItemDropStateUpdate state);

    bool TryPostDrop(ConnectionHandle connection, short slot, in WorldItemDropStateUpdate state);

    bool TryPostRemove(ConnectionHandle connection, short slot);

    bool TryPostOwner(ConnectionHandle connection, short slot, in WorldItemOwnerStateUpdate state);
}
