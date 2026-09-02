using TerraRuntime.Gameplay.Items;
using System.Collections.Concurrent;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime;

internal sealed partial class RuntimeConnectionRegistry
{
    internal bool TryGetServerPlayerAppearanceFrame(PlayerHandle player, out OutboundFrame frame) =>
        _serverPlayers.TryGetAppearanceFrame(player, out frame);

    internal bool TryGetServerPlayerHealthFrame(PlayerHandle player, out OutboundFrame frame) =>
        _serverPlayers.TryGetHealthFrame(player, out frame);

    internal bool TryGetServerPlayerMovementFrame(PlayerHandle player, out OutboundFrame frame) =>
        _serverPlayers.TryGetMovementFrame(player, out frame);

    internal bool TryGetServerPlayerItemFrame(
        PlayerHandle player,
        short slot,
        out OutboundFrame frame) =>
        _serverPlayers.TryGetItemFrame(player, slot, out frame);

    public void ServerPlayerCreated(in PlayerStateSnapshot player)
    {
        if (!_serverPlayers.TryCreate(in player, out byte[] active, out byte[] movement))
            return;

        Interlocked.Add(ref _playerActiveBaselineFrames, BroadcastToPlaying(active));
        Interlocked.Add(ref _relayedMovementFrames, BroadcastToPlaying(movement));
    }

    public void ServerPlayerAppearanceUpdated(
        PlayerHandle player,
        in ServerPlayerAppearanceState appearance)
    {
        if (_serverPlayers.TryUpdateAppearance(player, in appearance, out byte[] encoded))
            Interlocked.Add(ref _relayedAppearanceFrames, BroadcastToPlaying(encoded));
    }

    public void ServerPlayerVitalsUpdated(
        PlayerHandle player,
        in ServerPlayerVitalsState vitals)
    {
        if (!_serverPlayers.TryUpdateVitals(player, in vitals, out byte[] health, out byte[] mana))
            return;

        Interlocked.Add(ref _serverPlayerHealthFrames, BroadcastToPlaying(health));
        Interlocked.Add(ref _serverPlayerManaFrames, BroadcastToPlaying(mana));
    }

    public void ServerPlayerItemUpdated(PlayerHandle player, in ServerPlayerItemState item)
    {
        if (_serverPlayers.TryUpdateItem(player, in item, out byte[] encoded))
            Interlocked.Add(ref _relayedEquipmentFrames, BroadcastToPlaying(encoded));
    }

    public void ServerPlayerMoved(in PlayerStateSnapshot player)
    {
        if (_serverPlayers.TryUpdateMovement(in player, out byte[] encoded))
            Interlocked.Add(ref _relayedMovementFrames, BroadcastToPlaying(encoded));
    }

    public void ServerPlayerDespawned(PlayerHandle player)
    {
        if (_serverPlayers.TryRemove(player, out byte[] inactive))
            Interlocked.Add(ref _playerDeactivationFrames, BroadcastToPlaying(inactive));
    }


    private void SynchronizeServerPlayerBaselines(RuntimeConnectionEndpoint recipient)
    {
        ServerPlayerBaselineEnqueueCounts counts = _serverPlayers.EnqueueBaselines(recipient);
        Interlocked.Add(ref _playerActiveBaselineFrames, counts.Active);
        Interlocked.Add(ref _appearanceBaselineFrames, counts.Appearance);
        Interlocked.Add(ref _equipmentBaselineFrames, counts.Equipment);
        Interlocked.Add(ref _serverPlayerHealthFrames, counts.Health);
        Interlocked.Add(ref _serverPlayerManaFrames, counts.Mana);
        Interlocked.Add(ref _movementResyncFrames, counts.Movement);
    }

    private int BroadcastToPlaying(byte[] encoded)
    {
        int enqueued = 0;
        var frame = new OutboundFrame(encoded);
        foreach (RuntimeConnectionEndpoint endpoint in _endpoints.Values)
        {
            if (endpoint.TryGetPlayingSlot(out _) &&
                endpoint.Outbound.TryEnqueue(frame) == OutboundEnqueueResult.Enqueued)
            {
                enqueued++;
            }
        }

        return enqueued;
    }
}
