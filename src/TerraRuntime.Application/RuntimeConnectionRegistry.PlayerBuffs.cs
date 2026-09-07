using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Buffs;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeConnectionRegistry
{
    public void PlayerBuffTypesUpdated(ConnectionHandle connection, in PlayerBuffTypesCommitRequest request)
    {
        if (!connection.IsAssigned ||
            connection.Player.Slot != request.PlayerSlot ||
            request.BuffTypes.Length > TerrariaPlayerBuffCodec1458.MaximumBuffs ||
            !_endpoints.TryGetValue(connection.Source, out RuntimeConnectionEndpoint? endpoint))
        {
            return;
        }

        ReadOnlySpan<BuffTypeId> buffs = request.BuffTypes.Span;
        for (int i = 0; i < buffs.Length; i++)
        {
            if (buffs[i] == VanillaBuffIds.None || !VanillaBuffIds.TryCreate(buffs[i].Value, out _))
                return;
        }

        byte[] encoded = TerrariaPlayerBuffCodec1458.Encode(connection.Player.Slot.Value, buffs);
        bool changed = endpoint.UpdateLatestBuffFrame(connection.Player, encoded);

        // Initial packet 50 arrives during the vanilla handshake before PlayerSpawned marks the endpoint playing.
        // Cache it now and let the ordinary spawn baseline exchange publish it later.
        if (!endpoint.TryGetPlayingPlayer(out PlayerHandle current) || current != connection.Player)
            return;
        if (!changed)
        {
            Interlocked.Increment(ref _suppressedDuplicateBuffFrames);
            return;
        }

        Interlocked.Add(ref _relayedBuffFrames, BroadcastToPlayingExcept(connection.Source, encoded));
    }

    internal bool TryGetLatestPlayerBuffFrame(PlayerSlotId slot, out OutboundFrame frame)
    {
        if (!TryGetPlayingEndpoint(slot, out RuntimeConnectionEndpoint endpoint) ||
            !endpoint.TryGetPlayingPlayer(out PlayerHandle player))
        {
            frame = default;
            return false;
        }

        return endpoint.TryGetLatestBuffFrame(player, out frame);
    }
    public void PlayerPvpBuffApplied(PlayerHandle player, BuffTypeId buffType, int durationTicks)
    {
        if (!player.IsAssigned ||
            buffType == VanillaBuffIds.None ||
            !VanillaBuffIds.TryCreate(buffType.Value, out _) ||
            !VanillaPvpBuffFacts1458.IsRelayable(buffType) ||
            durationTicks <= 0 ||
            !TryGetPlayingEndpoint(player, out RuntimeConnectionEndpoint endpoint))
        {
            return;
        }

        byte[] encoded = TerrariaPlayerPvpBuffCodec1458.Encode(player.Slot.Value, buffType, durationTicks);
        if (endpoint.Outbound.TryEnqueue(new OutboundFrame(encoded)) == OutboundEnqueueResult.Enqueued)
            Interlocked.Increment(ref _relayedPvpBuffFrames);
    }

}
