using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Players;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using Xunit;
namespace TerraRuntime.Tests;
public sealed partial class NpcDefinitionOnlyLifecycle1458Tests
{
    [Fact]
    public void Definition_query_does_not_admit_generation_owned_buff_state_or_packet54()
    {
        foreach (var row in MetadataRows)
        {
            var store = new RuntimeNpcStore(4);
            var update = CreateUpdate(row);
            Assert.True(store.TrySpawn(0, in update, out var npc));
            int publications = 0;
            var status = new RuntimeNpcBuffStatus1458(store, _ => publications++);
            Assert.False(status.EnsureGeneration(npc.Handle));
            Assert.False(status.TryApply(npc.Handle, 60));
            Assert.False(status.TryGetStinky(npc.Handle, out _));
            Assert.False(status.TryPlan(in npc, out _));
            var inner = new CountingStepper();
            var random = new CountingRandom();
            var wrapper = new RuntimeNpcBuffAiStepper1458(inner, status, random);
            Assert.False(wrapper.TryStepState(in npc, out _));
            Assert.Equal(0, inner.Calls);
            Assert.Equal(0, random.Draws);
            Assert.Equal(0, publications);
            Assert.True(store.TryGet(npc.Handle, out var after));
            Assert.Equal(npc, after);
        }
    }

    [Fact]
    public void Direct_loot_transaction_does_not_bypass_metadata_lifecycle_fence()
    {
        foreach (var row in MetadataRows)
        {
            var store = new RuntimeNpcStore(4);
            var items = new RuntimeWorldItemStore();
            var update = CreateUpdate(row) with { Simulation = CreateUpdate(row).Simulation with { Life = 0 } };
            Assert.True(store.TrySpawn(0, in update, out var npc));
            var rolls = new CountingLootRolls();
            var materializer = new RejectingMaterializer();
            var transaction = new RuntimeNpcLootWorldItemTransaction(store, items);
            var output = new WorldItemSnapshot[400];
            Assert.False(transaction.TryFinalizeAndSpawn(npc.Handle, default, rolls, materializer, output, out _));
            Assert.Equal(0, rolls.Calls);
            Assert.Equal(0, materializer.Calls);
            Assert.Equal(0, items.ActiveCount);
            Assert.True(store.TryGet(npc.Handle, out var after));
            Assert.Equal(npc, after);
        }
    }

    [Fact]
    public void Imported_reflects_flag_cannot_enable_vanilla_projectile_effects_for_metadata_only_actor()
    {
        foreach (var row in MetadataRows)
        {
            var npcs = new RuntimeNpcStore(4);
            var update = CreateUpdate(row) with { PositionX = 120, PositionY = 120,
                Simulation = CreateUpdate(row).Simulation with { ReflectsProjectiles = true } };
            Assert.True(npcs.TrySpawn(0, in update, out _));
            var projectiles = new RuntimeProjectileStore(4);
            var arrowUpdate = new ProjectileStateUpdate(VanillaProjectileIds.WoodenArrowFriendly, 0,
                120, 120, 3, 4, default, 0, 20, 1, 20);
            Assert.True(projectiles.TrySpawn(0, in arrowUpdate, out var arrow));
            Assert.True(projectiles.TryGetLifecycle(arrow.Handle, out var lifecycle));
            Assert.True(projectiles.TryCommitSimulationStep(arrow.Handle, in arrowUpdate,
                lifecycle.TimeLeft, out arrow, out var expired));
            Assert.False(expired);
            var random = new ReflectionRandom();
            var pass = new RuntimeNpcProjectileReflectionPass(npcs, projectiles, new ReflectionPlayerLookup(), random);
            Assert.Equal(0, pass.Tick());
            Assert.Equal(0, random.Calls);
            Assert.True(projectiles.TryGet(arrow.Handle, out var after));
            Assert.Equal(arrow, after);
        }
    }

    private sealed class ReflectionRandom : IVanillaProjectileReflectionRandom
    {
        public int Calls;
        public int NextInt32(int min, int max) { Calls++; return min; }
    }
    private sealed class ReflectionPlayerLookup : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot player)
        {
            player = new(new PlayerHandle(slot, new PlayerSessionGeneration(1)), new PlayerStateRevision(1),
                0, 0, 0, 0, 0, 0, 300, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            return true;
        }
    }

    private sealed class CountingLootRolls : INpcLootRollSource
    {
        public int Calls;
        public int RollLuck(int max) { Calls++; return 0; }
        public int NextInt32(int min, int max) { Calls++; return min; }
    }
    private sealed class RejectingMaterializer : INpcLootWorldItemMaterializer
    {
        public int Calls;
        public bool CanMaterialize(ItemTypeId type) { Calls++; return true; }
        public bool TryMaterialize(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop,
            INpcLootRollSource random, out WorldItemDropStateUpdate item)
        { Calls++; item = default; return false; }
    }
}
