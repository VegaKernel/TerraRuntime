using TerraRuntime.Gameplay.Items;
using System.Collections.Concurrent;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

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

    public void ServerPlayerPvpUpdated(PlayerHandle player, bool hostile)
    {
        if (_serverPlayers.TryUpdatePvp(player, hostile, out byte[] encoded))
            Interlocked.Add(ref _relayedPvpFrames, BroadcastToPlaying(encoded));
    }


    public void ServerPlayerGodModeUpdated(PlayerHandle player, bool enabled) =>
        PlayerGodModeChanged(player, enabled);

    public void ServerPlayerBuffTypesUpdated(PlayerHandle player, ReadOnlySpan<BuffTypeId> buffs)
    {
        if (_serverPlayers.TryUpdateBuffTypes(player, buffs, out byte[] encoded))
            Interlocked.Add(ref _relayedBuffFrames, BroadcastToPlaying(encoded));
    }

    public void ServerPlayerDied(
        PlayerHandle player,
        DamageSource source,
        ProjectileTypeId projectileType,
        int damage,
        int hitDirection)
    {
        TerrariaPlayerDeathReasonState reason = source.Kind switch
        {
            DamageSourceKind.Environment when source.EnvironmentCause is EnvironmentDamageCause.Lava or EnvironmentDamageCause.Burning =>
                new TerrariaPlayerDeathReasonState(-1, -1, -1,
                    (sbyte)(source.EnvironmentCause == EnvironmentDamageCause.Lava ? 2 : 8), 0, 0, 0, null),
            DamageSourceKind.PlayerItem or DamageSourceKind.PlayerProjectile when source.Player.IsAssigned =>
                new TerrariaPlayerDeathReasonState(source.Player.Slot.Value, -1, -1, -1, 0, 0, 0, null),
            DamageSourceKind.NpcContact when source.Npc.IsAssigned => new TerrariaPlayerDeathReasonState(
                -1, checked((short)source.Npc.Slot), -1, -1, 0, 0, 0, null),
            // Projectile.Damage_EVP in TerrariaServer 1.4.5.8 uses PlayerDeathReason.ByProjectile(-1, whoAmI)
            // for ordinary hostile NPC projectiles. Player index 255 would be a materially different death reason.
            DamageSourceKind.NpcProjectile when source.Npc.IsAssigned && source.Projectile.IsAssigned && projectileType != default =>
                new TerrariaPlayerDeathReasonState(
                    -1, -1, checked((short)source.Projectile.Slot), -1, checked((short)projectileType.Value), 0, 0, null),
            _ => default
        };
        bool knownEnvironment = source.Kind == DamageSourceKind.Environment &&
            source.EnvironmentCause is EnvironmentDamageCause.Lava or EnvironmentDamageCause.Burning;
        if (!source.IsValid || (!knownEnvironment && source.Kind is not (DamageSourceKind.NpcContact or DamageSourceKind.NpcProjectile or DamageSourceKind.PlayerItem or DamageSourceKind.PlayerProjectile)) ||
            damage is < 0 or > short.MaxValue || hitDirection is < -1 or > 1)
        {
            return;
        }

        var death = new TerrariaPlayerDeathState(
            player.Slot.Value,
            reason,
            checked((short)damage),
            checked((byte)(hitDirection + 1)),
            Flags: (byte)(source.Kind is DamageSourceKind.PlayerItem or DamageSourceKind.PlayerProjectile ? 1 : 0));
        if (TerrariaPlayerCombatCodec.TryEncodeDeath(in death, out byte[] encoded) == TerrariaPlayerDeathEncodeResult.Encoded)
            BroadcastToPlaying(encoded);
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

    public void ServerPlayerItemUsePresented(PlayerHandle player, float rotation, short animationTicks) =>
        BroadcastToPlaying(TerrariaPlayerReplicationFrameEncoder.EncodeItemAnimation(player.Slot, rotation, animationTicks));

    public void ServerPlayerRecallPresented(in PlayerStateSnapshot player, short floorX, short floorY)
    {
        // Packet 12 updates the remote actor's SpawnX/Y before Spawn(RecallFromItem=2). Without it the
        // observer's Magic Mirror ItemCheck can recall a fake player to an unrelated world-spawn position.
        var recall = new PlayerSpawnCommitRequest(player.Player.Slot, floorX, floorY, 0, 0, 0, player.Team, 2);
        BroadcastToPlaying(TerrariaPlayerReplicationFrameEncoder.EncodeSpawn(in recall));
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
        Interlocked.Add(ref _relayedPvpFrames, counts.Pvp);
        Interlocked.Add(ref _serverPlayerHealthFrames, counts.Health);
        Interlocked.Add(ref _serverPlayerManaFrames, counts.Mana);
        Interlocked.Add(ref _movementResyncFrames, counts.Movement);
        Interlocked.Add(ref _relayedBuffFrames, counts.Buffs);
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
