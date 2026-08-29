using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Authoritative-thread processor for protocol-326 world-chest commands. Socket-owned sinks only decode and enqueue;
/// this processor owns the actual open/item/rename/close/name-lookup decision and projects committed state through
/// the chest replication registry. Phase-7 inventory conservation remains a separate validation layer so gameplay
/// parity is not blocked on anti-cheat policy that packet 5 itself does not yet enforce.
/// </summary>
internal sealed class RuntimeChestCommandProcessor
{
    private readonly RuntimeChestStore store;
    private readonly RuntimeChestReplicationRegistry replication;

    public RuntimeChestCommandProcessor(
        RuntimeChestStore store,
        RuntimeChestReplicationRegistry replication)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(replication);
        this.store = store;
        this.replication = replication;
    }

    public long AppliedOpens { get; private set; }

    public long RejectedOpens { get; private set; }

    public long AppliedItemUpdates { get; private set; }

    public long RejectedItemUpdates { get; private set; }

    public long AppliedActiveStates { get; private set; }

    public long RejectedActiveStates { get; private set; }

    public long AppliedNameLookups { get; private set; }

    public long RejectedNameLookups { get; private set; }

    /// <summary>
    /// Returns true when the command belongs exclusively to the chest subsystem. Player disconnects are observed for
    /// chest cleanup and deliberately return false so the normal player runtime still commits the disconnect.
    /// </summary>
    public bool TryApply(RuntimeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        switch (command)
        {
            case ClientChestOpenRuntimeCommand open:
                ApplyOpen(open);
                return true;

            case ClientChestItemRuntimeCommand item:
                ApplyItem(item);
                return true;

            case ClientActiveChestRuntimeCommand active:
                ApplyActiveState(active);
                return true;

            case ClientChestNameLookupRuntimeCommand lookup:
                ApplyNameLookup(lookup);
                return true;

            case PlayerDisconnectRuntimeCommand disconnect:
                ApplyDisconnect(disconnect);
                return false;

            default:
                return false;
        }
    }

    private void ApplyOpen(ClientChestOpenRuntimeCommand command)
    {
        bool hadOpenWorldChest = store.TryGetOpenChest(command.Connection, out _);
        TerrariaChestOpenRequest request = command.Request;
        if (!store.TryOpen(command.Connection, request.TileX, request.TileY, out WorldChest chest))
        {
            RejectedOpens++;
            return;
        }

        if (!replication.TrySendOpen(command.Connection, chest))
        {
            // Do not leave a world chest permanently owned when the opening client could not receive its baseline.
            // TryOpen may also have released a previously open chest while switching to this one. If the new baseline
            // cannot be delivered, observers must not keep the old packet-80 chest index indefinitely.
            store.TryClose(command.Connection, out _);
            if (hadOpenWorldChest)
                replication.PublishClosed(command.Connection);
            RejectedOpens++;
            return;
        }

        AppliedOpens++;
    }

    private void ApplyItem(ClientChestItemRuntimeCommand command)
    {
        TerrariaChestItemState submitted = command.State;
        if (!store.TrySetItem(command.Connection, in submitted, out TerrariaChestItemState committed))
        {
            RejectedItemUpdates++;
            return;
        }

        replication.PublishItem(command.Connection, in committed);
        AppliedItemUpdates++;
    }

    private void ApplyActiveState(ClientActiveChestRuntimeCommand command)
    {
        TerrariaActiveChestState submitted = command.State;
        if (!store.TryApplyActiveState(
                command.Connection,
                in submitted,
                out WorldChest? renamedChest,
                out bool closedWorldChest))
        {
            RejectedActiveStates++;
            return;
        }

        if (renamedChest is not null)
            replication.PublishRenamed(command.Connection, renamedChest);
        if (closedWorldChest)
            replication.PublishClosed(command.Connection);

        AppliedActiveStates++;
    }

    private void ApplyNameLookup(ClientChestNameLookupRuntimeCommand command)
    {
        TerrariaChestNameLookupRequest request = command.Request;
        if (!store.TryResolveNameLookup(in request, out WorldChest chest) ||
            !replication.TrySendName(command.Connection, chest))
        {
            RejectedNameLookups++;
            return;
        }

        AppliedNameLookups++;
    }

    private void ApplyDisconnect(PlayerDisconnectRuntimeCommand command)
    {
        if (store.TryClose(command.Connection, out short closedChestId) && closedChestId >= 0)
            replication.PublishClosed(command.Connection);
    }
}
