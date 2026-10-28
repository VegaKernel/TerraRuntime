using System.Reflection;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class CaveHouseTileMutation1458Tests
{
    [Fact]
    public void House_unframed_mutations_and_beam_endpoints_preserve_the_official_tile_state()
    {
        // Actual original Actions.SetTileKeepWall.Apply without self-framing and
        // WorldUtils.ClearTile(frameNeighbors:false), on all 160 same preimages.
        // Later SetFrames, wall framing and full house decoration are separate.
        for (int code = 0; code < 80; code++)
        {
            WorldTile before = Preimage(code);
            var block = new WorldTile
            {
                Type = 30,
                Wall = before.Wall,
                Flags = (before.Flags & (WorldTileFlagMasks.Wires | WorldTileFlagMasks.Actuation)) | WorldTileFlags.Active
            };
            Assert.Equal(block, Mutate("SetBlock", before, (ushort)30));

            WorldTile cleared = before;
            cleared.Shape = 0;
            cleared.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive | WorldTileFlags.HalfBrickWithSlope);
            Assert.Equal(cleared, Mutate("ClearTile", before));
        }

        // Source HouseBuilder.PlaceSupportBeams clears BOTH independent shape
        // components on its landing cell, preserving all its other metadata.
        // The explicit x=4 helper fixture keeps all neighbor TileFrame calls
        // behind their five-tile border guard. Thus this checks the mutation
        // stage without claiming its unimplemented interior framing behavior.
        var workspace = new Workspace(64, 64);
        WorldTile landing = Preimage(62);
        landing.Flags |= WorldTileFlags.Actuator | WorldTileFlags.Inactive;
        Assert.True(landing.TrySetSourceShape(true, 4));
        workspace.TileStore.Set(4, 20, landing);
        workspace.TileStore.Set(4, 22, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        CaveHousePlacement1458.PlaceSupportBeamsForTesting(workspace,
            [new(4, 10, 1, 2), new(4, 20, 1, 2)], 124);
        WorldTile expectedLanding = landing;
        expectedLanding.Shape = 0;
        expectedLanding.Flags &= ~WorldTileFlags.HalfBrickWithSlope;
        Assert.Equal(expectedLanding, workspace.TileStore.Get(4, 20));
        Assert.True(workspace.TileStore.Get(4, 20).HasValidSourceShape);
    }

    private static WorldTile Preimage(int code)
    {
        var tile = new WorldTile
        {
            Type = (ushort)(code % 2 == 0 ? 53 : 1),
            Wall = (ushort)(code % 3 == 0 ? 0 : 2),
            FrameX = 36,
            FrameY = -18,
            LiquidAmount = 131,
            LiquidKind = (WorldLiquidKind)(code % 4),
            TileColor = 7,
            WallColor = 9,
            Flags = code % 2 == 0 ? WorldTileFlags.Active : 0
        };
        for (int bit = 1; bit <= 10; bit++)
            if (((code >> ((bit - 1) % 6)) & 1) != 0)
                tile.Flags |= (WorldTileFlags)(1 << bit);
        Assert.True(tile.TrySetSourceShape(code % 2 == 0, (byte)(code % 5)));
        return tile;
    }

    private static WorldTile Mutate(string name, WorldTile before, params object[] extra)
    {
        object[] args = new object[extra.Length + 1];
        args[0] = before;
        extra.CopyTo(args, 1);
        typeof(CaveHousePlacement1458).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);
        return (WorldTile)args[0];
    }
}
