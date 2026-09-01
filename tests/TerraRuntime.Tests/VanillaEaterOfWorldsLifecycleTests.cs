using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaEaterOfWorldsLifecycleTests
{
    [Fact]
    public void Player_interaction_is_propagated_to_every_active_eater_segment()
    {
        var store = new RuntimeNpcStore(capacity: 8);
        var ledger = new RuntimeNpcPlayerInteractionLedger(store);
        NpcSnapshot head = Spawn(store, 0, VanillaNpcIds.EaterOfWorldsHead);
        NpcSnapshot body = Spawn(store, 1, VanillaNpcIds.EaterOfWorldsBody);
        NpcSnapshot tail = Spawn(store, 2, VanillaNpcIds.EaterOfWorldsTail);
        _ = Spawn(store, 3, VanillaNpcIds.BlueSlime);
        var player = new PlayerHandle(new PlayerSlotId(5), new PlayerSessionGeneration(1));
        var buffer = new NpcSnapshot[store.Capacity];

        Assert.Equal(3, VanillaEaterOfWorldsLifecycle.MarkPlayerInteractionAcrossActiveSegments(
            store, ledger, player, buffer));

        Assert.True(ledger.HasInteraction(head.Handle, player.Slot));
        Assert.True(ledger.HasInteraction(body.Handle, player.Slot));
        Assert.True(ledger.HasInteraction(tail.Handle, player.Slot));
    }

    [Fact]
    public void Only_final_active_segment_is_promoted_to_boss_for_death_loot()
    {
        var store = new RuntimeNpcStore(capacity: 8);
        NpcSnapshot head = Spawn(store, 0, VanillaNpcIds.EaterOfWorldsHead);
        NpcSnapshot body = Spawn(store, 1, VanillaNpcIds.EaterOfWorldsBody);
        NpcSnapshot tail = Spawn(store, 2, VanillaNpcIds.EaterOfWorldsTail);
        var buffer = new NpcSnapshot[store.Capacity];

        Assert.False(VanillaEaterOfWorldsLifecycle.IsLastActiveSegment(store, in body, buffer));
        Assert.True(store.TryDespawn(head.Handle));
        Assert.True(store.TryDespawn(tail.Handle));
        Assert.True(VanillaEaterOfWorldsLifecycle.IsLastActiveSegment(store, in body, buffer));
    }

    private static NpcSnapshot Spawn(RuntimeNpcStore store, byte slot, NpcTypeId type)
    {
        var update = new NpcStateUpdate(
            Type: type.Value,
            NetId: checked((short)type.Value),
            PositionX: 32f + slot * 20f,
            PositionY: 64f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial with
            {
                Life = 150,
                LifeMax = 150
            });
        Assert.True(store.TrySpawn(slot, in update, out NpcSnapshot spawned));
        return spawned;
    }
}
