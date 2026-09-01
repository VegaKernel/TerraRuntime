using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownNpcShimmerService1458Tests
{
    [Fact]
    public void Shimmer_contact_runs_state_25_and_persists_variant_toggle()
    {
        WorldTileStore tiles = CreateFloorWorld();
        var persistence = new WorldNpcPersistence(
            [],
            [new WorldTownNpc(22, "Andrew", 320f, 480f, false, 20, 36, 0, false)],
            []);
        var town = new RuntimeTownNpcStateStore(
            persistence,
            [new WorldTownRoom(22, 20, 36)],
            tiles.Dimensions);
        var npcs = new RuntimeNpcStore();
        Assert.True(town.TryReserveRuntimeSlots(npcs));
        Assert.True(npcs.TryGetActive(0, out NpcSnapshot guide));
        Assert.True(SetShimmerContact(npcs, in guide, out guide));
        var service = new RuntimeTownNpcShimmerService1458(npcs, town, tiles);

        for (int tick = 0; tick < 100; tick++)
            service.Tick();

        Assert.True(npcs.TryGetActive(0, out NpcSnapshot transforming));
        Assert.Equal(25f, transforming.Ai.Ai0);
        Assert.True(transforming.Simulation.DontTakeDamage);
        Assert.True(transforming.Simulation.NoGravity);

        for (int tick = 0; tick < 140; tick++)
            service.Tick();

        Assert.True(npcs.TryGetActive(0, out NpcSnapshot finished));
        Assert.Equal(0f, finished.Ai.Ai0);
        Assert.False(finished.Simulation.DontTakeDamage);
        Assert.False(finished.Simulation.NoGravity);
        WorldNpcPersistence saved = town.CaptureNpcPersistence();
        WorldTownNpc savedGuide = Assert.Single(saved.TownNpcs);
        Assert.Equal(1, savedGuide.TownNpcVariationIndex);
        Assert.Contains(22, saved.ShimmeredTownNpcIndices);
    }

    [Fact]
    public void Second_complete_shimmer_cycle_toggles_variant_back_and_clears_type_flag()
    {
        WorldTileStore tiles = CreateFloorWorld();
        var persistence = new WorldNpcPersistence(
            [22],
            [new WorldTownNpc(22, "Guide", 320f, 480f, false, 20, 36, 1, false)],
            []);
        var town = new RuntimeTownNpcStateStore(
            persistence,
            [new WorldTownRoom(22, 20, 36)],
            tiles.Dimensions);
        var npcs = new RuntimeNpcStore();
        Assert.True(town.TryReserveRuntimeSlots(npcs));
        Assert.True(npcs.TryGetActive(0, out NpcSnapshot guide));
        Assert.True(SetShimmerContact(npcs, in guide, out _));
        var service = new RuntimeTownNpcShimmerService1458(npcs, town, tiles);

        for (int tick = 0; tick < 240; tick++)
            service.Tick();

        WorldNpcPersistence saved = town.CaptureNpcPersistence();
        WorldTownNpc savedGuide = Assert.Single(saved.TownNpcs);
        Assert.Equal(0, savedGuide.TownNpcVariationIndex);
        Assert.DoesNotContain(22, saved.ShimmeredTownNpcIndices);
    }

    [Fact]
    public void Non_transformable_town_pet_never_enters_state_25()
    {
        WorldTileStore tiles = CreateFloorWorld();
        var persistence = new WorldNpcPersistence(
            [],
            [new WorldTownNpc(637, "Cat", 320f, 480f, true, -1, -1, 0, false)],
            []);
        var town = new RuntimeTownNpcStateStore(persistence, [], tiles.Dimensions);
        var npcs = new RuntimeNpcStore();
        Assert.True(town.TryReserveRuntimeSlots(npcs));
        Assert.True(npcs.TryGetActive(0, out NpcSnapshot cat));
        Assert.True(SetShimmerContact(npcs, in cat, out _));
        var service = new RuntimeTownNpcShimmerService1458(npcs, town, tiles);

        for (int tick = 0; tick < 120; tick++)
            service.Tick();

        Assert.True(npcs.TryGetActive(0, out NpcSnapshot after));
        Assert.NotEqual(25f, after.Ai.Ai0);
        Assert.Equal(0, Assert.Single(town.CaptureNpcPersistence().TownNpcs).TownNpcVariationIndex);
    }

    private static bool SetShimmerContact(RuntimeNpcStore npcs, in NpcSnapshot source, out NpcSnapshot updated)
    {
        var state = new NpcStateUpdate(
            source.Type,
            source.NetId,
            source.PositionX,
            source.PositionY,
            source.VelocityX,
            source.VelocityY,
            source.Target,
            source.Ai,
            source.Simulation with
            {
                Wet = true,
                LiquidContact = NpcLiquidContactKind.Shimmer
            });
        return npcs.TryUpdate(source.Handle, in state, out updated);
    }

    private static WorldTileStore CreateFloorWorld()
    {
        var tiles = new WorldTileStore(new WorldDimensions(80, 100));
        for (int x = 0; x < 80; x++)
        {
            tiles.Set(x, 36, new WorldTile
            {
                Type = checked((ushort)VanillaTileIds.Dirt.Value),
                Flags = WorldTileFlags.Active
            });
        }
        return tiles;
    }
}
