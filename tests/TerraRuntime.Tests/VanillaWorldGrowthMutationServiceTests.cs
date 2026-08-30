using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldGrowthMutationServiceTests
{
    [Fact]
    public void Guarded_spread_commit_changes_only_tile_identity_and_shape_frame_state()
    {
        var tiles = new WorldTileStore(new WorldDimensions(20, 20));
        var dirt = new WorldTile
        {
            Type = checked((ushort)VanillaTileIds.Dirt.Value),
            Wall = checked((ushort)VanillaWallIds.DirtUnsafe.Value),
            FrameX = 18,
            FrameY = 36,
            Flags = WorldTileFlags.Active | WorldTileFlags.WireGreen,
            LiquidAmount = 12,
            LiquidKind = WorldLiquidKind.Water,
            TileColor = 3,
            WallColor = 4,
            Shape = 2
        };
        tiles.Set(8, 9, in dirt);
        tiles.DirtySections.Clear();
        tiles.PersistenceDirtySections.Clear();
        var service = new VanillaWorldGrowthMutationService(tiles);
        var request = new WorldGrowthMutationRequest(
            WorldGrowthMutationKind.Spread,
            8,
            9,
            VanillaTileIds.Dirt,
            VanillaTileIds.Grass);

        WorldGrowthMutationResult result = service.Apply(in request);

        Assert.True(result.Applied);
        Assert.Equal(VanillaTileIds.Grass, result.After.TileType);
        Assert.Equal(VanillaWallIds.DirtUnsafe, result.After.WallType);
        Assert.True((result.After.Flags & WorldTileFlags.WireGreen) != 0);
        Assert.Equal((byte)12, result.After.LiquidAmount);
        Assert.Equal((byte)3, result.After.TileColor);
        Assert.Equal((byte)4, result.After.WallColor);
        Assert.Equal((short)0, result.After.FrameX);
        Assert.Equal((short)0, result.After.FrameY);
        Assert.Equal((byte)0, result.After.Shape);
        Assert.Equal(1, tiles.DirtySections.DirtyCount);
        Assert.Equal(1, tiles.PersistenceDirtySections.DirtyCount);
    }

    [Fact]
    public void Stale_invalid_and_frame_important_growth_requests_fail_closed()
    {
        var tiles = new WorldTileStore(new WorldDimensions(20, 20));
        var dirt = new WorldTile
        {
            Type = checked((ushort)VanillaTileIds.Dirt.Value),
            Flags = WorldTileFlags.Active
        };
        tiles.Set(4, 4, in dirt);
        tiles.DirtySections.Clear();
        tiles.PersistenceDirtySections.Clear();
        var service = new VanillaWorldGrowthMutationService(tiles);
        var stale = new WorldGrowthMutationRequest(
            WorldGrowthMutationKind.Grow,
            4,
            4,
            VanillaTileIds.Stone,
            VanillaTileIds.Grass);
        var frameImportant = stale with
        {
            ExpectedTileType = VanillaTileIds.Dirt,
            ResultTileType = VanillaTileIds.Containers
        };
        var invalid = stale with
        {
            ExpectedTileType = VanillaTileIds.Dirt,
            ResultTileType = new TileTypeId(VanillaTileIds.Count)
        };

        Assert.Equal(WorldGrowthMutationStatus.SourceMismatch, service.Apply(in stale).Status);
        Assert.Equal(WorldGrowthMutationStatus.UnsupportedTile, service.Apply(in frameImportant).Status);
        Assert.Equal(WorldGrowthMutationStatus.InvalidContent, service.Apply(in invalid).Status);
        Assert.Equal(VanillaTileIds.Dirt, tiles.Get(4, 4).TileType);
        Assert.Equal(0, tiles.DirtySections.DirtyCount);
        Assert.Equal(0, tiles.PersistenceDirtySections.DirtyCount);
    }
}
