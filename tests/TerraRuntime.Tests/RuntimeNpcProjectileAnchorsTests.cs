using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcProjectileAnchorsTests
{
    [Fact]
    public void Pending_player_buff_snapshot_selects_returning_foreign_head_leech_and_disconnect_clears_it()
    {
        var players = new PlayerAuthority(null, null);
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(51),
            new PlayerHandle(new PlayerSlotId(1), new PlayerSessionGeneration(1)));
        var store = new RuntimeProjectileStore(4);
        var state = new ProjectileStateUpdate(VanillaProjectileIds.MoonLeech, 255, 1200, 1000, 0, 0,
            new ProjectileAiState(-6, 1, 0), 0, 0, 0, 0);
        Assert.True(store.TrySpawn(0, in state, out var shot));
        var identities = new RuntimeProjectileWireIdentityRegistry(4);
        var key = new TerrariaProjectileKeyState(255, 30, 8160);
        Assert.True(identities.TryBind(in key, shot.Handle));
        var anchors = new RuntimeNpcProjectileAnchors(store, identities, players);
        Assert.False(anchors.TryGetHealingAnchor(0, out _));
        var buffs = new PlayerBuffTypesCommitRequest(connection.Player.Slot, new BuffTypeId[] { VanillaBuffIds.MoonLeech });
        Assert.True(players.TryApply(new PlayerBuffTypesRuntimeCommand(connection, buffs)));
        Assert.False(players.TryGet(connection, out _)); // The player has not entered the active membership yet.
        Assert.True(anchors.TryGetHealingAnchor(0, out float bits));
        Assert.Equal(255u | (30u << 8) | (8160u << 18), BitConverter.SingleToUInt32Bits(bits));
        Assert.True(anchors.TryGetProjectile(bits, out var resolved));
        Assert.Equal(shot.Handle, resolved.Handle);
        var clear = new PlayerBuffTypesCommitRequest(connection.Player.Slot, Array.Empty<BuffTypeId>());
        Assert.True(players.TryApply(new PlayerBuffTypesRuntimeCommand(connection, clear)));
        Assert.False(anchors.TryGetHealingAnchor(0, out _));
        Assert.True(players.TryApply(new PlayerBuffTypesRuntimeCommand(connection, buffs)));
        Assert.True(players.TryApply(new PlayerDisconnectRuntimeCommand(connection)));
        Assert.False(anchors.TryGetHealingAnchor(0, out _));
    }

    [Fact]
    public void Head_scan_preserves_shadowed_physical_keys_but_anchor_lookup_uses_current_forward_binding()
    {
        var players = new PlayerAuthority(null, null);
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(52),
            new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1)));
        var buffs = new PlayerBuffTypesCommitRequest(connection.Player.Slot, new BuffTypeId[] { VanillaBuffIds.MoonLeech });
        Assert.True(players.TryApply(new PlayerBuffTypesRuntimeCommand(connection, buffs)));
        var store = new RuntimeProjectileStore(4);
        var state = new ProjectileStateUpdate(VanillaProjectileIds.MoonLeech, 255, 1200, 1000, 0, 0,
            new ProjectileAiState(99, 0, 0), 0, 0, 0, 0);
        Assert.True(store.TrySpawn(0, in state, out var old));
        Assert.True(store.TrySpawn(1, in state, out var current));
        var identities = new RuntimeProjectileWireIdentityRegistry(4);
        var oldKey = new TerrariaProjectileKeyState(255, 777, 16383);
        var newKey = new TerrariaProjectileKeyState(255, 777, 0);
        Assert.True(identities.TryBind(in oldKey, old.Handle));
        Assert.True(identities.TryBind(in newKey, current.Handle));
        var anchors = new RuntimeNpcProjectileAnchors(store, identities, players);
        Assert.True(anchors.TryGetHealingAnchor(0, out float oldBits));
        Assert.True(anchors.TryGetHealingAnchor(1, out float newBits));
        Assert.False(anchors.TryGetProjectile(oldBits, out _));
        Assert.True(anchors.TryGetProjectile(newBits, out var resolved));
        Assert.Equal(current.Handle, resolved.Handle);
        Assert.True(store.TryDespawn(old.Handle, out _));
        Assert.False(anchors.TryGetHealingAnchor(0, out _));
        state = state with { Type = VanillaProjectileIds.WoodenArrowFriendly };
        Assert.True(store.TryUpdate(current.Handle, in state, out _));
        Assert.False(anchors.TryGetHealingAnchor(1, out _));
        Assert.False(anchors.TryGetHealingAnchor(999, out _));
    }
}
