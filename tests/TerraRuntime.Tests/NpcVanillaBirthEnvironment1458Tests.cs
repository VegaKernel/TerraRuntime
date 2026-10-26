using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class NpcVanillaBirthEnvironment1458Tests
{
    [Fact]
    public void Real_world_composition_samples_NewNPC_wet_before_publishing_both_creation_paths()
    {
        // NPC.NewNPC sets wet from the materialized body before returning/publishing the birth.
        var tiles = new WorldTileStore(new WorldDimensions(100, 80));
        for (int x = 48; x <= 52; x++)
            for (int y = 26; y < 30; y++)
                tiles.Set(x, y, new WorldTile { LiquidAmount = 255 });
        var sink = new Births();
        var npcs = new RuntimeNpcStore(commitSink: sink);
        _ = new ServerRuntimeState(npcs: npcs, worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, false, default, 0, 0),
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with
                { WorldSurface = 60, RockLayer = 65 },
            townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458));
        Assert.True(npcs.TrySpawnIntent(new NpcAiSpawnIntent(new(3), 809, 480, 0, 0, 255)
            { NetIdOverride = new(-27) }, out var intent));
        Assert.True(intent.Simulation.Wet);
        Assert.Equal(intent, sink.Last);
        Assert.True(npcs.TrySpawnVanilla(new(201, 201, 800, 440, 0, 0, 255, default,
            NpcSimulationState.Initial), out var direct));
        Assert.True(direct.Simulation.Wet);
        Assert.Equal(direct, sink.Last);
        Assert.Equal(2, sink.Count);
    }

    [Fact]
    public void Reentrant_world_query_cannot_publish_the_stale_outer_birth()
    {
        var sink = new Births();
        var npcs = new RuntimeNpcStore(commitSink: sink);
        bool entered = false;
        NpcSnapshot child = default;
        npcs.SetVanillaSpawnWetCollisionSource((_, _, _, _) =>
        {
            if (!entered)
            {
                entered = true;
                Assert.True(npcs.TrySpawnIntent(new NpcAiSpawnIntent(new(201), 809, 480, 0, 0, 255), out child));
            }
            return true;
        });
        Assert.False(npcs.TrySpawnIntent(new NpcAiSpawnIntent(new(3), 809, 480, 0, 0, 255), out _));
        Assert.Equal(1, npcs.ActiveCount);
        Assert.Equal(1, sink.Count);
        Assert.Equal(child, sink.Last);
        Assert.True(npcs.TryGet(child.Handle, out var retained));
        Assert.Equal(child, retained);
    }

    [Fact]
    public void Source_no_tile_collision_birth_does_not_query_the_world()
    {
        var npcs = new RuntimeNpcStore();
        npcs.SetVanillaSpawnWetCollisionSource((_, _, _, _) =>
            throw new InvalidOperationException("NewNPC must skip WetCollision for noTileCollide."));
        Assert.True(npcs.TrySpawnVanilla(new(201, 201, 800, 440, 0, 0, 255, default,
            NpcSimulationState.Initial with { NoTileCollide = true }), out var born));
        Assert.False(born.Simulation.Wet);
    }

    [Fact]
    public void Detached_natural_birth_cannot_adopt_after_the_world_callback_changes_its_owner()
    {
        var npcs = new RuntimeNpcStore();
        var random = new VanillaUnifiedRandom1458(1458);
        npcs.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(random));
        Assert.True(npcs.TrySpawnVanilla(new(201, 201, 800, 440, 0, 0, 255, default,
            NpcSimulationState.Initial), out var existing));
        npcs.SetVanillaSpawnWetCollisionSource((_, _, _, _) =>
        {
            var update = new NpcStateUpdate(existing.Type, existing.NetId, existing.PositionX,
                existing.PositionY, 1, 0, existing.Target, existing.Ai, existing.Simulation);
            Assert.True(npcs.TryUpdate(existing.Handle, in update, out _));
            return true;
        });
        var before = random.Clone();
        Assert.True(npcs.TryCreateNaturalSpawnPreview(random, out var preview));
        var requested = new NpcStateUpdate(202, 202, 0, 0, 0, 0, 255, default, NpcSimulationState.Initial);
        Assert.True(preview!.TryStage(in requested, 809, 480, out _));
        Assert.False(preview.TryAdoptOwned(out _));
        Assert.Equal(1, npcs.ActiveCount);
        Assert.True(before.HasSameState(random));
        Assert.True(npcs.TryGet(existing.Handle, out var retained));
        Assert.Equal(1, retained.VelocityX);
    }

    private sealed class Births : INpcStateCommitSink
    {
        internal int Count;
        internal NpcSnapshot Last;
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot)
        {
            if (kind != NpcStateCommitKind.Spawn) return;
            Count++;
            Last = snapshot;
        }
    }
}
