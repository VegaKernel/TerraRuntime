using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldShimmerLanding1458Tests
{
    [Fact]
    public void Near_ring_uses_source_order_and_requires_ground_within_100_pixels()
    {
        WorldTileStore tiles = CreateFloorWorld();

        Assert.True(VanillaWorldShimmerLanding1458.TryFind(
            tiles,
            positionX: 320f,
            positionY: 480f,
            width: 18,
            height: 40,
            homeless: false,
            homeTileX: 20,
            homeTileY: 36,
            out float x,
            out float y));

        Assert.Equal(311f, x);
        Assert.Equal(456f, y);
    }

    [Fact]
    public void Candidate_with_shimmer_in_actor_plus_ground_probe_is_rejected()
    {
        WorldTileStore tiles = CreateFloorWorld();
        tiles.Set(20, 34, new WorldTile
        {
            LiquidAmount = byte.MaxValue,
            LiquidKind = WorldLiquidKind.Shimmer
        });

        Assert.True(VanillaWorldShimmerLanding1458.TryFind(
            tiles,
            positionX: 320f,
            positionY: 480f,
            width: 18,
            height: 40,
            homeless: false,
            homeTileX: -1,
            homeTileY: -1,
            out float x,
            out float y));

        Assert.NotEqual(311f, x);
        Assert.NotEqual(456f, y);
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
