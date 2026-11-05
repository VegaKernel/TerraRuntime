using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationRemixDesertLava1458Tests
{
    // Untouched TerrariaServer.exe 1.4.5.8 registered "Settle Liquids Again",
    // seed 1458, 1000x500. The same open two-cell lava pocket and wall 187
    // exercises Liquid.LavaCheck's UndergroundDesertCheck in an ordinary world,
    // while the official Remix guard leaves every dry neighbour's kind intact.
    [Theory]
    [InlineData(true,
        "9DA69F44A1B8C546F7B517B7BF2043E39D9351A5A5429C09B6C26BAB567CD47F",
        "D9E422594617C5C60D13C140DFB12B5F0AD1DE01F4A4C554D636C5544D1DDD5F")]
    [InlineData(false,
        "3A9957E559620B882F612A6549DFAF5D53AF0FD335BCAFE36657F84D96E89379",
        "4CAA4CF74CA2083A9BD4D50140D701E38FEF3F5C1465FFFB19FDB638E60D3B32")]
    public void Registered_settle_desert_lava_recolor_respects_remix_guard(
        bool remix, string expectedSeven, string expectedFull17)
    {
        var workspace = new Workspace(1000, 500);
        workspace.InitializeVanillaGenerationSolidity();
        Assert.True(workspace.TrySetLayers(140, 400));
        workspace.SetVanillaSeedProfile(new(
            remix ? VanillaSpecialWorldSeed1458.Remix : VanillaSpecialWorldSeed1458.None,
            VanillaSecretWorldSeed1458.None));
        workspace.SetVanillaLiquidLines(400, 180);

        for (int y = 190; y <= 260; y++)
        {
            Put(499, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            Put(502, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            if (y != 259)
                Put(501, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        }
        Put(500, 260, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        Put(501, 260, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        Put(500, 259, new WorldTile { Wall = 187 });
        Put(500, 200, new WorldTile { LiquidAmount = 200, LiquidKind = WorldLiquidKind.Lava });

        var random = new VanillaUnifiedRandom1458(1458);
        var framing = new GenerationTileFraming1458(workspace.TileStore, new RandomAdapter(random),
            workspace, useWorldGenerationSolidity: true);
        VanillaWaterCheckDiagnostic1458 result = framing.SettleLiquidsWithContext(
            400, workspace.IsVanillaGenerationSolid, workspace.SetVanillaGenerationSolid,
            (_, _, _) => false, () => { }, () => { }, TestContext.Current.CancellationToken);

        Assert.True(result.IsApplied, result.ToString());
        Assert.Equal((byte)100, workspace.TileStore.Get(500, 259).LiquidAmount);
        Assert.Equal((byte)100, workspace.TileStore.Get(501, 259).LiquidAmount);
        Assert.Equal(remix ? WorldLiquidKind.Water : WorldLiquidKind.Lava,
            workspace.TileStore.Get(500, 258).LiquidKind);
        string actualSeven = SevenFieldHash(workspace.TileStore);
        string actualFull17 = Full17Hash(workspace.TileStore);
        Assert.True(expectedSeven == actualSeven && expectedFull17 == actualFull17,
            $"remix={remix} seven={actualSeven}/{expectedSeven} full17={actualFull17}/{expectedFull17}");
        Assert.Equal(906992634, random.Next());
        Assert.Equal(0, workspace.TileStore.LiquidUpdates.ActiveCount);
        Assert.Equal(0, workspace.TileStore.LiquidUpdates.BufferedCount);

        void Put(int x, int y, WorldTile tile) => workspace.TileStore.Set(x, y, in tile);
    }

    private static string SevenFieldHash(WorldTileStore store)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (int x = 0; x < store.Dimensions.WidthTiles; x++)
        for (int y = 0; y < store.Dimensions.HeightTiles; y++)
        {
            WorldTile tile = store.Get(x, y);
            hash.AppendData(Encoding.UTF8.GetBytes($"{(tile.IsActive ? 1 : 0)},{tile.Type},{tile.Wall}," +
                $"{tile.FrameX},{tile.FrameY},{tile.LiquidAmount},{(byte)tile.LiquidKind};"));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string Full17Hash(WorldTileStore store)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> cell = stackalloc byte[17];
        foreach (WorldTile tile in store.Tiles)
        {
            cell[0] = (byte)(tile.IsActive ? 1 : 0);
            BinaryPrimitives.WriteUInt16LittleEndian(cell[1..3], tile.Type);
            BinaryPrimitives.WriteUInt16LittleEndian(cell[3..5], tile.Wall);
            BinaryPrimitives.WriteInt16LittleEndian(cell[5..7], tile.FrameX);
            BinaryPrimitives.WriteInt16LittleEndian(cell[7..9], tile.FrameY);
            cell[9] = tile.LiquidAmount;
            cell[10] = (byte)tile.LiquidKind;
            cell[11] = (byte)(tile.IsHalfBrick ? 1 : 0);
            cell[12] = tile.SourceSlope;
            cell[13] = tile.TileColor;
            cell[14] = tile.WallColor;
            ushort flags = 0;
            if ((tile.Flags & WorldTileFlags.WireRed) != 0) flags |= 1 << 0;
            if ((tile.Flags & WorldTileFlags.WireBlue) != 0) flags |= 1 << 1;
            if ((tile.Flags & WorldTileFlags.WireGreen) != 0) flags |= 1 << 2;
            if ((tile.Flags & WorldTileFlags.WireYellow) != 0) flags |= 1 << 3;
            if ((tile.Flags & WorldTileFlags.Actuator) != 0) flags |= 1 << 4;
            if ((tile.Flags & WorldTileFlags.Inactive) != 0) flags |= 1 << 5;
            if ((tile.Flags & WorldTileFlags.InvisibleBlock) != 0) flags |= 1 << 6;
            if ((tile.Flags & WorldTileFlags.FullbrightBlock) != 0) flags |= 1 << 7;
            if ((tile.Flags & WorldTileFlags.InvisibleWall) != 0) flags |= 1 << 8;
            if ((tile.Flags & WorldTileFlags.FullbrightWall) != 0) flags |= 1 << 9;
            BinaryPrimitives.WriteUInt16LittleEndian(cell[15..17], flags);
            hash.AppendData(cell);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private sealed class RandomAdapter(VanillaUnifiedRandom1458 source) : IWorldGenerationVanillaRandom
    {
        public int Next() => source.Next();
        public int Next(int max) => source.Next(max);
        public int Next(int min, int max) => source.Next(min, max);
        public double NextDouble() => source.NextDouble();
        public void NextBytes(byte[] bytes) => source.NextBytes(bytes);
    }
}
