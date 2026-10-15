using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeChestReplicationRegistry
{
    internal bool IsCurrentCraftingActor(ConnectionHandle connection) =>
        connection.IsAssigned && endpoints.TryGetValue(connection.Source, out Endpoint? endpoint) &&
        endpoint.IsPlaying(connection.Player) && !endpoint.Outbound.IsCompleted;

    internal int GetMaximumCraftingChanges(ConnectionHandle connection)
    {
        if (!IsCurrentCraftingActor(connection) || !endpoints.TryGetValue(connection.Source, out var endpoint))
            return 0;
        // Each packet32 is11 bytes; the approval is6. This also bounds retained
        // sparse changes by existing host queue limits instead of a new ledger.
        return (int)Math.Max(0, Math.Min(endpoint.Outbound.MaxFrames - 1L,
            (endpoint.Outbound.MaxQueuedBytes - 6) / 11));
    }

    internal bool CanPublishCrafting(ConnectionHandle connection, ReadOnlySpan<TerrariaChestItemState> changes)
    {
        if (!IsCurrentCraftingActor(connection)) return false;
        foreach ((var source, Endpoint endpoint) in endpoints)
        {
            if (!endpoint.IsPlaying()) continue;
            if (source == connection.Source && !endpoint.IsPlaying(connection.Player)) return false;
            int count = changes.Length + (source == connection.Source ? 1 : 0);
            long bytes = (long)changes.Length * 11 + (source == connection.Source ? 6 : 0);
            if (endpoint.Outbound.IsCompleted ||
                (long)endpoint.Outbound.QueuedFrames + count > endpoint.Outbound.MaxFrames ||
                endpoint.Outbound.QueuedBytes + bytes > endpoint.Outbound.MaxQueuedBytes)
                return false;
        }
        return true;
    }

    internal void PublishCrafting(ConnectionHandle connection, ReadOnlySpan<TerrariaChestItemState> changes,
        bool approved)
    {
        if (!IsCurrentCraftingActor(connection)) return;
        var updates = new OutboundFrame[changes.Length];
        for (int index = 0; index < changes.Length; index++)
            updates[index] = new(TerrariaChestCodec.EncodeChestItem(in changes[index]));

        foreach ((var source, Endpoint endpoint) in endpoints)
        {
            bool requester = source == connection.Source;
            if (!endpoint.IsPlaying() || requester && !endpoint.IsPlaying(connection.Player)) continue;
            if (!approved && !requester) continue;
            var batch = new OutboundFrame[updates.Length + (requester ? 1 : 0)];
            updates.CopyTo(batch, 0);
            if (requester) batch[^1] = new(TerrariaCraftingRequestCodec1458.EncodeResponse(approved));
            // Unlike client packet32 updates, original server crafting broadcasts
            // its accepted slots to the requester too, before its boolean response.
            if (endpoint.Outbound.TryEnqueueBatch(batch) == OutboundEnqueueResult.Enqueued)
                ItemFrames += updates.Length;
            else
                RejectedFrames += batch.Length;
        }
    }
}
