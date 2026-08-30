using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldLiquidMutationServiceTests
{
    [Fact]
    public void Set_and_clear_liquid_preserve_other_state_and_schedule_affected_cells()
    {
        var tiles = new WorldTileStore(new WorldDimensions(10, 10));
        var original = new WorldTile
        {
            Type = checked((ushort)VanillaTileIds.Stone.Value),
            Wall = checked((ushort)VanillaWallIds.Stone.Value),
            Flags = WorldTileFlags.Active | WorldTileFlags.WireYellow,
            TileColor = 4,
            WallColor = 7
        };
        tiles.Set(4, 5, in original);
        tiles.DirtySections.Clear();
        tiles.PersistenceDirtySections.Clear();
        var service = new VanillaWorldLiquidMutationService(tiles);
        var set = new WorldLiquidMutationRequest(
            WorldLiquidMutationKind.SetLiquid,
            4,
            5,
            200,
            WorldLiquidKind.Shimmer);

        WorldLiquidMutationResult result = service.Apply(in set);

        Assert.True(result.Applied);
        Assert.Equal(5, result.ScheduledCells);
        Assert.Equal(5, tiles.LiquidUpdates.ActiveCount);
        Assert.Equal(VanillaTileIds.Stone, result.After.TileType);
        Assert.Equal(VanillaWallIds.Stone, result.After.WallType);
        Assert.True((result.After.Flags & WorldTileFlags.WireYellow) != 0);
        Assert.Equal((byte)200, result.After.LiquidAmount);
        Assert.Equal(WorldLiquidKind.Shimmer, result.After.LiquidKind);
        Assert.Equal((byte)4, result.After.TileColor);
        Assert.Equal((byte)7, result.After.WallColor);

        var clear = set with { Kind = WorldLiquidMutationKind.ClearLiquid };
        WorldLiquidMutationResult cleared = service.Apply(in clear);
        Assert.True(cleared.Applied);
        Assert.Equal(0, cleared.ScheduledCells);
        Assert.Equal((byte)0, cleared.After.LiquidAmount);
        Assert.Equal(WorldLiquidKind.Water, cleared.After.LiquidKind);
    }

    [Fact]
    public void Edge_scheduling_is_clipped_and_invalid_requests_do_not_mutate()
    {
        var tiles = new WorldTileStore(new WorldDimensions(3, 3));
        var service = new VanillaWorldLiquidMutationService(tiles);
        var edge = new WorldLiquidMutationRequest(
            WorldLiquidMutationKind.SetLiquid,
            0,
            0,
            1,
            WorldLiquidKind.Lava);

        Assert.Equal(3, service.Apply(in edge).ScheduledCells);

        var zero = edge with { X = 1, Y = 1, Amount = 0 };
        var invalidKind = edge with { X = 1, Y = 1, LiquidKind = (WorldLiquidKind)99 };
        var outside = edge with { X = 3 };
        Assert.Equal(WorldLiquidMutationStatus.InvalidLiquid, service.Apply(in zero).Status);
        Assert.Equal(WorldLiquidMutationStatus.InvalidLiquid, service.Apply(in invalidKind).Status);
        Assert.Equal(WorldLiquidMutationStatus.OutOfBounds, service.Apply(in outside).Status);
        Assert.Equal(default, tiles.Get(1, 1));
    }
}
