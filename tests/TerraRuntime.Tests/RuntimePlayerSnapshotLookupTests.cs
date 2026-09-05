using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;

namespace TerraRuntime.Tests;

public sealed class RuntimePlayerSnapshotLookupTests
{
    [Fact]
    public void ServerPlayerSlotReuseRejectsStaleGeneration()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new ServerPlayerSlotRegistry(slots);
        var states = new ServerPlayerStateStore(identities, slots.Capacity);
        var serverPlayers = new ServerPlayerAuthority(states, identities);
        var players = new PlayerAuthority(events: null, worldTiles: null);
        var lookup = new RuntimePlayerSnapshotLookup(players, serverPlayers);
        var id = new ServerPlayerId("test:snapshot-lookup");

        ServerPlayerCreateResult first = serverPlayers.Create(id, 10f, 20f);
        Assert.True(first.IsCreated);
        Assert.True(lookup.TryGetPlayer(first.Player, out PlayerStateSnapshot firstSnapshot));
        Assert.Equal(first.Player, firstSnapshot.Player);
        Assert.True(lookup.TryGetPlayer(first.Player.Slot, out PlayerStateSnapshot slotSnapshot));
        Assert.Equal(first.Player, slotSnapshot.Player);

        Assert.True(serverPlayers.Despawn(id));
        ServerPlayerCreateResult second = serverPlayers.Create(id, 30f, 40f);
        Assert.True(second.IsCreated);
        Assert.Equal(first.Player.Slot, second.Player.Slot);
        Assert.NotEqual(first.Player.Generation, second.Player.Generation);

        Assert.False(lookup.TryGetPlayer(first.Player, out _));
        Assert.True(lookup.TryGetPlayer(second.Player, out PlayerStateSnapshot current));
        Assert.Equal(30f, current.PositionX);
        Assert.True(lookup.TryGetPlayer(second.Player.Slot, out PlayerStateSnapshot currentBySlot));
        Assert.Equal(second.Player, currentBySlot.Player);
    }
}
