using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaLarvaObjectMutationService1458Tests
{
    [Fact]
    public void Arbitrary_styled_cell_resolves_and_complete_object_break_preserves_independent_state()
    {
        var tiles = new WorldTileStore(new WorldDimensions(200, 150));
        PlaceLarva(tiles, left: 40, top: 50, styleX: 54, styleY: 108);
        WorldSectionId section = TerrariaSectionGeometry.FromTile(tiles.Dimensions, 40, 50);
        Span<WorldSectionId> dirty = stackalloc WorldSectionId[4];
        _ = tiles.DirtySections.Drain(dirty);
        long beforeVersion = tiles.GetSectionVersion(section);
        var service = new VanillaLarvaObjectMutationService1458(tiles);

        VanillaLarvaObjectMutationResult1458 result = service.TryBreakAt(42, 51);

        Assert.True(result.Applied);
        Assert.Equal(new WorldTileRegion(40, 50, 3, 3), result.Bounds);
        Assert.Equal(9, result.ChangedTiles);
        for (int y = 50; y < 53; y++)
        {
            for (int x = 40; x < 43; x++)
            {
                WorldTile cell = tiles.Get(x, y);
                Assert.False(cell.IsActive);
                Assert.Equal((ushort)7, cell.Wall);
                Assert.Equal((byte)90, cell.LiquidAmount);
                Assert.True((cell.Flags & WorldTileFlags.WireRed) != 0);
            }
        }
        Assert.True(tiles.GetSectionVersion(section) > beforeVersion);
        Assert.Equal(1, tiles.DirtySections.DirtyCount);
    }

    [Fact]
    public void Corrupt_frame_rejects_without_partial_world_mutation()
    {
        var tiles = new WorldTileStore(new WorldDimensions(200, 150));
        PlaceLarva(tiles, left: 40, top: 50, styleX: 0, styleY: 0);
        WorldTile corrupt = tiles.Get(42, 52);
        corrupt.FrameX = 18;
        tiles.Set(42, 52, in corrupt);
        Span<WorldSectionId> dirty = stackalloc WorldSectionId[4];
        _ = tiles.DirtySections.Drain(dirty);
        WorldTile[] before = Capture(tiles, 40, 50);
        var service = new VanillaLarvaObjectMutationService1458(tiles);

        VanillaLarvaObjectMutationResult1458 result = service.TryBreakAt(41, 51);

        Assert.Equal(VanillaLarvaObjectMutationStatus1458.InvalidObjectState, result.Status);
        Assert.Equal(0, result.ChangedTiles);
        Assert.Equal(before, Capture(tiles, 40, 50));
        Assert.Equal(0, tiles.DirtySections.DirtyCount);
    }

    private static void PlaceLarva(WorldTileStore tiles, int left, int top, short styleX, short styleY)
    {
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                var tile = new WorldTile
                {
                    Type = checked((ushort)VanillaTileIds.Larva.Value),
                    Wall = 7,
                    FrameX = checked((short)(styleX + x * 18)),
                    FrameY = checked((short)(styleY + y * 18)),
                    Flags = WorldTileFlags.Active | WorldTileFlags.WireRed,
                    LiquidAmount = 90,
                    LiquidKind = WorldLiquidKind.Honey
                };
                tiles.Set(left + x, top + y, in tile);
            }
        }
    }

    private static WorldTile[] Capture(WorldTileStore tiles, int left, int top)
    {
        var result = new WorldTile[9];
        int index = 0;
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
                result[index++] = tiles.Get(left + x, top + y);
        }
        return result;
    }
}
