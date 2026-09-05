using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Worlds;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaSimpleTileBreakResolver1458Tests
{
    [Fact]
    public void Fixed_drop_materializes_packet21_state_in_vanilla_item_rng_order()
    {
        var random = new SequenceRandom(-7, -21);
        VanillaTileDefinition definition = VanillaTileDefinitionCatalog.Get(VanillaTileIds.Dirt);

        VanillaSimpleTileBreakOutcome outcome = VanillaSimpleTileBreakResolver1458.Resolve(
            definition,
            tileX: 10,
            tileY: 20,
            closestPlayerHasCordage: false,
            random);

        Assert.Equal(VanillaTileDropResolutionStatus.Resolved, outcome.DropStatus);
        Assert.True(outcome.HasDrop);
        Assert.False(outcome.FillWithHoney);
        Assert.Equal(0, outcome.NpcSpawnCount);
        Assert.Equal(162f, outcome.Drop.PositionX);
        Assert.Equal(322f, outcome.Drop.PositionY);
        Assert.InRange(outcome.Drop.VelocityX, -0.700001f, -0.699999f);
        Assert.InRange(outcome.Drop.VelocityY, -2.100001f, -2.099999f);
        Assert.Equal(1, outcome.Drop.Stack);
        Assert.Equal(VanillaPrefixIds.NoneValue, outcome.Drop.Prefix);
        Assert.Equal(WorldItemOwnershipMode.None, outcome.Drop.Ownership);
        Assert.Equal(VanillaItemIds.DirtBlock.Value, outcome.Drop.ItemNetId);
        Assert.False(outcome.Drop.Shimmered);
        Assert.Equal([(-30, 31), (-40, -15)], random.Requests);
    }

    [Fact]
    public void Cordage_vine_uses_nearest_player_equipment_and_half_chance()
    {
        var random = new SequenceRandom(0, 6, -18);
        VanillaTileDefinition definition = VanillaTileDefinitionCatalog.Get(VanillaTileIds.Vines);

        VanillaSimpleTileBreakOutcome outcome = VanillaSimpleTileBreakResolver1458.Resolve(
            definition,
            tileX: 8,
            tileY: 9,
            closestPlayerHasCordage: true,
            random);

        Assert.True(outcome.HasDrop);
        Assert.Equal(VanillaItemIds.VineRope.Value, outcome.Drop.ItemNetId);
        Assert.Equal([(0, 2), (-30, 31), (-40, -15)], random.Requests);
    }

    [Fact]
    public void Cordage_vine_does_not_drop_rope_without_functional_cordage_item()
    {
        var random = new SequenceRandom(0);
        VanillaTileDefinition definition = VanillaTileDefinitionCatalog.Get(VanillaTileIds.JungleVines);

        VanillaSimpleTileBreakOutcome outcome = VanillaSimpleTileBreakResolver1458.Resolve(
            definition,
            tileX: 8,
            tileY: 9,
            closestPlayerHasCordage: false,
            random);

        Assert.Equal(VanillaTileDropResolutionStatus.NoDrop, outcome.DropStatus);
        Assert.False(outcome.HasDrop);
        Assert.Equal([(0, 2)], random.Requests);
    }

    [Fact]
    public void Mushroom_vine_uses_vanilla_half_chance_for_glowing_mushroom()
    {
        var random = new SequenceRandom(0, 0, -20);
        VanillaTileDefinition definition = VanillaTileDefinitionCatalog.Get(VanillaTileIds.MushroomVines);

        VanillaSimpleTileBreakOutcome outcome = VanillaSimpleTileBreakResolver1458.Resolve(
            definition,
            tileX: 4,
            tileY: 5,
            closestPlayerHasCordage: false,
            random);

        Assert.True(outcome.HasDrop);
        Assert.Equal(VanillaItemIds.GlowingMushroom.Value, outcome.Drop.ItemNetId);
        Assert.Equal([(0, 2), (-30, 31), (-40, -15)], random.Requests);
    }

    [Fact]
    public void Hive_one_third_branch_leaves_full_honey_and_no_drop_or_bee()
    {
        var random = new SequenceRandom(0);
        VanillaTileDefinition definition = VanillaTileDefinitionCatalog.Get(VanillaTileIds.Hive);

        VanillaSimpleTileBreakOutcome outcome = VanillaSimpleTileBreakResolver1458.Resolve(
            definition,
            tileX: 12,
            tileY: 30,
            closestPlayerHasCordage: false,
            random);

        Assert.Equal(VanillaTileDropResolutionStatus.NoDrop, outcome.DropStatus);
        Assert.False(outcome.HasDrop);
        Assert.True(outcome.FillWithHoney);
        Assert.Equal(0, outcome.NpcSpawnCount);
        Assert.Equal([(0, 3)], random.Requests);
    }

    [Fact]
    public void Hive_block_branch_consumes_bee_rng_before_item_rng()
    {
        // non-honey, spawn bees, choose two, Bee then SmallBee, velocity pairs, then item velocity.
        var random = new SequenceRandom(
            1,
            0,
            0,
            VanillaNpcIds.Bee.Value,
            -100,
            50,
            VanillaNpcIds.SmallBee.Value,
            125,
            -75,
            4,
            -17);
        VanillaTileDefinition definition = VanillaTileDefinitionCatalog.Get(VanillaTileIds.Hive);

        VanillaSimpleTileBreakOutcome outcome = VanillaSimpleTileBreakResolver1458.Resolve(
            definition,
            tileX: 12,
            tileY: 30,
            closestPlayerHasCordage: false,
            random);

        Assert.True(outcome.HasDrop);
        Assert.False(outcome.FillWithHoney);
        Assert.Equal(VanillaItemIds.HiveBlock.Value, outcome.Drop.ItemNetId);
        Assert.Equal(2, outcome.NpcSpawnCount);
        Assert.Equal(VanillaNpcIds.Bee, outcome.FirstNpc.Type);
        Assert.Equal(VanillaNpcIds.SmallBee, outcome.SecondNpc.Type);
        Assert.Equal(200, outcome.FirstNpc.BottomX);
        Assert.Equal(495, outcome.FirstNpc.BottomY);
        Assert.Equal((ushort)byte.MaxValue, outcome.FirstNpc.Target);
        Assert.InRange(outcome.FirstNpc.VelocityX, -0.200001f, -0.199999f);
        Assert.InRange(outcome.FirstNpc.VelocityY, 0.099999f, 0.100001f);
        Assert.InRange(outcome.SecondNpc.VelocityX, 0.249999f, 0.250001f);
        Assert.InRange(outcome.SecondNpc.VelocityY, -0.150001f, -0.149999f);
        Assert.Equal(
            [
                (0, 3),
                (0, 2),
                (0, 3),
                (VanillaNpcIds.Bee.Value, VanillaNpcIds.SmallBee.Value + 1),
                (-200, 201),
                (-200, 201),
                (VanillaNpcIds.Bee.Value, VanillaNpcIds.SmallBee.Value + 1),
                (-200, 201),
                (-200, 201),
                (-30, 31),
                (-40, -15)
            ],
            random.Requests);
    }

    [Fact]
    public void Frame_important_contextual_tile_is_rejected_by_simple_cell_resolver()
    {
        VanillaTileDefinition definition = VanillaTileDefinitionCatalog.Get(VanillaTileIds.Torches);
        var random = new SequenceRandom();

        VanillaSimpleTileBreakOutcome outcome = VanillaSimpleTileBreakResolver1458.Resolve(
            definition,
            tileX: 1,
            tileY: 1,
            closestPlayerHasCordage: false,
            random);

        Assert.Equal(VanillaTileDropResolutionStatus.WrongPath, outcome.DropStatus);
        Assert.Empty(random.Requests);
    }

    private sealed class SequenceRandom(params int[] values) : IWorldItemSpawnRandom
    {
        private int index;

        public List<(int Min, int Max)> Requests { get; } = [];

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            Requests.Add((inclusiveMin, exclusiveMax));
            Assert.True(index < values.Length, "Resolver consumed more RNG calls than expected.");
            int value = values[index++];
            Assert.InRange(value, inclusiveMin, exclusiveMax - 1);
            return value;
        }
    }
}
